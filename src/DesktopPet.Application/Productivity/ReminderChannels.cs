using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Productivity;

public sealed class PetBubbleReminderChannel(IPetBubbleService bubble) : IReminderNotificationChannel
{
    public ReminderChannels Channel => ReminderChannels.PetBubble;
    public Task NotifyAsync(Reminder reminder, DateTimeOffset occurrenceAtUtc, CancellationToken ct) =>
        bubble.ShowAsync(reminder.Title, ct);
}

public sealed class NoOpSoundReminderChannel : IReminderNotificationChannel
{
    public ReminderChannels Channel => ReminderChannels.Sound;
    public Task NotifyAsync(Reminder reminder, DateTimeOffset occurrenceAtUtc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
