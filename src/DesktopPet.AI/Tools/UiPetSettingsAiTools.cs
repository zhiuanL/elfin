using System.Text.Json;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Appearance;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Domain.Movement;
using DesktopPet.Domain.Pets;

namespace DesktopPet.AI.Tools;

public enum UiToolKind { OpenPage, OpenSettings, ShowControlCenter }
public sealed class UiAiTool(UiToolKind kind, ICommandRegistry commands) : IAiTool
{
    public AiToolDefinition Definition { get; } = kind switch
    {
        UiToolKind.OpenPage => Low("ui.openPage", "Open an allowed control center page.", AiToolSchemas.UiPage),
        UiToolKind.OpenSettings => Low("ui.openSettings", "Open the Settings page."),
        UiToolKind.ShowControlCenter => Low("ui.showControlCenter", "Show the control center window."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public async Task<AiToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var command = kind switch
        {
            UiToolKind.OpenSettings => CommandId.OpenSettings,
            UiToolKind.ShowControlCenter => CommandId.OpenControlCenter,
            _ => PageCommand(arguments.GetProperty("page").GetString()!)
        };
        return (await commands.ExecuteAsync(command, ct)).Status == CommandStatus.Completed
            ? AiToolResult.Success("ui_opened", new { target = command.ToString() })
            : new(ToolExecutionStatus.Failed, "command_unavailable");
    }
    private static CommandId PageCommand(string page) => page switch
    {
        "home" => CommandId.OpenHome, "ai" => CommandId.OpenAi, "pomodoro" => CommandId.OpenPomodoro,
        "reminders" => CommandId.OpenReminders, "statistics" => CommandId.OpenStatistics,
        "characters" => CommandId.OpenCharacters, "settings" => CommandId.OpenSettings,
        _ => throw new ArgumentException("Invalid page.")
    };
    private static AiToolDefinition Low(string id, string description, string schema = AiToolSchemas.Empty) =>
        new(id, description, schema, ToolRiskLevel.Low, ConfirmationPolicy.None, true);
}

public enum PetToolKind { Show, Hide, SetMovementMode, SetClickThrough }
public sealed class PetAiTool(PetToolKind kind, ICommandRegistry commands, ISettingsService settings) : IAiTool
{
    public AiToolDefinition Definition { get; } = kind switch
    {
        PetToolKind.Show => Low("pet.show", "Show the desktop pet."),
        PetToolKind.Hide => Low("pet.hide", "Hide the desktop pet."),
        PetToolKind.SetMovementMode => Medium("pet.setMovementMode", "Change the pet movement mode.", AiToolSchemas.MovementMode),
        PetToolKind.SetClickThrough => Medium("pet.setClickThrough", "Enable or disable mouse click-through for the pet.", AiToolSchemas.ClickThrough),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public async Task<AiToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (kind is PetToolKind.Show or PetToolKind.Hide)
        {
            var command = kind == PetToolKind.Show ? CommandId.ShowPet : CommandId.HidePet;
            return (await commands.ExecuteAsync(command, ct)).Status == CommandStatus.Completed
                ? AiToolResult.Success(kind == PetToolKind.Show ? "pet_shown" : "pet_hidden")
                : new(ToolExecutionStatus.Failed, "command_unavailable");
        }
        if (kind == PetToolKind.SetClickThrough)
        {
            var enabled = arguments.GetProperty("enabled").GetBoolean();
            var command = enabled ? CommandId.SetClickThrough : CommandId.SetInteractive;
            return (await commands.ExecuteAsync(command, ct)).Status == CommandStatus.Completed
                ? AiToolResult.Success("click_through_changed", new { enabled })
                : new(ToolExecutionStatus.Failed, "command_unavailable");
        }
        var modeText = arguments.GetProperty("mode").GetString()!;
        var mode = modeText switch
        { "fixed" => MovementMode.Fixed, "local" => MovementMode.Local, "desktop" => MovementMode.Desktop, "hybrid" => MovementMode.Hybrid, _ => throw new ArgumentException("Invalid movement mode.") };
        await settings.UpdateAsync(value => value with { MovementMode = mode }, ct);
        return AiToolResult.Success("movement_mode_changed", new { mode = modeText });
    }
    private static AiToolDefinition Low(string id, string description) => new(id, description, AiToolSchemas.Empty, ToolRiskLevel.Low, ConfirmationPolicy.None, true);
    private static AiToolDefinition Medium(string id, string description, string schema) => new(id, description, schema, ToolRiskLevel.Medium, ConfirmationPolicy.ConfirmOrUndo, true);
}

public static class SettingsToolWhitelist
{
    public static IReadOnlySet<string> Fields { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "motionStyle", "theme", "alwaysOnTop" };
}
public sealed class SettingsAiTool(ISettingsService settings, IAppearanceService appearance, ICommandRegistry commands) : IAiTool
{
    public AiToolDefinition Definition { get; } = new("settings.set", "Change one explicitly allowed appearance or motion setting.",
        AiToolSchemas.Settings, ToolRiskLevel.Medium, ConfirmationPolicy.ConfirmOrUndo, true);
    public async Task<AiToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var setting = arguments.GetProperty("setting").GetString()!;
        var value = arguments.GetProperty("value").GetString()!;
        if (!SettingsToolWhitelist.Fields.Contains(setting)) return new(ToolExecutionStatus.ValidationError, "setting_not_allowed");
        switch (setting)
        {
            case "motionStyle":
                var style = value.ToLowerInvariant() switch
                { "quiet" => MotionStyle.Quiet, "natural" => MotionStyle.Natural, "lively" => MotionStyle.Lively, _ => throw new ArgumentException("Invalid motion style.") };
                await settings.UpdateAsync(current => current with { MotionStyle = style,
                    Movement = current.Movement with { UserMotionStyle = style } }, ct); break;
            case "theme":
                var theme = value.ToLowerInvariant() switch
                { "system" => ThemeMode.System, "light" => ThemeMode.Light, "dark" => ThemeMode.Dark, _ => throw new ArgumentException("Invalid theme.") };
                await settings.UpdateAsync(current => current with { Appearance = current.Appearance with { Theme = theme } }, ct);
                await appearance.ApplyAsync(theme, ct); break;
            case "alwaysOnTop":
                if (!bool.TryParse(value, out var topmost)) throw new ArgumentException("Invalid topmost value.");
                var command = topmost ? CommandId.EnableTopmost : CommandId.DisableTopmost;
                if ((await commands.ExecuteAsync(command, ct)).Status != CommandStatus.Completed)
                    return new(ToolExecutionStatus.Failed, "command_unavailable");
                break;
        }
        return AiToolResult.Success("setting_changed", new { setting, value });
    }
}
