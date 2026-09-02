using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.App.ViewModels;

public sealed class RemindersViewModel : ObservableViewModel, IDisposable
{
    private readonly IReminderService _service;
    private readonly IUserConfirmationService _confirmation;
    private readonly ITextLocalizer _text;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _lifetime = new();
    private Reminder? _selected;
    private string _title = string.Empty, _description = string.Empty, _notice = string.Empty;
    private bool _disposed;
    public RemindersViewModel(IReminderService service, IUserConfirmationService confirmation, ITextLocalizer text,
        TimeProvider clock)
    {
        _service = service; _confirmation = confirmation; _text = text; _clock = clock;
        AddCommand = Async(CreateAsync); UpdateCommand = Async(UpdateAsync); DeleteCommand = Async(DeleteAsync);
        ToggleCommand = Async(ToggleAsync); _service.Changed += OnChanged; _text.CultureChanged += OnChanged;
    }
    public string PageTitle => _text.Get(TextKey.RemindersTitle);
    public string Subtitle => _text.Get(TextKey.RemindersSubtitle);
    public string AddText => _text.Get(TextKey.ReminderAdd);
    public string UpdateText => _text.Get(TextKey.ReminderUpdate);
    public string DeleteText => _text.Get(TextKey.ReminderDelete);
    public string ToggleText => Selected?.Enabled == true ? _text.Get(TextKey.ReminderDisable) : _text.Get(TextKey.ReminderEnable);
    public string ScheduleText => _text.Get(TextKey.ReminderScheduleType);
    public string DueTextLabel => _text.Get(TextKey.ReminderDue);
    public string RecurrenceText => _text.Get(TextKey.ReminderRecurrence);
    public string TimeZoneText => _text.Get(TextKey.ReminderTimeZone);
    public string ChannelsText => _text.Get(TextKey.ReminderChannelsLabel);
    public string TitleLabel => _text.Get(TextKey.ReminderTitleLabel);
    public string DescriptionLabel => _text.Get(TextKey.ReminderDescriptionLabel);
    public string RelativeMinutesLabel => _text.Get(TextKey.ReminderRelativeMinutes);
    public string AbsoluteLocalLabel => _text.Get(TextKey.ReminderAbsoluteLocal);
    public string RecurringTimeLabel => _text.Get(TextKey.ReminderRecurringTime);
    public string IntervalDaysLabel => _text.Get(TextKey.ReminderIntervalDays);
    public string WeekdaysLabel => _text.Get(TextKey.ReminderWeekdays);
    public string PetBubbleLabel => _text.Get(TextKey.ChannelPetBubble);
    public string PetActionLabel => _text.Get(TextKey.ChannelPetAction);
    public string WindowsLabel => _text.Get(TextKey.ChannelWindows);
    public string SoundLabel => _text.Get(TextKey.ChannelSound);
    public string Notice { get => _notice; private set { _notice = value; OnPropertyChanged(); } }
    public ObservableCollection<Reminder> Items { get; } = [];
    public IReadOnlyList<ReminderScheduleType> ScheduleTypes { get; } = Enum.GetValues<ReminderScheduleType>();
    public IReadOnlyList<RecurrenceKind> RecurrenceKinds { get; } = Enum.GetValues<RecurrenceKind>();
    public Reminder? Selected { get => _selected; set { _selected = value; Load(value); OnPropertyChanged(string.Empty); } }
    public string ReminderTitle { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    public ReminderScheduleType ScheduleType { get; set; } = ReminderScheduleType.RelativeOneTime;
    public int RelativeMinutes { get; set; } = 10;
    public string AbsoluteLocal { get; set; } = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public RecurrenceKind RecurrenceKind { get; set; } = RecurrenceKind.Daily;
    public string RecurringTime { get; set; } = "09:00";
    public int IntervalDays { get; set; } = 1;
    public string Weekdays { get; set; } = "Monday,Tuesday,Wednesday,Thursday,Friday";
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public bool PetBubble { get; set; } = true;
    public bool PetAction { get; set; } = true;
    public bool WindowsNotification { get; set; } = true;
    public bool Sound { get; set; }
    public ICommand AddCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ToggleCommand { get; }
    public async Task InitializeAsync() => await RefreshAsync();
    private async Task CreateAsync()
    {
        var now = _clock.GetUtcNow();
        await _service.CreateAsync(new(Guid.Empty, ReminderTitle, Description, BuildSchedule(), TimeZoneId, true,
            MissedReminderPolicy.Smart, BuildChannels(), now, now, null), _lifetime.Token);
        Clear(); await RefreshAsync();
    }
    private async Task UpdateAsync()
    {
        if (Selected is null) return;
        await _service.UpdateAsync(Selected with { Title = ReminderTitle, Description = Description,
            Schedule = BuildSchedule(), TimeZoneId = TimeZoneId, Channels = BuildChannels() }, _lifetime.Token);
        await RefreshAsync();
    }
    private async Task DeleteAsync()
    {
        if (Selected is null || !await _confirmation.ConfirmAsync(new(ConfirmationAction.DeleteReminder, Selected.Title), _lifetime.Token)) return;
        await _service.DeleteAsync(Selected.Id, _lifetime.Token); Clear(); await RefreshAsync();
    }
    private async Task ToggleAsync()
    {
        if (Selected is null) return;
        await _service.SetEnabledAsync(Selected.Id, !Selected.Enabled, _lifetime.Token); await RefreshAsync();
    }
    private ReminderSchedule BuildSchedule() => ScheduleType switch
    {
        ReminderScheduleType.RelativeOneTime => new RelativeOneTimeSchedule(_clock.GetUtcNow().AddMinutes(Math.Clamp(RelativeMinutes, 1, 525600))),
        ReminderScheduleType.AbsoluteOneTime => new AbsoluteOneTimeSchedule(DateTime.ParseExact(AbsoluteLocal, "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.None)),
        _ => new RecurringSchedule(1, new(RecurrenceKind, TimeOnly.Parse(RecurringTime, CultureInfo.InvariantCulture),
            ParseWeekdays(), Math.Clamp(IntervalDays, 1, 365)))
    };
    private HashSet<DayOfWeek> ParseWeekdays() => Weekdays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => Enum.Parse<DayOfWeek>(value, true)).ToHashSet();
    private ReminderChannels BuildChannels() => (PetBubble ? ReminderChannels.PetBubble : 0) |
        (PetAction ? ReminderChannels.PetAction : 0) | (WindowsNotification ? ReminderChannels.WindowsNotification : 0) |
        (Sound ? ReminderChannels.Sound : 0);
    private void Load(Reminder? reminder)
    {
        if (reminder is null) return;
        ReminderTitle = reminder.Title; Description = reminder.Description ?? string.Empty; ScheduleType = reminder.Schedule.Type;
        TimeZoneId = reminder.TimeZoneId; PetBubble = reminder.Channels.HasFlag(ReminderChannels.PetBubble);
        PetAction = reminder.Channels.HasFlag(ReminderChannels.PetAction);
        WindowsNotification = reminder.Channels.HasFlag(ReminderChannels.WindowsNotification);
        Sound = reminder.Channels.HasFlag(ReminderChannels.Sound);
        switch (reminder.Schedule)
        {
            case RelativeOneTimeSchedule relative: RelativeMinutes = Math.Max(1, (int)(relative.DueAtUtc - _clock.GetUtcNow()).TotalMinutes); break;
            case AbsoluteOneTimeSchedule absolute: AbsoluteLocal = absolute.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); break;
            case RecurringSchedule recurring:
                RecurrenceKind = recurring.Rule.Kind; RecurringTime = recurring.Rule.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                IntervalDays = recurring.Rule.IntervalDays; Weekdays = string.Join(',', recurring.Rule.Weekdays); break;
        }
    }
    private async Task RefreshAsync()
    {
        var items = await _service.ListAsync(_lifetime.Token);
        Items.Clear(); foreach (var item in items) Items.Add(item);
        OnPropertyChanged(string.Empty);
    }
    private void Clear() { Selected = null; ReminderTitle = string.Empty; Description = string.Empty; }
    private AsyncActionCommand Async(Func<Task> action) => new(action, exception => Notice = exception.GetType().Name);
    private async void OnChanged(object? sender, EventArgs e)
    {
        try { await RefreshAsync(); } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _service.Changed -= OnChanged; _text.CultureChanged -= OnChanged; _lifetime.Cancel(); _lifetime.Dispose();
    }
}
