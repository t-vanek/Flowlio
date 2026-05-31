namespace Flowlio.Server.Endpoints;

/// <summary>Calendar date-range primitives shared by the dashboard period switcher and budget periods.
/// All windows are half-open [Start, End).</summary>
internal static class DatePeriods
{
    /// <summary>The calendar month containing <paramref name="today"/>.</summary>
    public static (DateOnly Start, DateOnly End) MonthWindow(DateOnly today) =>
        (new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, 1).AddMonths(1));

    /// <summary>The calendar year containing <paramref name="today"/>.</summary>
    public static (DateOnly Start, DateOnly End) YearWindow(DateOnly today) =>
        (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year + 1, 1, 1));

    /// <summary>The ISO (Monday-based) week containing <paramref name="today"/>.</summary>
    public static (DateOnly Start, DateOnly End) WeekWindow(DateOnly today)
    {
        var offset = ((int)today.DayOfWeek + 6) % 7;
        var monday = today.AddDays(-offset);
        return (monday, monday.AddDays(7));
    }

    /// <summary>First day of the calendar quarter containing <paramref name="d"/>.</summary>
    public static DateOnly QuarterStart(DateOnly d) => new(d.Year, (d.Month - 1) / 3 * 3 + 1, 1);

    /// <summary>Whole months from <paramref name="today"/> to <paramref name="target"/>, at least 1.</summary>
    public static int MonthsUntil(DateOnly today, DateOnly target) =>
        Math.Max((target.Year - today.Year) * 12 + (target.Month - today.Month), 1);
}
