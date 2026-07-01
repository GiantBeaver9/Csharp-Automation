using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using MailKit.Net.Smtp;
using MimeKit;

namespace DailySummary.Providers.Delivery;

/// <summary>Sends the digest as a multipart email: HTML body + plaintext (Markdown) fallback.</summary>
public sealed class EmailDelivery : IDeliveryChannel
{
    public string Channel => "email";

    public async Task DeliverAsync(RenderedSummary doc, DeliveryConfig config, CancellationToken ct)
    {
        var cfg = config.Email
            ?? throw new InvalidOperationException("delivery.email must be set when channel is 'email'.");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(cfg.From));
        message.To.Add(MailboxAddress.Parse(cfg.To));
        message.Subject = doc.Subject;
        message.Body = new BodyBuilder { HtmlBody = doc.Html, TextBody = doc.Markdown }.ToMessageBody();

        var password = Environment.GetEnvironmentVariable(cfg.PasswordEnv) ?? string.Empty;

        using var client = new SmtpClient();
        await client.ConnectAsync(cfg.SmtpHost, cfg.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls, ct)
            .ConfigureAwait(false);
        await client.AuthenticateAsync(cfg.From, password, ct).ConfigureAwait(false);
        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);
    }
}
