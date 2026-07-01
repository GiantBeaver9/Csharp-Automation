using System.Text;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using Ical.Net;
using Ical.Net.DataTypes;

namespace DailySummary.Providers.Gatherers;

public sealed class CalendarSettings
{
    public string IcalUrlEnv { get; set; } = "GCAL_ICAL_URL";
    public List<string> Views { get; set; } = new() { "today", "week" };
    public int WeekDays { get; set; } = 7;
    public string Timezone { get; set; } = "UTC";
}

/// <summary>
/// Google Calendar via the secret iCal (.ics) URL — no OAuth. Emits a "today" group and a per-day
/// "week" group (SubHeading = date) so the renderer shows dated headers. Pair with summarizer "none".
/// </summary>
public sealed class GoogleCalendarGatherer : ISectionGatherer
{
    private readonly HttpClient _http;

    public GoogleCalendarGatherer(HttpClient http) => _http = http;

    public SectionType Type => SectionType.GoogleCalendar;

    public async Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct)
    {
        var s = config.SettingsAs<CalendarSettings>();
        var url = Environment.GetEnvironmentVariable(s.IcalUrlEnv)
            ?? throw new InvalidOperationException($"Env var {s.IcalUrlEnv} (secret iCal URL) not set.");

        var ics = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        var calendar = Calendar.Load(ics);

        var pieces = new List<RawPiece>();
        var todayStart = DateTime.Today;

        if (s.Views.Any(v => v.Equals("today", StringComparison.OrdinalIgnoreCase)))
        {
            var body = Events(calendar, todayStart, todayStart.AddDays(1));
            pieces.Add(new RawPiece(config.Order, config.Heading, "To do today", null,
                body.Length == 0 ? "(nothing scheduled today)" : body));
        }

        if (s.Views.Any(v => v.Equals("week", StringComparison.OrdinalIgnoreCase)))
        {
            for (var i = 0; i < s.WeekDays; i++)
            {
                var day = todayStart.AddDays(i);
                var body = Events(calendar, day, day.AddDays(1));
                if (body.Length == 0) continue;
                pieces.Add(new RawPiece(config.Order, config.Heading, day.ToString("ddd, MMM d"), null, body));
            }
        }

        return pieces;
    }

    private static string Events(Calendar calendar, DateTime from, DateTime to)
    {
        var occurrences = calendar
            .GetOccurrences(new CalDateTime(from), new CalDateTime(to))
            .OrderBy(o => o.Period.StartTime.Value);

        var sb = new StringBuilder();
        foreach (var occ in occurrences)
        {
            var title = (occ.Source as Ical.Net.CalendarComponents.CalendarEvent)?.Summary ?? "(event)";
            sb.AppendLine($"- {occ.Period.StartTime.Value:HH:mm} {title}");
        }
        return sb.ToString().TrimEnd();
    }
}
