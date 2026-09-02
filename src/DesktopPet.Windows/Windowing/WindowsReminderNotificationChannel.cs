using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsReminderNotificationChannel(INotificationService notifications) : IReminderNotificationChannel
{
    public ReminderChannels Channel => ReminderChannels.WindowsNotification;
    public Task NotifyAsync(Reminder reminder, DateTimeOffset occurrenceAtUtc, CancellationToken ct) =>
        notifications.ShowAsync(reminder.Title, reminder.Description ?? reminder.Title, ct);
}
