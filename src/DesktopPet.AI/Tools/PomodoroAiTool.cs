using System.Text.Json;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.AI.Tools;

public enum PomodoroToolKind { Get, Start, Pause, Resume, Stop }

public sealed class PomodoroAiTool(PomodoroToolKind kind, IPomodoroService pomodoro, ISettingsService settings) : IAiTool
{
    public AiToolDefinition Definition { get; } = kind switch
    {
        PomodoroToolKind.Get => Low("pomodoro.get", "Get the current Pomodoro status and remaining time."),
        PomodoroToolKind.Start => Low("pomodoro.start", "Start a Pomodoro phase using an optional phase and duration.", AiToolSchemas.PomodoroStart),
        PomodoroToolKind.Pause => Low("pomodoro.pause", "Pause the running Pomodoro."),
        PomodoroToolKind.Resume => Low("pomodoro.resume", "Resume the paused Pomodoro."),
        PomodoroToolKind.Stop => Low("pomodoro.stop", "Stop the active Pomodoro."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public async Task<AiToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var snapshot = await pomodoro.GetSnapshotAsync(ct);
        switch (kind)
        {
            case PomodoroToolKind.Get: break;
            case PomodoroToolKind.Start:
                if (snapshot.Session?.IsActive == true) return new(ToolExecutionStatus.Conflict, "pomodoro_already_active");
                var phase = arguments.TryGetProperty("phase", out var phaseNode) ? ParsePhase(phaseNode.GetString()!) : snapshot.SuggestedPhase;
                var minutes = arguments.TryGetProperty("minutes", out var minutesNode) ? minutesNode.GetInt32() : DefaultMinutes(phase);
                await pomodoro.StartAsync(phase, TimeSpan.FromMinutes(minutes), null, ct); break;
            case PomodoroToolKind.Pause:
                if (snapshot.Status != PomodoroStatus.Running) return new(ToolExecutionStatus.Conflict, "pomodoro_not_running");
                await pomodoro.PauseAsync(ct); break;
            case PomodoroToolKind.Resume:
                if (snapshot.Status != PomodoroStatus.Paused) return new(ToolExecutionStatus.Conflict, "pomodoro_not_paused");
                await pomodoro.ResumeAsync(ct); break;
            case PomodoroToolKind.Stop:
                if (snapshot.Session?.IsActive != true) return new(ToolExecutionStatus.Conflict, "pomodoro_not_active");
                await pomodoro.StopAsync(ct); break;
        }
        snapshot = await pomodoro.GetSnapshotAsync(ct);
        return AiToolResult.Success("pomodoro_state", new { status = snapshot.Status.ToString(), phase = snapshot.Session?.Phase.ToString(),
            remainingSeconds = Math.Max(0, (long)snapshot.Remaining.TotalSeconds) });
    }
    private int DefaultMinutes(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => settings.Current.Productivity.Pomodoro.FocusMinutes,
        PomodoroPhase.ShortBreak => settings.Current.Productivity.Pomodoro.ShortBreakMinutes,
        _ => settings.Current.Productivity.Pomodoro.LongBreakMinutes
    };
    private static PomodoroPhase ParsePhase(string value) => value switch
    { "focus" => PomodoroPhase.Focus, "shortBreak" => PomodoroPhase.ShortBreak, "longBreak" => PomodoroPhase.LongBreak, _ => throw new ArgumentException("Invalid phase.") };
    private static AiToolDefinition Low(string id, string description, string schema = AiToolSchemas.Empty) =>
        new(id, description, schema, ToolRiskLevel.Low, ConfirmationPolicy.None, true);
}
