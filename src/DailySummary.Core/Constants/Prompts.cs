using DailySummary.Core.Models;

namespace DailySummary.Core.Constants;

/// <summary>All prompt text lives here — no floating strings in the code.</summary>
public static class Prompts
{
    public const string Weather =
        "You are preparing a daily weather brief. Here is the data. " +
        "Please brief for morning, afternoon, and evening.";

    /// <summary>
    /// Self-contained prompt for the SEPARATE link-selection pass. Runs as its own LLM call over just
    /// the links list (clean context) — no page body, no summary — so it only picks and links.
    /// </summary>
    public const string LinkSelection =
        "Below is a list of links, each formatted as \"headline — url — context\". " +
        "Select ONLY the 3-5 most impactful/important stories (skip trivia and human-interest fluff " +
        "like \"man walks 400 miles\"). Output just a markdown bullet list of those, each as [headline](url). " +
        "Use the exact url shown for each — never invent, guess, or modify a URL. Output nothing else.";

    public const string News =
        "You are summarizing the day's top headlines for an end user. " +
        "Provide a few concise bullet points covering the most important items.";

    public const string Misc =
        "You are summarizing miscellaneous site updates for an end user. " +
        "Please provide a short summary of what changed.";

    public const string Rss =
        "You are summarizing recent feed items for an end user. " +
        "Provide a short bulleted digest of what's new.";

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
