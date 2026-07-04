using System.Text.Json;

namespace DailySummary.Core.Models;

/// <summary>The kind of a section — selects an <c>ISectionGatherer</c> from the registry.</summary>
public enum SectionType
{
    Weather,
    Web,
    Rss,
    Podcast,
    Sql,
    GoogleCalendar,
    Question,
    Email,
    Prompt
}

/// <summary>Root configuration bound from <c>app.json</c>.</summary>
public sealed class AppConfig
{
    public ConcurrencyConfig Concurrency { get; set; } = new();
    public int ChannelCapacity { get; set; } = 16;
    public BrowserConfig Browser { get; set; } = new();

    /// <summary>Named summarizer backends. A section's <c>Summarizer</c> names one of these (or "none").</summary>
    public Dictionary<string, SummarizerConfig> Summarizers { get; set; } = new();

    /// <summary>Each digest is one scheduled run with its own sections and delivery.</summary>
    public List<DigestConfig> Digests { get; set; } = new();

    public DigestConfig? Digest(string name) =>
        Digests.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class ConcurrencyConfig
{
    /// <summary>Parallel fetch/gather lanes.</summary>
    public int Fetch { get; set; } = 7;

    /// <summary>LLM consumer lanes. Keep at 1 for local models.</summary>
    public int Summarize { get; set; } = 1;
}

public sealed class BrowserConfig
{
    public string Engine { get; set; } = "firefox";
    public bool Headed { get; set; } = true;
}

public sealed class SummarizerConfig
{
    public string? Model { get; set; }
    public string? ApiKeyEnv { get; set; }
    public string? Endpoint { get; set; }
}

public sealed class DigestConfig
{
    public string Name { get; set; } = "";
    public string Schedule { get; set; } = "0 0 6 * * *";
    public DeliveryConfig Delivery { get; set; } = new();
    public List<SectionConfig> Sections { get; set; } = new();
}

public sealed class DeliveryConfig
{
    /// <summary>Single-channel shorthand ("markdown" | "console" | "email" | "telegram").</summary>
    public string Channel { get; set; } = "console";

    /// <summary>Multi-channel: deliver to each (e.g. ["markdown","email"]). Takes precedence over <see cref="Channel"/>.</summary>
    public List<string>? Channels { get; set; }

    public string OutputDir { get; set; } = "./out";
    public EmailDeliveryConfig? Email { get; set; }
    public TelegramDeliveryConfig? Telegram { get; set; }

    /// <summary>The channels to deliver to — <see cref="Channels"/> if set, else the single <see cref="Channel"/>.</summary>
    public IReadOnlyList<string> ResolvedChannels() =>
        Channels is { Count: > 0 } ? Channels : new List<string> { Channel };
}

public sealed class EmailDeliveryConfig
{
    public string To { get; set; } = "";
    public string From { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string PasswordEnv { get; set; } = "SMTP_PASSWORD";
}

public sealed class TelegramDeliveryConfig
{
    /// <summary>Destination chat id (a user, group, or channel). Empty ⇒ Telegram delivery is skipped.</summary>
    public string ChatId { get; set; } = "";

    /// <summary>
    /// Env var holding the bot token (from @BotFather). Never put the token in app.json — this names the
    /// variable the delivery reads it from.
    /// </summary>
    public string TokenEnv { get; set; } = "TELEGRAM_BOT_TOKEN";
}

/// <summary>One section of a digest. <c>Settings</c> is a type-specific blob each gatherer parses.</summary>
public sealed class SectionConfig
{
    public SectionType Type { get; set; }
    public string Heading { get; set; } = "";
    public int Order { get; set; }

    /// <summary>Name of a configured summarizer, or "none" for the verbatim passthrough.</summary>
    public string Summarizer { get; set; } = "none";
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Optional prompt override; when null the default for <see cref="Type"/> is used.</summary>
    public string? Prompt { get; set; }

    /// <summary>Type-specific settings, parsed by the matching gatherer.</summary>
    public JsonElement Settings { get; set; }

    /// <summary>Deserialize <see cref="Settings"/> into a strongly-typed options object.</summary>
    public T SettingsAs<T>() where T : new() =>
        Settings.ValueKind == JsonValueKind.Undefined || Settings.ValueKind == JsonValueKind.Null
            ? new T()
            : Settings.Deserialize<T>(JsonOpts) ?? new T();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
