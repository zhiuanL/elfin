using System.Collections.ObjectModel;
using System.Windows.Media;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Runtime;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;
using DesktopPet.Windows.Characters;
using DesktopPet.AI.Contracts;

namespace DesktopPet.App.ViewModels;

public sealed class CharacterManagerItemViewModel(CharacterPackage package, string culture, ImageSource? preview, bool active)
{
    public CharacterPackage Package { get; } = package;
    public CharacterId Id => Package.Definition.Id;
    public string Name => Package.Definition.Localize(culture).Name;
    public string Description => Package.Definition.Localize(culture).Description;
    public string PackageVersion => Package.Definition.Manifest.PackageVersion;
    public string Level => $"{Package.Definition.Metadata.ActualLevel} · {Package.Definition.Metadata.CompletenessPercentage}%";
    public string Capabilities => string.Join(", ", Package.Definition.Animations.Keys.Order());
    public string MissingCapabilities => string.Join(", ", Package.Definition.Metadata.MissingCapabilities);
    public ImageSource? Preview { get; } = preview;
    public bool IsActive { get; } = active;
}

public sealed class CharacterManagerViewModel : ObservableViewModel, IDisposable
{
    private readonly ICharacterPackageService _characters;
    private readonly ICharacterPresentation _presentation;
    private readonly ICharacterPackagePicker _picker;
    private readonly ICharacterPreviewLoader _previews;
    private readonly IUserConfirmationService _confirmation;
    private readonly ITextLocalizer _text;
    private readonly IExceptionHandler _exceptions;
    private readonly IAiChatService _ai;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CharacterManagerItemViewModel? _selected;
    private string _sourcePath = string.Empty;
    private string _validation = string.Empty;
    private string _notice = string.Empty;

