using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Delivery;

/// <summary>
/// Sends the Markdown digest to a Telegram chat via the Bot API (<c>sendMessage</c>). Telegram caps a
/// message at 4096 UTF-16 code units, so long digests are split across several messages. Sent as plain
/// text (no <c>parse_mode</c>): the Markdown stays human-readable and we avoid MarkdownV2's escaping rules,
/// which would reject the digest's <c>#</c>/<c>-</c>/<c>.</c> characters outright.
/// </summary>
public sealed class TelegramDelivery : IDeliveryChannel
{
    // Telegram's per-message limit, counted in UTF-16 code units (an emoji is 2). A C# string's Length is
    // already that count, so no re-encoding is needed — unlike the Python original, whose str is code points.
    private const int MessageLimit = 4096;

    // 429 backoff is bounded so a persistently throttled bot can't retry forever.
    private const int MaxRetries = 5;

    private readonly HttpClient _http;

    public TelegramDelivery(HttpClient http) => _http = http;

    public string Channel => "telegram";

    public async Task DeliverAsync(RenderedSummary doc, DeliveryConfig config, string digestName, CancellationToken ct)
    {
        var cfg = config.Telegram
            ?? throw new InvalidOperationException("delivery.telegram must be set when channel is 'telegram'.");

        // No chat configured → nothing to deliver to; skip silently.
        if (string.IsNullOrWhiteSpace(cfg.ChatId))
            return;

        var token = Environment.GetEnvironmentVariable(cfg.TokenEnv);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"Telegram bot token env var '{cfg.TokenEnv}' is empty. Create a bot with @BotFather and set it.");

        var url = $"https://api.telegram.org/bot{token}/sendMessage";
        foreach (var chunk in Chunks(doc.Markdown, MessageLimit))
        {
            var retries = 0;
            var sent = false;
            while (!sent)
            {
                // Telegram documents chat_id as "Integer or String", so a numeric id or @channelusername
                // both travel fine as a string. Anonymous payload matches how the summarizers POST JSON here.
                using var resp = await _http.PostAsJsonAsync(
                    url, new { chat_id = cfg.ChatId, text = chunk }, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    sent = true;
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                    // 429 = flood control. Telegram returns the wait in parameters.retry_after; honor it
                    // (plus jitter) and retry the SAME chunk, up to MaxRetries. Anything else throws.
                    if (resp.StatusCode == HttpStatusCode.TooManyRequests &&
                        retries < MaxRetries &&
                        TryGetRetryAfter(body) is { } retryAfter)
                    {
                        retries++;
                        await DelayWithJitter(TimeSpan.FromSeconds(retryAfter), ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // The url carries the bot token, so keep it out of the error — status + reason only.
                        throw new InvalidOperationException(
                            $"Telegram sendMessage returned {(int)resp.StatusCode}: {Truncate(body, 500)}");
                    }
                }
            }

            // Pace ~1 msg/sec to one chat (Telegram's per-chat limit), with jitter. Fires after every send,
            // including the last — one idle second on a background job isn't worth a branch to skip it.
            await DelayWithJitter(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Splits <paramref name="text"/> into slices of at most <paramref name="limit"/> UTF-16 code units,
    /// never cutting through a surrogate pair (so an emoji is never torn across two messages). Whitespace-only
    /// or empty input yields nothing. Pure and static, so it's unit-testable.
    /// </summary>
    internal static IEnumerable<string> Chunks(string text, int limit)
    {
        if (limit < 2) throw new ArgumentOutOfRangeException(
            nameof(limit), "limit must be >= 2 (a surrogate pair is 2 UTF-16 units).");
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var i = 0;
        while (i < text.Length)
        {
            var len = Math.Min(limit, text.Length - i);
            // If the slice would end on a high surrogate (the first half of a pair), pull back one unit so the
            // whole pair moves to the next chunk. limit >= 2 keeps len >= 1, so this always makes progress.
            if (i + len < text.Length && char.IsHighSurrogate(text[i + len - 1]))
                len--;
            yield return text.Substring(i, len);
            i += len;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>Adds 0–500 ms of jitter on top of a delay (only ever adds, so a 429 retry can't fire early).</summary>
    private static Task DelayWithJitter(TimeSpan baseDelay, CancellationToken ct) =>
        Task.Delay(baseDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);

    /// <summary>Reads parameters.retry_after (seconds) from a Telegram error body, or null if absent/non-JSON.</summary>
    internal static int? TryGetRetryAfter(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("parameters", out var p) &&
                p.TryGetProperty("retry_after", out var r) &&
                r.TryGetInt32(out var seconds))
                return seconds;
        }
        catch (JsonException) { /* non-JSON body → no retry hint */ }
        return null;
    }
}
