using System.Net;
using System.Net.Mail;
using AutomationFunctions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutomationFunctions.Services;

public interface IEmailService
{
    /// <summary>Sends an HTML email to the configured recipient.</summary>
    Task SendAsync(string subject, string htmlBody, CancellationToken ct = default);
}

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

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(_options.ToAddress);

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = true, // STARTTLS on 587 or implicit SSL on 465
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(_options.Username, _options.Password),
        };

        _logger.LogInformation("Sending email \"{Subject}\" to {To}", subject, _options.ToAddress);
        ct.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, ct);
    }
}
