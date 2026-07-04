using System.Net.Http.Json;
using System.Text.Json.Serialization;
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

    private readonly HttpClient _http;

    public TelegramDelivery(HttpClient http) => _http = http;

    public string Channel => "telegram";

    public async Task DeliverAsync(RenderedSummary doc, DeliveryConfig config, string digestName, CancellationToken ct)
    {
        var cfg = config.Telegram
            ?? throw new InvalidOperationException("delivery.telegram must be set when channel is 'telegram'.");

        var token = Environment.GetEnvironmentVariable(cfg.TokenEnv);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"Telegram bot token env var '{cfg.TokenEnv}' is empty. Create a bot with @BotFather and set it.");

        var chatId = string.IsNullOrWhiteSpace(cfg.ChatId)
            ? Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID")
            : cfg.ChatId;
        if (string.IsNullOrWhiteSpace(chatId))
            throw new InvalidOperationException(
                "Telegram chat id is not set (delivery.telegram.chatId or the TELEGRAM_CHAT_ID env var).");

        var url = $"https://api.telegram.org/bot{token}/sendMessage";
        foreach (var chunk in Chunks(doc.Markdown, MessageLimit))
        {
            using var resp = await _http.PostAsJsonAsync(url, new SendMessage(chatId, chunk), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                // The url carries the bot token, so keep it out of the error — report status + Telegram's reason only.
                throw new InvalidOperationException(
                    $"Telegram sendMessage returned {(int)resp.StatusCode}: {Truncate(body, 500)}");
            }
        }
    }

    /// <summary>
    /// Splits <paramref name="text"/> into slices of at most <paramref name="limit"/> UTF-16 code units,
    /// never cutting through a surrogate pair (so an emoji is never torn across two messages). Whitespace-only
    /// or empty input yields nothing. Pure and static, so it's unit-testable.
    /// </summary>
    internal static IEnumerable<string> Chunks(string text, int limit)
    {
        if (string.IsNullOrEmpty(text)) yield break;

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

    private sealed record SendMessage(
        [property: JsonPropertyName("chat_id")] string ChatId,
        [property: JsonPropertyName("text")] string Text);
}
