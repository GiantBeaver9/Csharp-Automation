using System.Net;
using System.Net.Sockets;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Compute;
using DailySummary.Core.Models;
using DailySummary.Core.Pipeline;
using DailySummary.Core.Summarization;
using Microsoft.Azure.Functions.Worker;

namespace DailySummary.Functions.Durable;

/// <summary>
/// The side-effectful work, wrapping the existing Core/Providers services. Activities are where all
/// I/O and LLM calls live; the orchestrator only coordinates them.
/// </summary>
public sealed class DigestActivities
{
    private readonly AppConfig _app;
    private readonly SectionGathererRegistry _gatherers;
    private readonly SectionSummarizers _summarizers;
    private readonly ChunkedSummarizer _chunked;
    private readonly ISummaryRenderer _renderer;
    private readonly IReadOnlyDictionary<string, IDeliveryChannel> _delivery;
    private readonly HttpClient _http;

    public DigestActivities(
        AppConfig app,
        SectionGathererRegistry gatherers,
        SectionSummarizers summarizers,
        ChunkedSummarizer chunked,
        ISummaryRenderer renderer,
        IEnumerable<IDeliveryChannel> deliveryChannels,
        HttpClient http)
    {
        _app = app;
        _gatherers = gatherers;
        _summarizers = summarizers;
        _chunked = chunked;
        _renderer = renderer;
        _delivery = deliveryChannels.ToDictionary(d => d.Channel, StringComparer.OrdinalIgnoreCase);
        _http = http;
    }

    /// <summary>Returns the remote-compute config (or null when single-machine) for the orchestrator's gate.</summary>
    [Function(nameof(LoadComputeActivity))]
    public RemoteComputeConfig? LoadComputeActivity([ActivityTrigger] string _) => _app.RemoteCompute;

    /// <summary>Wake the compute PC by broadcasting a Wake-on-LAN magic packet. Best-effort, fire-and-forget.</summary>
    [Function(nameof(WakePcActivity))]
    public async Task WakePcActivity([ActivityTrigger] RemoteComputeConfig cfg)
    {
        var packet = MagicPacket.Build(cfg.MacAddress);
        using var udp = new UdpClient { EnableBroadcast = true };
        var endpoint = new IPEndPoint(IPAddress.Parse(cfg.BroadcastAddress), cfg.WolPort);
        await udp.SendAsync(packet, packet.Length, endpoint).ConfigureAwait(false);
    }

    /// <summary>
    /// True once the LLM server answers (LM Studio's model list). A loaded model is not required — the
    /// model loads on the first summarize call. Never throws; a down/booting PC just reads as not-ready.
    /// </summary>
    [Function(nameof(PcReadyActivity))]
    public async Task<bool> PcReadyActivity([ActivityTrigger] RemoteComputeConfig cfg)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var resp = await _http.GetAsync(cfg.ReadinessUrl, cts.Token).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [Function(nameof(LoadSectionsActivity))]
    public SectionConfig[] LoadSectionsActivity([ActivityTrigger] string digestName)
    {
        var digest = _app.Digest(digestName)
            ?? throw new KeyNotFoundException($"No digest named '{digestName}' in app.json.");
        return digest.Sections.ToArray();
    }

    [Function(nameof(GatherSectionActivity))]
    public async Task<RawPiece[]> GatherSectionActivity([ActivityTrigger] SectionConfig section)
    {
        using var cts = Timeout(section.TimeoutSeconds);
        try
        {
            var gatherer = _gatherers.Resolve(section.Type);
            var pieces = await gatherer.GatherAsync(section, cts.Token).ConfigureAwait(false);
            return pieces.ToArray();
        }
        catch (Exception ex)
        {
            return new[] { new RawPiece(section.Order, section.Heading, null, null, string.Empty, ex.Message) };
        }
    }

    [Function(nameof(SummarizePieceActivity))]
    public async Task<SummarizedPiece> SummarizePieceActivity([ActivityTrigger] PieceWork work)
    {
        var (section, piece) = (work.Section, work.Piece);
        if (piece.Error is not null)
            return new SummarizedPiece(section.Order, section.Heading, piece.SubHeading,
                $"_this call was unable to complete: {piece.Error}_", Failed: true);

        using var cts = Timeout(section.TimeoutSeconds);
        try
        {
            var pair = _summarizers.For(section, piece);
            var body = string.IsNullOrEmpty(piece.Text)
                ? await pair.Chunk(string.Empty, cts.Token).ConfigureAwait(false)
                : await _chunked.SummarizeAsync(piece.Text, pair.Chunk, pair.Final, cts.Token).ConfigureAwait(false);
            return new SummarizedPiece(section.Order, section.Heading, piece.SubHeading, body, Failed: false);
        }
        catch (Exception ex)
        {
            return new SummarizedPiece(section.Order, section.Heading, piece.SubHeading,
                $"_this call was unable to complete: {ex.Message}_", Failed: true);
        }
    }

    [Function(nameof(DeliverActivity))]
    public async Task DeliverActivity([ActivityTrigger] DeliverWork work)
    {
        var digest = _app.Digest(work.DigestName)
            ?? throw new KeyNotFoundException($"No digest named '{work.DigestName}' in app.json.");

        var rendered = _renderer.Render(work.Document);

        foreach (var name in digest.Delivery.ResolvedChannels())
        {
            if (!_delivery.TryGetValue(name, out var channel)) continue;
            try
            {
                await channel.DeliverAsync(rendered, digest.Delivery, work.DigestName, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Delivery channel '{name}' failed: {ex.Message}");
            }
        }
    }

    private static CancellationTokenSource Timeout(int seconds)
    {
        var cts = new CancellationTokenSource();
        if (seconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }
}
