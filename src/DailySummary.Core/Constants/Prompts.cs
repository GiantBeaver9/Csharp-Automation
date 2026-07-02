using DailySummary.Core.Models;

namespace DailySummary.Core.Constants;

/// <summary>All prompt text lives here — no floating strings in the code.</summary>
public static class Prompts
{
    public const string Weather =
        "You are preparing a daily weather brief. Here is the data. " +
        "Please brief for morning, afternoon, and evening.";

    /// <summary>Appended to news/feed prompts: cite the few most important stories as links.</summary>
    public const string TopLinks =
        " Then add a \"Top Stories\" list: pick ONLY the 3-5 most impactful/important items " +
        "(skip trivia and human-interest fluff like \"man walks 400 miles\") and list them as markdown " +
        "links — [headline](url). Use ONLY entries from the LINKS list in the source text: the link text " +
        "there is the real headline and the url is its real address — pair them exactly, do not invent, " +
        "guess, or modify any URL. If an item has no matching link, mention it without a link.";

    public const string News =
        "You are creating a summary for an end user on the daily headlines. " +
        "Please take the information and provide a summary." + TopLinks;

    public const string Misc =
        "You are summarizing miscellaneous site updates for an end user. " +
        "Please provide a short summary of what changed.";

    public const string Rss =
        "You are summarizing recent feed items for an end user. " +
        "Please provide a short digest of what's new." + TopLinks;

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