    public CharacterManagerViewModel(ICharacterPackageService characters, ICharacterPresentation presentation,
        ICharacterPackagePicker picker, ICharacterPreviewLoader previews, IUserConfirmationService confirmation,
        ITextLocalizer text, IExceptionHandler exceptions, IAiChatService ai)
    {
        _characters = characters; _presentation = presentation; _picker = picker; _previews = previews;
        _confirmation = confirmation; _text = text; _exceptions = exceptions; _ai = ai;
        BrowseCommand = Command(BrowseAsync);
        ValidateCommand = Command(ValidateAsync);
        ImportCommand = Command(ImportAndActivateAsync);
        ActivateCommand = Command(ActivateAsync, () => Selected is not null && !Selected.IsActive);
        RemoveCommand = Command(RemoveAsync, () => Selected is not null && !Selected.IsActive);
        RefreshCommand = Command(RefreshAsync);
        _text.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<CharacterManagerItemViewModel> Characters { get; } = [];
    public CharacterManagerItemViewModel? Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelection));
            ActivateCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged(); }
    }
    public bool HasSelection => Selected is not null;
    public string SourcePath { get => _sourcePath; set { if (_sourcePath == value) return; _sourcePath = value; OnPropertyChanged(); } }
    public string Validation { get => _validation; private set { _validation = value; OnPropertyChanged(); } }
    public string Notice { get => _notice; private set { _notice = value; OnPropertyChanged(); } }
    public string Title => _text.Get(TextKey.CharactersTitle);
    public string Subtitle => _text.Get(TextKey.CharactersSubtitle);
    public string ActiveBadge => _text.Get(TextKey.CharacterActiveBadge);
    public string BrowseText => _text.Get(TextKey.CharacterBrowseZip);
    public string ValidateText => _text.Get(TextKey.CharacterValidate);
    public string ImportText => _text.Get(TextKey.CharacterImportAndActivate);
    public string ActivateText => _text.Get(TextKey.CharacterActivate);
    public string RemoveText => _text.Get(TextKey.CharacterRemove);
    public string RefreshText => _text.Get(TextKey.CharacterRefresh);
    public string PreviewText => _text.Get(TextKey.CharacterPreview);
    public string VersionText => _text.Get(TextKey.CharacterPackageVersion);
    public string CapabilitiesText => _text.Get(TextKey.CharacterCapabilities);
    public string ValidationText => _text.Get(TextKey.CharacterValidationResults);
    public AsyncActionCommand BrowseCommand { get; }
    public AsyncActionCommand ValidateCommand { get; }
    public AsyncActionCommand ImportCommand { get; }
    public AsyncActionCommand ActivateCommand { get; }
    public AsyncActionCommand RemoveCommand { get; }
    public AsyncActionCommand RefreshCommand { get; }

    public Task InitializeAsync() => RefreshAsync();
    private AsyncActionCommand Command(Func<Task> action, Func<bool>? canExecute = null) => new(async () =>
    {
        await _gate.WaitAsync(_lifetime.Token);
        try { Notice = string.Empty; await action(); }
        finally { _gate.Release(); }
    }, exception =>
    {
        _exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Command);
        Notice = _text.Get(TextKey.CommandFailed);
    }, canExecute);

    private async Task BrowseAsync()
    {
        var selected = await _picker.PickAsync(CharacterPackageSourceKind.Zip, _lifetime.Token);
        if (!string.IsNullOrWhiteSpace(selected)) SourcePath = selected;
    }
    private async Task ValidateAsync() => ShowValidation(await _characters.ValidateAsync(SourcePath, _lifetime.Token));
    private async Task ImportAndActivateAsync()
    {
        var validation = await _characters.ValidateAsync(SourcePath, _lifetime.Token);
        ShowValidation(validation);
        if (!validation.CanInstall || validation.Definition is null) return;
        var imported = await _characters.ImportAsync(SourcePath, _lifetime.Token);
        ShowValidation(imported.Validation);
        if (!imported.Succeeded || imported.Package is null) return;
        await _ai.StopAsync(_lifetime.Token);
        var activated = await _presentation.ActivateAsync(imported.Package.Definition.Id, _lifetime.Token);
        ShowValidation(activated.Validation);
        if (activated.Succeeded) Notice = _text.Get(TextKey.CharacterImportSucceeded);
        await RefreshAsync();
    }
    private async Task ActivateAsync()
    {
        if (Selected is null) return;
        await _ai.StopAsync(_lifetime.Token);
        ShowValidation((await _presentation.ActivateAsync(Selected.Id, _lifetime.Token)).Validation);
        await RefreshAsync();
    }
    private async Task RemoveAsync()
    {
        if (Selected is null) return;
        if (Selected.IsActive) { Notice = _text.Get(TextKey.CharacterRemoveProtected); return; }
        if (!await _confirmation.ConfirmAsync(new(ConfirmationAction.RemoveCharacter, Selected.Name), _lifetime.Token)) return;
        ShowValidation(await _characters.RemoveAsync(Selected.Id, _lifetime.Token));
        await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        var keep = Selected?.Id;
        var discovery = await _characters.DiscoverAsync(_lifetime.Token);
        var items = new List<CharacterManagerItemViewModel>();
        foreach (var package in discovery.Packages)
            items.Add(new(package, _text.Culture.Name, await _previews.LoadAsync(package, _lifetime.Token),
                package.Definition.Id == _presentation.Current?.Definition.Id));
        Characters.Clear();
        foreach (var item in items.OrderByDescending(item => item.IsActive).ThenBy(item => item.Name)) Characters.Add(item);
        Selected = Characters.FirstOrDefault(item => item.Id == keep) ?? Characters.FirstOrDefault();
        if (discovery.Issues.Count > 0) Validation = FormatIssues(discovery.Issues);
    }
    private void ShowValidation(ValidationResult result)
    {
        Validation = _text.Get(result.CanInstall ? TextKey.CharacterAccepted : TextKey.CharacterRejected) +
            (result.Definition is null ? "" : $" · {result.ActualLevel} · {result.CompletenessPercentage}%") + "\n" + FormatIssues(result.Issues);
    }
    private static string FormatIssues(IEnumerable<ValidationIssue> issues) => string.Join("\n", issues.Select(issue =>
        $"{issue.Severity} / {issue.ErrorCode} / {issue.JsonPath ?? issue.ResourcePath}\n{issue.Message} {issue.Suggestion}"));
    private async void OnCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);
        try { await RefreshAsync(); }
        catch (Exception exception) { _exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Command); }
    }
    public async Task StopAsync() { _lifetime.Cancel(); await Task.WhenAll(BrowseCommand.Completion, ValidateCommand.Completion,
        ImportCommand.Completion, ActivateCommand.Completion, RemoveCommand.Completion, RefreshCommand.Completion); }
    public void Dispose() { _text.CultureChanged -= OnCultureChanged; _lifetime.Cancel(); _lifetime.Dispose(); _gate.Dispose(); }
}
