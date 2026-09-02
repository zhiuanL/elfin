using System.Collections.ObjectModel;
using System.Windows.Input;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Navigation;
using DesktopPet.Application.Windows;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.App.ViewModels;

public sealed class PomodoroViewModel : ObservableViewModel, IDisposable
{
    private readonly IPomodoroService _pomodoro;
    private readonly ITaskService _tasks;
    private readonly ITagService _tags;
    private readonly IStatisticsService _statistics;
    private readonly ISettingsService _settings;
    private readonly ITextLocalizer _text;
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _refreshLoop = Task.CompletedTask;
    private PomodoroSnapshot _snapshot = new(null, PomodoroPhase.Focus, 0, DateTimeOffset.UtcNow);
    private FocusTask? _selectedTask;
    private Tag? _selectedTag;
    private string _newTaskTitle = string.Empty, _newTagName = string.Empty, _notice = string.Empty;
    private TimeSpan _todayFocus;
    private bool _disposed;

    public PomodoroViewModel(IPomodoroService pomodoro, ITaskService tasks, ITagService tags,
        IStatisticsService statistics, ISettingsService settings, ITextLocalizer text, IUiDispatcher dispatcher)
    {
        _pomodoro = pomodoro; _tasks = tasks; _tags = tags; _statistics = statistics;
        _settings = settings; _text = text; _dispatcher = dispatcher;
        FocusMinutes = settings.Current.Productivity.Pomodoro.FocusMinutes;
        ShortBreakMinutes = settings.Current.Productivity.Pomodoro.ShortBreakMinutes;
        LongBreakMinutes = settings.Current.Productivity.Pomodoro.LongBreakMinutes;
        LongBreakInterval = settings.Current.Productivity.Pomodoro.LongBreakInterval;
        AutoStartNextPhase = settings.Current.Productivity.Pomodoro.AutoStartNextPhase;
        StartCommand = Async(StartAsync); PauseCommand = Async(() => _pomodoro.PauseAsync(_lifetime.Token));
        ResumeCommand = Async(() => _pomodoro.ResumeAsync(_lifetime.Token)); StopCommand = Async(() => _pomodoro.StopAsync(_lifetime.Token));
        SaveSettingsCommand = Async(SaveSettingsAsync); AddTaskCommand = Async(AddTaskAsync);
        ArchiveTaskCommand = Async(ArchiveTaskAsync); AddTagCommand = Async(AddTagAsync); AssignTagCommand = Async(AssignTagAsync);
        _pomodoro.Changed += OnChanged; _text.CultureChanged += OnChanged;
    }
    public string Title => _text.Get(TextKey.PomodoroTitle);
    public string Subtitle => _text.Get(TextKey.PomodoroSubtitle);
    public string PhaseLabel => _text.Get(TextKey.PomodoroPhaseLabel);
    public string RemainingLabel => _text.Get(TextKey.PomodoroRemaining);
    public string Phase => _snapshot.Status == PomodoroStatus.Idle ? _text.Get(TextKey.PomodoroIdle) : _snapshot.Session?.Phase.ToString() ?? string.Empty;
    public string Status => _snapshot.Status.ToString();
    public string Remaining => $"{Math.Max(0, (int)_snapshot.Remaining.TotalMinutes):00}:{Math.Max(0, _snapshot.Remaining.Seconds):00}";
    public string Consecutive => $"{_text.Get(TextKey.PomodoroConsecutive)}: {_snapshot.ConsecutiveFocusCount}";
    public string TodayFocus => $"{_text.Get(TextKey.PomodoroTodayFocus)}: {_todayFocus.TotalMinutes:F0} min";
    public string Notice { get => _notice; private set { _notice = value; OnPropertyChanged(); } }
    public ObservableCollection<FocusTask> Tasks { get; } = [];
    public ObservableCollection<Tag> Tags { get; } = [];
    public FocusTask? SelectedTask { get => _selectedTask; set { _selectedTask = value; OnPropertyChanged(); } }
    public Tag? SelectedTag { get => _selectedTag; set { _selectedTag = value; OnPropertyChanged(); } }
    public string NewTaskTitle { get => _newTaskTitle; set { _newTaskTitle = value; OnPropertyChanged(); } }
    public string NewTagName { get => _newTagName; set { _newTagName = value; OnPropertyChanged(); } }
    public int FocusMinutes { get; set; }
    public int ShortBreakMinutes { get; set; }
    public int LongBreakMinutes { get; set; }
    public int LongBreakInterval { get; set; }
    public bool AutoStartNextPhase { get; set; }
    public string StartText => _text.Get(TextKey.PomodoroStart);
    public string PauseText => _text.Get(TextKey.PomodoroPause);
    public string ResumeText => _text.Get(TextKey.PomodoroResume);
    public string StopText => _text.Get(TextKey.PomodoroStop);
    public string TaskText => _text.Get(TextKey.PomodoroTask);
    public string TagsText => _text.Get(TextKey.PomodoroTags);
    public string SettingsText => _text.Get(TextKey.PomodoroSettings);
    public string SaveText => _text.Get(TextKey.PomodoroSaveSettings);
    public string AddTaskText => _text.Get(TextKey.PomodoroAddTask);
    public string ArchiveTaskText => _text.Get(TextKey.PomodoroArchiveTask);
    public string AddTagText => _text.Get(TextKey.PomodoroAddTag);
    public string AssignTagText => _text.Get(TextKey.PomodoroAssignTag);
    public string FocusDurationText => _text.Get(TextKey.FocusDuration);
    public string ShortBreakDurationText => _text.Get(TextKey.ShortBreakDuration);
    public string LongBreakDurationText => _text.Get(TextKey.LongBreakDuration);
    public string LongBreakIntervalText => _text.Get(TextKey.LongBreakIntervalLabel);
    public string AutoStartText => _text.Get(TextKey.AutoStartNextPhase);
    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand AddTaskCommand { get; }
    public ICommand ArchiveTaskCommand { get; }
    public ICommand AddTagCommand { get; }
    public ICommand AssignTagCommand { get; }
    public async Task InitializeAsync()
    {
        await RefreshAsync();
        _refreshLoop = RefreshLoopAsync(_lifetime.Token);
    }
    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested) { await Task.Delay(TimeSpan.FromSeconds(1), ct); await RefreshAsync(); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private async Task RefreshAsync()
    {
        var snapshot = await _pomodoro.GetSnapshotAsync(_lifetime.Token);
        var tasks = await _tasks.ListAsync(false, _lifetime.Token);
        var tags = await _tags.ListAsync(_lifetime.Token);
        var stats = await _statistics.GetOverviewAsync(DateOnly.FromDateTime(DateTime.Now), TimeZoneInfo.Local, _lifetime.Token);
        await _dispatcher.InvokeAsync(() =>
        {
            _snapshot = snapshot; _todayFocus = stats.TodayFocusDuration;
            Replace(Tasks, tasks); Replace(Tags, tags);
            OnPropertyChanged(string.Empty);
            return Task.CompletedTask;
        }, _lifetime.Token);
    }
    private Task StartAsync()
    {
        var phase = _snapshot.SuggestedPhase;
        var minutes = phase switch { PomodoroPhase.Focus => FocusMinutes, PomodoroPhase.ShortBreak => ShortBreakMinutes, _ => LongBreakMinutes };
        return _pomodoro.StartAsync(phase, TimeSpan.FromMinutes(minutes), phase == PomodoroPhase.Focus ? SelectedTask?.Id : null, _lifetime.Token);
    }
    private async Task SaveSettingsAsync()
    {
        var value = new PomodoroSettings { FocusMinutes = FocusMinutes, ShortBreakMinutes = ShortBreakMinutes,
            LongBreakMinutes = LongBreakMinutes, LongBreakInterval = LongBreakInterval, AutoStartNextPhase = AutoStartNextPhase };
        if (!value.IsValid) throw new ArgumentException("Invalid Pomodoro settings.");
        await _settings.UpdateAsync(x => x with { Productivity = x.Productivity with { Pomodoro = value } }, _lifetime.Token);
        Notice = _text.Get(TextKey.SettingsSaved);
    }
    private async Task AddTaskAsync()
    {
        var task = await _tasks.CreateAsync(NewTaskTitle, null, _lifetime.Token);
        NewTaskTitle = string.Empty; SelectedTask = task; await RefreshAsync();
    }
    private async Task ArchiveTaskAsync()
    {
        if (SelectedTask is null) return;
        await _tasks.ArchiveAsync(SelectedTask.Id, _lifetime.Token); SelectedTask = null; await RefreshAsync();
    }
    private async Task AddTagAsync()
    {
        var tag = await _tags.CreateAsync(NewTagName, _lifetime.Token);
        NewTagName = string.Empty; SelectedTag = tag; await RefreshAsync();
    }
    private async Task AssignTagAsync()
    {
        if (SelectedTask is null || SelectedTag is null) return;
        var current = await _tasks.GetTagsAsync(SelectedTask.Id, _lifetime.Token);
        await _tasks.SetTagsAsync(SelectedTask.Id, current.Select(x => x.Id).Append(SelectedTag.Id).Distinct().ToArray(), _lifetime.Token);
        Notice = $"{SelectedTask.Title} · {SelectedTag.Name}";
    }
    private AsyncActionCommand Async(Func<Task> action) => new(action, exception => Notice = exception.GetType().Name);
    private async void OnChanged(object? sender, EventArgs e)
    {
        try { await RefreshAsync(); } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear(); foreach (var item in source) target.Add(item);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pomodoro.Changed -= OnChanged; _text.CultureChanged -= OnChanged;
        _lifetime.Cancel(); _lifetime.Dispose();
    }

    public async Task StopAsync()
    {
        if (_disposed) return;
        _pomodoro.Changed -= OnChanged;
        _text.CultureChanged -= OnChanged;
        _lifetime.Cancel();
        try { await _refreshLoop; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        _disposed = true;
        _lifetime.Dispose();
    }
}
