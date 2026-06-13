namespace AutomationFunctions.Options;

/// <summary>Settings for scanning one or more mailboxes over IMAP.</summary>
public class MailScanOptions
{
    public const string SectionName = "MailScan";

    /// <summary>Master switch; when false the digest skips the inbox section entirely.</summary>
    public bool Enabled { get; set; } = Constants.MailScan.Enabled;

    /// <summary>How far back to look for messages.</summary>
    public int LookbackHours { get; set; } = Constants.MailScan.LookbackHours;

    /// <summary>Cap on messages fetched per mailbox (keeps LLM calls bounded).</summary>
    public int MaxPerAccount { get; set; } = Constants.MailScan.MaxPerAccount;

    public List<MailAccountOptions> Accounts { get; set; } = new();
}

public class MailAccountOptions
{
    /// <summary>Friendly label shown in the digest, e.g. "Gmail" or "Outlook".</summary>
    public string Name { get; set; } = "";

    public string ImapHost { get; set; } = "";

    public int ImapPort { get; set; } = Constants.MailScan.ImapPort;

    /// <summary>true = implicit SSL (993); false = STARTTLS (143).</summary>
    public bool UseSsl { get; set; } = Constants.MailScan.UseSsl;

    public string Username { get; set; } = "";

    /// <summary>App Password for Gmail/Outlook (not the account login password).</summary>
    public string Password { get; set; } = "";
}
