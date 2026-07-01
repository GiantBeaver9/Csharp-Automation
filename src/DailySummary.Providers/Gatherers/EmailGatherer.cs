using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;

namespace DailySummary.Providers.Gatherers;

public sealed class EmailSettings
{
    public string ImapHost { get; set; } = "imap.gmail.com";
    public int ImapPort { get; set; } = 993;
    public string Username { get; set; } = "";
    public string PasswordEnv { get; set; } = "GMAIL_APP_PASSWORD";
    public string Mailbox { get; set; } = "INBOX";
    public bool UnreadOnly { get; set; } = true;
    public string Since { get; set; } = "today";
    public int MaxMessages { get; set; } = 50;
}

/// <summary>Gmail via IMAP + app password (no OAuth). One RawPiece per message, folded to one inbox digest.</summary>
public sealed class EmailGatherer : ISectionGatherer
{
    public SectionType Type => SectionType.Email;

    public async Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct)
    {
        var s = config.SettingsAs<EmailSettings>();
        var password = Environment.GetEnvironmentVariable(s.PasswordEnv)
            ?? throw new InvalidOperationException($"Env var {s.PasswordEnv} not set.");

        using var client = new ImapClient();
        await client.ConnectAsync(s.ImapHost, s.ImapPort, MailKit.Security.SecureSocketOptions.SslOnConnect, ct)
            .ConfigureAwait(false);
        await client.AuthenticateAsync(s.Username, password, ct).ConfigureAwait(false);

        var inbox = client.GetFolder(s.Mailbox);
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);

        var since = s.Since.Equals("today", StringComparison.OrdinalIgnoreCase)
            ? DateTime.Today
            : DateTime.Today.AddDays(-1);
        SearchQuery query = SearchQuery.DeliveredAfter(since);
        if (s.UnreadOnly) query = query.And(SearchQuery.NotSeen);

        var uids = await inbox.SearchAsync(query, ct).ConfigureAwait(false);
        var pieces = new List<RawPiece>();

        foreach (var uid in uids.Take(s.MaxMessages))
        {
            var msg = await inbox.GetMessageAsync(uid, ct).ConfigureAwait(false);
            var body = msg.TextBody ?? msg.HtmlBody ?? string.Empty;
            var text = $"From: {msg.From}\nSubject: {msg.Subject}\n\n{body}";
            pieces.Add(new RawPiece(config.Order, config.Heading, null, null, text));
        }

        await client.DisconnectAsync(true, ct).ConfigureAwait(false);
        return pieces;
    }
}
