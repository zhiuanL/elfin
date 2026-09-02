using System.Globalization;
using System.Text;
using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Productivity;

public sealed class StatisticsService(IPomodoroRepository sessions, ITaskRepository tasks, ITagRepository tags) : IStatisticsService
{
    public async Task<StatisticsSummary> QueryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var rows = await sessions.ListAsync(fromUtc, toUtc, ct);
        var focus = rows.Where(IsCountedFocus).ToArray();
        return new(TimeSpan.FromSeconds(focus.Sum(x => x.ActualDuration.TotalSeconds)),
            focus.Count(x => x.Status == PomodoroStatus.Completed), 0);
    }
    public async Task<ProductivityStatistics> GetOverviewAsync(DateOnly localToday, TimeZoneInfo timeZone, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var first = localToday.AddDays(-370);
        var fromUtc = LocalStart(first, timeZone);
        var toUtc = LocalStart(localToday.AddDays(1), timeZone);
        var rows = (await sessions.ListAsync(fromUtc, toUtc, ct)).Where(IsCountedFocus).ToArray();
        var byDay = rows.GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.StartedAtUtc, timeZone).DateTime))
            .ToDictionary(g => g.Key, g => Point(g.Key, g));
        var daily = Enumerable.Range(0, 7).Select(i => localToday.AddDays(i - 6))
            .Select(day => byDay.GetValueOrDefault(day) ?? new(day, TimeSpan.Zero, 0)).ToArray();
        var weekly = Enumerable.Range(0, 4).Select(week =>
        {
            var end = localToday.AddDays(-week * 7); var start = end.AddDays(-6);
            var selected = rows.Where(x => InLocalRange(x, start, end, timeZone));
            return Point(start, selected);
        }).Reverse().ToArray();
        var monthly = Enumerable.Range(0, 6).Select(month =>
        {
            var marker = localToday.AddMonths(month - 5);
            var start = new DateOnly(marker.Year, marker.Month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            return Point(start, rows.Where(x => InLocalRange(x, start, end, timeZone)));
        }).ToArray();
        var todayRows = rows.Where(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.StartedAtUtc, timeZone).DateTime) == localToday).ToArray();
        var completedDays = rows.Where(x => x.Status == PomodoroStatus.Completed)
            .Select(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.StartedAtUtc, timeZone).DateTime)).ToHashSet();
        var streak = 0;
        for (var day = localToday; completedDays.Contains(day); day = day.AddDays(-1)) streak++;
        var allTasks = await tasks.ListAsync(true, ct);
        var taskStats = allTasks.Select(task => Named(task.Id, task.Title, rows.Where(x => x.TaskId == task.Id)))
            .Where(x => x.FocusDuration > TimeSpan.Zero).OrderByDescending(x => x.FocusDuration).ToArray();
        var allTags = await tags.ListAsync(ct);
        var tagStats = new List<NamedFocusStatistic>();
        foreach (var tag in allTags)
        {
            var taskIds = new HashSet<Guid>();
            foreach (var task in allTasks)
                if ((await tasks.GetTagsAsync(task.Id, ct)).Any(x => x.Id == tag.Id)) taskIds.Add(task.Id);
            var stat = Named(tag.Id, tag.Name, rows.Where(x => x.TaskId is { } id && taskIds.Contains(id)));
            if (stat.FocusDuration > TimeSpan.Zero) tagStats.Add(stat);
        }
        return new(TimeSpan.FromSeconds(todayRows.Sum(x => x.ActualDuration.TotalSeconds)),
            todayRows.Count(x => x.Status == PomodoroStatus.Completed), daily, weekly, monthly, streak,
            taskStats, tagStats.OrderByDescending(x => x.FocusDuration).ToArray());
    }
    private static bool IsCountedFocus(PomodoroSession x) => x.Phase == PomodoroPhase.Focus &&
        x.Status is PomodoroStatus.Completed or PomodoroStatus.Stopped && x.ActualDuration > TimeSpan.Zero;
    private static DateTimeOffset LocalStart(DateOnly day, TimeZoneInfo zone) =>
        ReminderScheduleCalculator.ResolveLocal(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
    private static bool InLocalRange(PomodoroSession x, DateOnly start, DateOnly end, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.StartedAtUtc, zone).DateTime);
        return day >= start && day <= end;
    }
    private static TrendPoint Point(DateOnly key, IEnumerable<PomodoroSession> rows)
    {
        var array = rows.ToArray();
        return new(key, TimeSpan.FromSeconds(array.Sum(x => x.ActualDuration.TotalSeconds)),
            array.Count(x => x.Status == PomodoroStatus.Completed));
    }
    private static NamedFocusStatistic Named(Guid id, string name, IEnumerable<PomodoroSession> rows)
    {
        var array = rows.ToArray();
        return new(id, name, TimeSpan.FromSeconds(array.Sum(x => x.ActualDuration.TotalSeconds)),
            array.Count(x => x.Status == PomodoroStatus.Completed));
    }
}

public sealed class CsvStatisticsExporter(IStatisticsService statistics) : IStatisticsExporter
{
    public async Task ExportCsvAsync(Stream destination, DateOnly from, DateOnly to, TimeZoneInfo timeZone, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        await using var writer = new StreamWriter(destination, new UTF8Encoding(true), leaveOpen: true);
        await writer.WriteLineAsync("Date,FocusMinutes,CompletedPomodoros".AsMemory(), ct);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var view = await statistics.GetOverviewAsync(day, timeZone, ct);
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"{day:yyyy-MM-dd},{view.TodayFocusDuration.TotalMinutes:F2},{view.TodayCompletedPomodoros}").AsMemory(), ct);
        }
        await writer.FlushAsync(ct);
    }
}
