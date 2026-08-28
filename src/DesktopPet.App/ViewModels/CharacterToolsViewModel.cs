using System.Collections.ObjectModel;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.App.ViewModels;

public sealed record CharacterListItem(CharacterId Id, string DisplayName);
public sealed class CharacterToolsViewModel : ObservableViewModel, IDisposable
{
    private readonly ICharacterPackageService _characters;
    private readonly CharacterPresentationService _presentation;
    private readonly ITextLocalizer _text;
    private readonly IExceptionHandler _exceptions;
    private readonly ICharacterPackagePicker _picker;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncActionCommand[] _commands;
    private string _diagnostics = string.Empty;
    private string _sourcePath = string.Empty;
    public CharacterToolsViewModel(ICharacterPackageService characters, CharacterPresentationService presentation, ITextLocalizer text,
        IExceptionHandler exceptions, ICharacterPackagePicker picker)
    {
        _characters = characters; _presentation = presentation; _text = text; _exceptions = exceptions;
        _picker = picker;
        BrowseZipCommand = Command(() => BrowseAsync(CharacterPackageSourceKind.Zip));
        BrowseFolderCommand = Command(() => BrowseAsync(CharacterPackageSourceKind.Directory));
        RefreshCommand = Command(RefreshCoreAsync);
        ValidateCommand = Command(async () => Show(await _characters.ValidateAsync(SourcePath, _lifetime.Token)));
        ImportCommand = Command(async () => { Show((await _characters.ImportAsync(SourcePath, _lifetime.Token)).Validation); await RefreshCoreAsync(); });
        ActivateCommand = Command(async () => { if (Selected is { } item) Show((await _presentation.ActivateAsync(item.Id, _lifetime.Token)).Validation); });
        RemoveCommand = Command(async () => { if (Selected is { } item) { Show(await _characters.RemoveAsync(item.Id, _lifetime.Token)); await RefreshCoreAsync(); } });
        PlayCommand = Command(async () =>
        {
            if (!CharacterSchema.IsSemantic(Semantic)) { Diagnostics = _text.Get(TextKey.CharacterInvalidSemantic); return; }
            await _presentation.PlayAsync(new(Semantic), _lifetime.Token);
        });
        _commands = [BrowseZipCommand, BrowseFolderCommand, RefreshCommand, ValidateCommand, ImportCommand, ActivateCommand, RemoveCommand, PlayCommand];
    }
    public string Heading => _text.Get(TextKey.CharacterTools);
    public string SourceHint => _text.Get(TextKey.CharacterSource);
    public string RefreshText => _text.Get(TextKey.CharacterRefresh);
    public string ValidateText => _text.Get(TextKey.CharacterValidate);
    public string ImportText => _text.Get(TextKey.CharacterImport);
    public string BrowseZipText => _text.Get(TextKey.CharacterBrowseZip);
    public string BrowseFolderText => _text.Get(TextKey.CharacterBrowseFolder);
    public string ActivateText => _text.Get(TextKey.CharacterActivate);
    public string RemoveText => _text.Get(TextKey.CharacterRemove);
    public string PlayText => _text.Get(TextKey.CharacterPlay);
    public string SourcePath
    {
        get => _sourcePath;
        set { if (_sourcePath == value) return; _sourcePath = value; OnPropertyChanged(); }
    }
    public string Semantic { get; set; } = "idle";
    public ObservableCollection<CharacterListItem> Characters { get; } = [];
    public CharacterListItem? Selected { get; set; }
    public string Diagnostics { get => _diagnostics; private set { _diagnostics = value; OnPropertyChanged(); } }
    public AsyncActionCommand RefreshCommand { get; }
    public AsyncActionCommand ValidateCommand { get; }
    public AsyncActionCommand ImportCommand { get; }
    public AsyncActionCommand BrowseZipCommand { get; }
    public AsyncActionCommand BrowseFolderCommand { get; }
    public AsyncActionCommand ActivateCommand { get; }
    public AsyncActionCommand RemoveCommand { get; }
    public AsyncActionCommand PlayCommand { get; }
    private AsyncActionCommand Command(Func<Task> action) => new(async () =>
    {
        await _gate.WaitAsync(_lifetime.Token);
        try { await action(); }
        finally { _gate.Release(); }
    }, exception =>
    {
        _exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Command);
        Diagnostics = _text.Get(TextKey.CommandFailed);
    });
    public Task InitializeAsync() => RefreshCoreAsync();
    private async Task BrowseAsync(CharacterPackageSourceKind kind)
    {
        var selected = await _picker.PickAsync(kind, _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(selected)) SourcePath = selected;
    }
    private async Task RefreshCoreAsync()
    {
        var result = await _characters.DiscoverAsync(_lifetime.Token);
        Characters.Clear();
        foreach (var package in result.Packages)
        {
            var definition = package.Definition;
            Characters.Add(new(definition.Id, $"{definition.Localize(_text.Culture.Name).Name} · {definition.Metadata.ActualLevel} · {definition.Metadata.CompletenessPercentage}%"));
        }
        Selected = Characters.FirstOrDefault(item => item.Id == _presentation.Current?.Definition.Id) ?? Characters.FirstOrDefault();
        OnPropertyChanged(nameof(Selected));
        if (result.Issues.Any(issue => issue.Severity == ValidationSeverity.Fatal)) Diagnostics = FormatIssues(result.Issues);
    }
    private void Show(ValidationResult result)
    {
        Diagnostics = _text.Get(result.CanInstall ? TextKey.CharacterAccepted : TextKey.CharacterRejected) +
            (result.Definition is { } definition ?
                $" · {_text.Get(TextKey.CharacterTargetTier)}: {definition.Metadata.TargetTier} → {_text.Get(TextKey.CharacterActualTier)}: {definition.Metadata.ActualLevel} · {definition.Metadata.CompletenessPercentage}%\n" +
                $"{_text.Get(TextKey.CharacterMissingCapabilities)}: {string.Join(", ", definition.Metadata.MissingCapabilities)}\n" : "\n") +
            FormatIssues(result.Issues);
    }
    private static string FormatIssues(IReadOnlyList<ValidationIssue> issues) => string.Join("\n", issues.Select(i =>
        $"{i.Severity} / {i.ErrorCode} / {i.JsonPath ?? i.ResourcePath}\nExpected: {i.Expected}; Actual: {i.Actual}\n{i.Message} {i.Suggestion}"));
    public async Task StopAsync()
    {
        _lifetime.Cancel();
        await Task.WhenAll(_commands.Select(command => command.Completion));
    }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); _gate.Dispose(); }
}
