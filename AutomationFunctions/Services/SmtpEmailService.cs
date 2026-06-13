using AutomationFunctions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AutomationFunctions.Services;

public interface IEmailService
{
    /// <summary>Sends an HTML email to the configured recipient.</summary>
    Task SendAsync(string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>Sends mail over SMTP using MailKit (modern TLS; pairs with the IMAP scanner).</summary>
public class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FromAddress) || string.IsNullOrWhiteSpace(_options.ToAddress))
        {
            throw new InvalidOperationException(
                "Email is not configured. Set Email__FromAddress and Email__ToAddress (and SMTP credentials).");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(_options.ToAddress));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var secureOptions = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;

        _logger.LogInformation("Sending email \"{Subject}\" to {To}", subject, _options.ToAddress);
        await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, secureOptions, ct);
        await client.AuthenticateAsync(_options.Username, _options.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
