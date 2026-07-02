using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Gatherers;

public sealed class OutlookSettings
{
    /// <summary>Azure AD app (public client) registration id. Not a secret — safe in app.json.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Authority tenant. "consumers" for personal outlook.com/hotmail/live accounts, "common" for
    /// personal-or-work. The app registration's supported account types must include personal accounts.
    /// </summary>
    public string TenantId { get; set; } = "consumers";

    /// <summary>Well-known folder name ("inbox", "junkemail", ...) or a folder id.</summary>
    public string Folder { get; set; } = "inbox";

    /// <summary>How far back to read, in hours.</summary>
    public int LookbackHours { get; set; } = 24;

    /// <summary>When true, only unread messages are read.</summary>
    public bool UnreadOnly { get; set; } = false;

    /// <summary>Cap on messages pulled (paged if the window holds more).</summary>
    public int MaxMessages { get; set; } = 50;

    /// <summary>
    /// Delegated Graph scopes. Mail.ReadWrite covers reading now and the future move/delete deleter.
    /// (Azure.Identity adds offline_access automatically so refresh tokens are issued.)
    /// </summary>
    public List<string> Scopes { get; set; } = new() { "https://graph.microsoft.com/Mail.ReadWrite" };

    /// <summary>
    /// MSAL persistent token-cache name (holds the refresh token so later runs are silent).
    /// </summary>
    public string CacheName { get; set; } = "DailySummary.Outlook";

    /// <summary>File holding the serialized AuthenticationRecord (account identity for silent auth).</summary>
    public string AuthRecordPath { get; set; } = ".outlook-auth.json";
}

/// <summary>
/// Personal Outlook mailbox via Microsoft Graph, delegated device-code auth (no client secret): the
/// first run prints a code to sign in once, later runs refresh silently from the persisted cache.
/// One RawPiece per message, folded into a single inbox digest. Reusable now for the summary; the
/// same Mail.ReadWrite credential backs the future spam auto-deleter.
/// </summary>
public sealed class OutlookGatherer : ISectionGatherer
{
    private readonly HttpClient _http;

    public OutlookGatherer(HttpClient http) => _http = http;

    public SectionType Type => SectionType.Outlook;

