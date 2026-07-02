using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using MailKit.Net.Smtp;
using MimeKit;

namespace DailySummary.Providers.Delivery;

/// <summary>Sends the digest as a multipart email: HTML body + plaintext (Markdown) fallback.</summary>
public sealed class EmailDelivery : IDeliveryChannel
{
    public string Channel => "email";

    public async Task DeliverAsync(RenderedSummary doc, DeliveryConfig config, string digestName, CancellationToken ct)
    {
        var cfg = config.Email
            ?? throw new InvalidOperationException("delivery.email must be set when channel is 'email'.");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(cfg.From));
        message.To.Add(MailboxAddress.Parse(cfg.To));
        message.Subject = doc.Subject;
        message.Body = new BodyBuilder { HtmlBody = doc.Html, TextBody = doc.Markdown }.ToMessageBody();

        // Gmail App Passwords are shown as "abcd efgh ijkl mnop" — strip spaces so a copy/paste works.
        var password = (Environment.GetEnvironmentVariable(cfg.PasswordEnv) ?? string.Empty).Replace(" ", "");
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException(
                $"Email password env var '{cfg.PasswordEnv}' is empty. For Gmail: enable 2-Step Verification " +
                "and generate a 16-character App Password (a normal account password will NOT work).");

        using var client = new SmtpClient();
        // Auto picks StartTls for port 587 and SSL-on-connect for 465 — works regardless of which port is set.
        await client.ConnectAsync(cfg.SmtpHost, cfg.SmtpPort, MailKit.Security.SecureSocketOptions.Auto, ct)
            .ConfigureAwait(false);
        await client.AuthenticateAsync(cfg.From, password, ct).ConfigureAwait(false);
        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);
    }
}
