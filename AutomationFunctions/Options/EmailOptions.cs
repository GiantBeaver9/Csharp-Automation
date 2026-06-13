namespace AutomationFunctions.Options;

/// <summary>SMTP settings. Defaults target Gmail; works with any SMTP server.</summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = Constants.Email.SmtpHost;

    public int SmtpPort { get; set; } = Constants.Email.SmtpPort;

    /// <summary>Use STARTTLS (true, port 587) vs implicit SSL (false, port 465).</summary>
    public bool UseStartTls { get; set; } = Constants.Email.UseStartTls;

    public string Username { get; set; } = "";

    /// <summary>For Gmail/Outlook use an App Password, not your account password.</summary>
    public string Password { get; set; } = "";

    public string FromAddress { get; set; } = "";

    public string FromName { get; set; } = Constants.Email.FromName;

    /// <summary>Where reports are sent (e.g. your own inbox).</summary>
    public string ToAddress { get; set; } = "";
}