    public async Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct)
    {
        var s = config.SettingsAs<OutlookSettings>();
        if (string.IsNullOrWhiteSpace(s.ClientId))
            throw new InvalidOperationException(
                "OutlookSettings.ClientId is required (the Azure app registration's Application (client) ID).");

        var token = await AcquireTokenAsync(s, ct).ConfigureAwait(false);

        var since = DateTimeOffset.UtcNow.AddHours(-Math.Abs(s.LookbackHours));
        var url = BuildFirstPageUrl(s, since);

        var pieces = new List<RawPiece>();
        while (url is not null && pieces.Count < s.MaxMessages)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            // Ask Graph to return plain-text bodies instead of HTML — no stripping needed.
            req.Headers.TryAddWithoutValidation("Prefer", "outlook.body-content-type=\"text\"");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Graph returned {(int)resp.StatusCode}: {Truncate(json, 500)}");

            var page = JsonSerializer.Deserialize<GraphMessagePage>(json, JsonOpts);
            foreach (var m in page?.Value ?? new List<GraphMessage>())
            {
                if (pieces.Count >= s.MaxMessages) break;
                pieces.Add(new RawPiece(config.Order, config.Heading, null, null, FormatMessage(m)));
            }
            url = page?.NextLink;
        }

        if (pieces.Count == 0)
            pieces.Add(new RawPiece(config.Order, config.Heading, null, null,
                $"(no messages in the last {s.LookbackHours} hours)"));

        return pieces;
    }

    /// <summary>Graph messages URL for the first page. Split out so the query shape is unit-testable.</summary>
    internal static string BuildFirstPageUrl(OutlookSettings s, DateTimeOffset since)
    {
        var iso = since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var filter = $"receivedDateTime ge {iso}";
        if (s.UnreadOnly) filter += " and isRead eq false";

        var query =
            $"$filter={Uri.EscapeDataString(filter)}" +
            "&$orderby=receivedDateTime desc" +
            $"&$top={Math.Clamp(s.MaxMessages, 1, 1000)}" +
            "&$select=subject,from,receivedDateTime,bodyPreview,isRead,body";

        return $"https://graph.microsoft.com/v1.0/me/mailFolders/{Uri.EscapeDataString(s.Folder)}/messages?{query}";
    }

    /// <summary>One message → the text handed to the summarizer. Pure, so it's unit-testable.</summary>
    internal static string FormatMessage(GraphMessage m)
    {
        var sender = m.From?.EmailAddress is { } a
            ? string.IsNullOrWhiteSpace(a.Name) ? a.Address : $"{a.Name} <{a.Address}>"
            : "(unknown)";
        var received = m.ReceivedDateTime?.ToString("ddd, MMM d HH:mm") ?? "";
        var flag = m.IsRead ? "" : "[unread] ";
        return $"{flag}From: {sender}\nReceived: {received}\nSubject: {m.Subject}\n\n{BodyText(m)}";
    }

    /// <summary>
    /// Best plain-text body. The Prefer header usually yields text, but Graph is documented to
    /// sometimes still return HTML — strip it defensively so the summarizer isn't fed markup.
    /// Falls back to the always-plain bodyPreview when there's no usable body.
    /// </summary>
    internal static string BodyText(GraphMessage m)
    {
        var content = m.Body?.Content;
        if (string.IsNullOrWhiteSpace(content))
            return (m.BodyPreview ?? "").Trim();
        return string.Equals(m.Body?.ContentType, "html", StringComparison.OrdinalIgnoreCase)
            ? StripHtml(content)
            : content.Trim();
    }

    private static string StripHtml(string html)
    {
        var noBlocks = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var noTags = Regex.Replace(noBlocks, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, "\\s+", " ").Trim();
    }

    private async Task<string> AcquireTokenAsync(OutlookSettings s, CancellationToken ct)
    {
        var options = new DeviceCodeCredentialOptions
        {
            ClientId = s.ClientId,
            TenantId = s.TenantId,
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = s.CacheName,
                UnsafeAllowUnencryptedStorage = true, // fallback for servers/containers without a keyring
            },
            DeviceCodeCallback = (info, _) =>
            {
                Console.WriteLine(info.Message); // first-run sign-in instructions land in the function logs
                return Task.CompletedTask;
            },
        };

        // Reuse the saved account identity so later runs authenticate silently from the cache.
        if (File.Exists(s.AuthRecordPath))
        {
            await using var read = File.OpenRead(s.AuthRecordPath);
            options.AuthenticationRecord = await AuthenticationRecord.DeserializeAsync(read, ct).ConfigureAwait(false);
        }

        var credential = new DeviceCodeCredential(options);
        var context = new TokenRequestContext(s.Scopes.ToArray());

        if (options.AuthenticationRecord is null)
        {
            var record = await credential.AuthenticateAsync(context, ct).ConfigureAwait(false);
            await using var write = File.Create(s.AuthRecordPath);
            await record.SerializeAsync(write, ct).ConfigureAwait(false);
        }

        var token = await credential.GetTokenAsync(context, ct).ConfigureAwait(false);
        return token.Token;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // --- Graph JSON shapes (only the fields we $select) ---

    internal sealed class GraphMessagePage
    {
        public List<GraphMessage>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    internal sealed class GraphMessage
    {
        public string? Subject { get; set; }
        public string? BodyPreview { get; set; }
        public bool IsRead { get; set; }
        public DateTimeOffset? ReceivedDateTime { get; set; }
        public GraphFrom? From { get; set; }
        public GraphBody? Body { get; set; }
    }

    internal sealed class GraphFrom
    {
        public GraphEmailAddress? EmailAddress { get; set; }
    }

    internal sealed class GraphEmailAddress
    {
        public string? Name { get; set; }
        public string? Address { get; set; }
    }

    internal sealed class GraphBody
    {
        public string? ContentType { get; set; }
        public string? Content { get; set; }
    }
}
