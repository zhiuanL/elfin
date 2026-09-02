using System.Collections.ObjectModel;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.App.ViewModels;

public sealed class StatisticsViewModel(IStatisticsService statistics, ITextLocalizer text) : ObservableViewModel
{
    private ProductivityStatistics? _value;
    public string Title => text.Get(TextKey.StatisticsTitle);
    public string Subtitle => text.Get(TextKey.StatisticsSubtitle);
    public string TodayFocus => $"{text.Get(TextKey.StatisticsTodayFocus)}: {_value?.TodayFocusDuration.TotalMinutes:F0} min";
    public string TodayCompleted => $"{text.Get(TextKey.StatisticsTodayCompleted)}: {_value?.TodayCompletedPomodoros ?? 0}";
    public string Streak => $"{text.Get(TextKey.StatisticsStreak)}: {_value?.FocusStreakDays ?? 0}";
    public string DailyTitle => text.Get(TextKey.StatisticsDaily);
    public string WeeklyTitle => text.Get(TextKey.StatisticsWeekly);
    public string MonthlyTitle => text.Get(TextKey.StatisticsMonthly);
    public string TaskTitle => text.Get(TextKey.StatisticsTaskSummary);
    public string TagTitle => text.Get(TextKey.StatisticsTagSummary);
    public ObservableCollection<string> Daily { get; } = [];
    public ObservableCollection<string> Weekly { get; } = [];
    public ObservableCollection<string> Monthly { get; } = [];
    public ObservableCollection<string> Tasks { get; } = [];
    public ObservableCollection<string> Tags { get; } = [];
    public async Task InitializeAsync()
    {
        _value = await statistics.GetOverviewAsync(DateOnly.FromDateTime(DateTime.Now), TimeZoneInfo.Local, CancellationToken.None);
        Fill(Daily, _value.DailyTrend); Fill(Weekly, _value.WeeklyTrend); Fill(Monthly, _value.MonthlyTrend);
        Replace(Tasks, _value.TaskStatistics.Select(x => $"{x.Name}: {x.FocusDuration.TotalMinutes:F0} min / {x.CompletedPomodoros}"));
        Replace(Tags, _value.TagStatistics.Select(x => $"{x.Name}: {x.FocusDuration.TotalMinutes:F0} min / {x.CompletedPomodoros}"));
        OnPropertyChanged(string.Empty);
    }
    private static void Fill(ObservableCollection<string> target, IEnumerable<TrendPoint> points) =>
        Replace(target, points.Select(x => $"{x.LocalDate:yyyy-MM-dd}: {x.FocusDuration.TotalMinutes:F0} min / {x.CompletedPomodoros}"));
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    { target.Clear(); foreach (var item in source) target.Add(item); }
}
