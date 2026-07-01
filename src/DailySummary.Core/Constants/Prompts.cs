using DailySummary.Core.Models;

namespace DailySummary.Core.Constants;

/// <summary>All prompt text lives here — no floating strings in the code.</summary>
public static class Prompts
{
    public const string Weather =
        "You are preparing a daily weather brief. Here is the data. " +
        "Please brief for morning, afternoon, and evening.";

    public const string News =
        "You are creating a summary for an end user on the daily headlines. " +
        "Please take the information and provide a summary.";

    public const string Misc =
        "You are summarizing miscellaneous site updates for an end user. " +
        "Please provide a short summary of what changed.";

    public const string Rss =
        "You are summarizing recent feed items for an end user. " +
        "Please provide a short digest of what's new.";

    public const string Todos =
        "You are tidying a list of calendar items into a clean to-do list for the day.";

    public const string Question =
        "Answer the question using only the provided sources. " +
        "If the sources do not answer it, say so plainly.";

    public const string Email =
        "Summarize the inbox into key items and any action items.";

    public const string Podcast =
        "Summarize this podcast transcript into the key points discussed.";

    /// <summary>Appended to the section prompt for the final fold pass.</summary>
    public const string ReduceSuffix =
        " The following are partial summaries; combine them into one final summary.";

    /// <summary>The default prompt for a section type when no override is given in config.</summary>
    public static string Default(SectionType type) => type switch
    {
        SectionType.Weather => Weather,
        SectionType.Web => News,
        SectionType.Rss => Rss,
        SectionType.Podcast => Podcast,
        SectionType.Sql => Todos,
        SectionType.GoogleCalendar => Todos,
        SectionType.Question => Question,
        SectionType.Email => Email,
        SectionType.Prompt => "", // prompt sections carry their instruction per-piece
        _ => "Summarize the following for an end user."
    };
}
