using System.Reflection;
using DesktopPet.Application.Appearance;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Windows;

namespace DesktopPet.App.ViewModels;

public sealed record SettingOption<T>(T Value, string Label);

public sealed class SettingsViewModel : ObservableViewModel, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ITextLocalizer _text;
    private readonly IAppearanceService _appearance;
    private readonly IExceptionHandler _exceptions;
    private readonly CancellationTokenSource _lifetime = new();
    private string _culture = "zh-CN";
    private ControlCenterCloseBehavior _closeBehavior;
    private bool _petVisible;
    private bool _topmost;
    private ThemeMode _theme;
    private string _notice = string.Empty;

    public SettingsViewModel(ISettingsService settings, ITextLocalizer text,
        IAppearanceService appearance, IExceptionHandler exceptions, MovementToolsViewModel movement)
    {
        _settings = settings; _text = text; _appearance = appearance; _exceptions = exceptions;
        Movement = movement;
        ApplyGeneralCommand = Command(ApplyGeneralAsync);
        ApplyPetCommand = Command(ApplyPetAsync);
        ApplyAppearanceCommand = Command(ApplyAppearanceAsync);
        _text.CultureChanged += OnCultureChanged;
        RefreshOptions();
    }

    public MovementToolsViewModel Movement { get; }
    public IReadOnlyList<SettingOption<string>> Cultures { get; private set; } = [];
    public IReadOnlyList<SettingOption<ControlCenterCloseBehavior>> CloseBehaviors { get; private set; } = [];
    public IReadOnlyList<SettingOption<ThemeMode>> Themes { get; private set; } = [];
    public string Culture { get => _culture; set { _culture = value; OnPropertyChanged(); } }
    public ControlCenterCloseBehavior CloseBehavior { get => _closeBehavior; set { _closeBehavior = value; OnPropertyChanged(); } }
    public bool PetVisible { get => _petVisible; set { _petVisible = value; OnPropertyChanged(); } }
    public bool Topmost { get => _topmost; set { _topmost = value; OnPropertyChanged(); } }
    public ThemeMode Theme { get => _theme; set { _theme = value; OnPropertyChanged(); } }
    public string Notice { get => _notice; private set { _notice = value; OnPropertyChanged(); } }
    public string Title => _text.Get(TextKey.SettingsTitle);
    public string Subtitle => _text.Get(TextKey.SettingsSubtitle);
    public string GeneralTitle => _text.Get(TextKey.GeneralSettings);
    public string PetTitle => _text.Get(TextKey.PetSettings);
    public string MovementTitle => _text.Get(TextKey.MovementSettingsLabel);
    public string InteractionTitle => _text.Get(TextKey.InteractionSettings);
    public string AppearanceTitle => _text.Get(TextKey.AppearanceSettings);
    public string AdvancedTitle => _text.Get(TextKey.AdvancedSettings);
    public string LanguageText => _text.Get(TextKey.Language);
    public string CloseBehaviorText => _text.Get(TextKey.CloseBehavior);
    public string PetVisibleText => _text.Get(TextKey.PetVisible);
    public string TopmostText => _text.Get(TextKey.AlwaysOnTop);
    public string ThemeText => _text.Get(TextKey.AppearanceSettings);
    public string ApplyGeneralText => _text.Get(TextKey.ApplyGeneral);
    public string ApplyPetText => _text.Get(TextKey.ApplyGeneral);
    public string ApplyAppearanceText => _text.Get(TextKey.ApplyAppearance);
    public string AppVersionText => _text.Get(TextKey.AppVersion);
    public string SettingsSchemaText => _text.Get(TextKey.SettingsSchema);
    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.7.0";
    public int SettingsSchema => AppSettings.CurrentSchemaVersion;
    public AsyncActionCommand ApplyGeneralCommand { get; }
    public AsyncActionCommand ApplyPetCommand { get; }
    public AsyncActionCommand ApplyAppearanceCommand { get; }
    public event EventHandler<WindowCommandEventArgs>? CommandRequested;

    public void Initialize()
    {
        var current = _settings.Current;
        Culture = current.Culture; CloseBehavior = current.ControlCenterCloseBehavior;
        PetVisible = current.PetWindow.IsVisible; Topmost = current.PetWindow.Topmost; Theme = current.Appearance.Theme;
        RefreshOptions();
    }
    private async Task ApplyGeneralAsync()
    {
        await _text.SetCultureAsync(Culture, _lifetime.Token);
        await _settings.UpdateAsync(current => current with { ControlCenterCloseBehavior = CloseBehavior }, _lifetime.Token);
        Notice = _text.Get(TextKey.SettingsSaved);
    }
    private Task ApplyPetAsync()
    {
        CommandRequested?.Invoke(this, new(PetVisible ? CommandId.ShowPet : CommandId.HidePet));
        CommandRequested?.Invoke(this, new(Topmost ? CommandId.EnableTopmost : CommandId.DisableTopmost));
        Notice = _text.Get(TextKey.SettingsSaved);
        return Task.CompletedTask;
    }
    private async Task ApplyAppearanceAsync()
    {
        await _appearance.ApplyAsync(Theme, _lifetime.Token);
        Notice = _text.Get(TextKey.SettingsSaved);
    }
    private AsyncActionCommand Command(Func<Task> action) => new(action, exception =>
    {
        _exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Command);
        Notice = _text.Get(TextKey.SettingsFailed);
    });
    private void RefreshOptions()
    {
        Cultures = [new("zh-CN", "简体中文"), new("en-US", "English")];
        CloseBehaviors = [new(ControlCenterCloseBehavior.HideToTray, _text.Get(TextKey.CloseHideToTray)),
            new(ControlCenterCloseBehavior.Exit, _text.Get(TextKey.CloseExit))];
        Themes = [new(ThemeMode.System, _text.Get(TextKey.ThemeSystem)), new(ThemeMode.Light, _text.Get(TextKey.ThemeLight)),
            new(ThemeMode.Dark, _text.Get(TextKey.ThemeDark))];
        OnPropertyChanged(nameof(Cultures)); OnPropertyChanged(nameof(CloseBehaviors)); OnPropertyChanged(nameof(Themes));
    }
    private void OnCultureChanged(object? sender, EventArgs e) { RefreshOptions(); OnPropertyChanged(string.Empty); }
    public async Task StopAsync() { _lifetime.Cancel(); await Task.WhenAll(ApplyGeneralCommand.Completion, ApplyPetCommand.Completion,
        ApplyAppearanceCommand.Completion); }
    public void Dispose() { _text.CultureChanged -= OnCultureChanged; _lifetime.Cancel(); _lifetime.Dispose(); }
}
