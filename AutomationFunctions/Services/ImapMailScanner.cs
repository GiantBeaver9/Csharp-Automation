using System.Net;
using System.Text.RegularExpressions;
using AutomationFunctions.Options;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutomationFunctions.Services;

/// <summary>A single fetched message, reduced to the fields the summarizer needs.</summary>
public record ScannedEmail(string Account, string From, string Subject, DateTimeOffset Date, string Body);

public interface IMailScanner
{
    /// <summary>Fetches recent messages across all configured mailboxes (newest first).</summary>
    Task<IReadOnlyList<ScannedEmail>> FetchRecentAsync(CancellationToken ct = default);
}

/// <summary>Reads recent mail over IMAP using MailKit (works with Gmail, Outlook, etc.).</summary>
public partial class ImapMailScanner : IMailScanner
{
    private readonly MailScanOptions _options;
    private readonly ILogger<ImapMailScanner> _logger;

    public ImapMailScanner(IOptions<MailScanOptions> options, ILogger<ImapMailScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScannedEmail>> FetchRecentAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled || _options.Accounts.Count == 0)
        {
            return Array.Empty<ScannedEmail>();
        }

        var cutoff = DateTimeOffset.Now.AddHours(-_options.LookbackHours);
        var results = new List<ScannedEmail>();

        foreach (var account in _options.Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.ImapHost) || string.IsNullOrWhiteSpace(account.Username))
            {
                continue;
            }

            try
            {
                results.AddRange(await FetchAccountAsync(account, cutoff, ct));
            }
            catch (Exception ex)
            {
                // One bad mailbox shouldn't sink the whole digest.
                _logger.LogError(ex, "Failed scanning mailbox {Account}", account.Name);
            }
        }

        return results.OrderByDescending(e => e.Date).ToList();
    }

    private async Task<List<ScannedEmail>> FetchAccountAsync(
        MailAccountOptions account, DateTimeOffset cutoff, CancellationToken ct)
    {
        var list = new List<ScannedEmail>();

        using var client = new ImapClient();
        await client.ConnectAsync(
            account.ImapHost,
            account.ImapPort,
            account.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
            ct);
        await client.AuthenticateAsync(account.Username, account.Password, ct);

        var inbox = client.Inbox
            ?? throw new InvalidOperationException($"No inbox available for mailbox '{account.Name}'.");
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        // IMAP date search is day-granular; we refine by timestamp below.
        var uids = await inbox.SearchAsync(SearchQuery.DeliveredAfter(cutoff.Date), ct);
        var recent = uids.Reverse().Take(_options.MaxPerAccount).ToList();

        _logger.LogInformation(
            "Mailbox {Account}: fetching {Count} message(s) since {Cutoff}",
            account.Name, recent.Count, cutoff);

        foreach (var uid in recent)
        {
            ct.ThrowIfCancellationRequested();
            var msg = await inbox.GetMessageAsync(uid, ct);
            if (msg.Date < cutoff)
            {
                continue;
            }

            var body = msg.TextBody ?? StripHtml(msg.HtmlBody ?? string.Empty);
            list.Add(new ScannedEmail(
                Account: string.IsNullOrWhiteSpace(account.Name) ? account.Username : account.Name,
                From: msg.From.ToString(),
                Subject: string.IsNullOrWhiteSpace(msg.Subject) ? "(no subject)" : msg.Subject,
                Date: msg.Date,
                Body: body.Trim()));
        }

        await client.DisconnectAsync(true, ct);
        return list;
    }

    private static string StripHtml(string html)
    {
        var noTags = TagRegex().Replace(html, " ");
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(noTags), " ").Trim();
    }

    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagRegex();
    [GeneratedRegex(@"\s+")] private static partial Regex WhitespaceRegex();
}
