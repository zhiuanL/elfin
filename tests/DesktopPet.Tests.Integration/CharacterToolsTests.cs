using DesktopPet.App.ViewModels;
using DesktopPet.Application.Characters;
using DesktopPet.CharacterSdk;
using DesktopPet.Infrastructure.Characters;
using DesktopPet.Infrastructure.Localization;

namespace DesktopPet.Tests.Integration;

public sealed class CharacterToolsTests
{
    [Fact]
    public async Task DeveloperCommandsUseApplicationServicesAndDisplayStructuredDiagnostics()
    {
        using var context = new CharacterTestContext();
        using var presentation = new CharacterPresentationService(context.Manager,
            new DirectoryCharacterSeedSource(CharacterTestContext.FixtureRoot), context.Settings, new NoImageSurface(), context.Exceptions, TimeProvider.System);
        using var viewModel = new CharacterToolsViewModel(context.Manager, presentation, new ResourceTextLocalizer("en-US"), context.Exceptions, new TestPicker());
        viewModel.SourcePath = context.CopyFixture();
        await Execute(viewModel.ValidateCommand);
        Assert.Contains("Target tier: Basic", viewModel.Diagnostics);
        Assert.Contains("Actual tier: Basic", viewModel.Diagnostics);
        Assert.Contains("Missing capabilities: Blink", viewModel.Diagnostics);
        Assert.Empty(await context.Manager.ListAsync(default));
        await Execute(viewModel.ImportCommand);
        Assert.Single(viewModel.Characters);
        await Execute(viewModel.ActivateCommand);
        Assert.Equal("dev.elfin.basic", presentation.Current!.Definition.Id.Value);
        await Execute(viewModel.RemoveCommand);
        Assert.Contains(nameof(CharacterErrorCode.ActiveCharacter), viewModel.Diagnostics);
        viewModel.Semantic = "../invalid";
        await Execute(viewModel.PlayCommand);
        Assert.Contains("semantic", viewModel.Diagnostics, StringComparison.OrdinalIgnoreCase);
        viewModel.SourcePath = Path.Combine(context.Environment.Directories.Root, "absent.zip");
        await Execute(viewModel.ImportCommand);
        Assert.Contains("Fatal", viewModel.Diagnostics);
        Assert.NotNull(presentation.Current);
        await viewModel.StopAsync();
        await presentation.StopAsync(default);
    }
    [Fact]
    public async Task AsyncCommandBlocksDoubleClickAndRecoversAfterCompletionOrFailure()
    {
        var completion = new TaskCompletionSource();
        var runs = 0;
        var failures = 0;
        var command = new AsyncActionCommand(async () => { runs++; await completion.Task; }, _ => failures++);
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal(1, runs);
        completion.SetException(new IOException("expected test failure"));
        await command.Completion;
        Assert.Equal(1, failures);
        Assert.True(command.CanExecute(null));
        var immediate = new AsyncActionCommand(() => Task.CompletedTask, _ => failures++);
        immediate.Execute(null);
        await immediate.Completion;
        Assert.True(immediate.CanExecute(null));
    }
    private static async Task Execute(AsyncActionCommand command) { command.Execute(null); await command.Completion; }
    [Theory]
    [InlineData(CharacterPackageSourceKind.Zip)]
    [InlineData(CharacterPackageSourceKind.Directory)]
    public async Task BrowseUpdatesBoundPathAndImportStillUsesValidatedApplicationFlow(CharacterPackageSourceKind kind)
    {
        using var context = new CharacterTestContext();
        using var presentation = new CharacterPresentationService(context.Manager,
            new DirectoryCharacterSeedSource(CharacterTestContext.FixtureRoot), context.Settings, new NoImageSurface(), context.Exceptions, TimeProvider.System);
        var source = context.CopyFixture();
        if (kind == CharacterPackageSourceKind.Zip)
        {
            var zip = Path.Combine(context.Environment.Directories.Root, "角色 测试.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(source, zip);
            source = zip;
        }
        var picker = new TestPicker { Result = source };
        using var viewModel = new CharacterToolsViewModel(context.Manager, presentation, new ResourceTextLocalizer("en-US"), context.Exceptions, picker);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        await Execute(kind == CharacterPackageSourceKind.Zip ? viewModel.BrowseZipCommand : viewModel.BrowseFolderCommand);
        Assert.Equal(kind, picker.Kind);
        Assert.Equal(source, viewModel.SourcePath);
        Assert.Contains(nameof(viewModel.SourcePath), changed);
        Assert.Empty(await context.Manager.ListAsync(default)); // Picking is not permission to install.
        await Execute(viewModel.ImportCommand);
        Assert.Single(await context.Manager.ListAsync(default));
        Assert.Single(viewModel.Characters);
        await viewModel.StopAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOrFailedSelectionPreservesExistingPathAndDoesNotInstall(bool fail)
    {
        using var context = new CharacterTestContext();
        using var presentation = new CharacterPresentationService(context.Manager,
            new DirectoryCharacterSeedSource(CharacterTestContext.FixtureRoot), context.Settings, new NoImageSurface(), context.Exceptions, TimeProvider.System);
        var picker = new TestPicker { Failure = fail ? new IOException("Expected picker failure") : null };
        using var viewModel = new CharacterToolsViewModel(context.Manager, presentation, new ResourceTextLocalizer("en-US"), context.Exceptions, picker);
        viewModel.SourcePath = "previous selection";
        await Execute(viewModel.BrowseZipCommand);
        await Execute(viewModel.BrowseFolderCommand);
        Assert.Equal("previous selection", viewModel.SourcePath);
        Assert.Empty(await context.Manager.ListAsync(default));
        if (fail) Assert.NotEmpty(viewModel.Diagnostics);
        else Assert.Empty(viewModel.Diagnostics);
        Assert.True(viewModel.BrowseZipCommand.CanExecute(null));
        await viewModel.StopAsync();
    }
    [Fact]
    public async Task ShutdownCancelsOutstandingPickerAndWaitsWithoutInstalling()
    {
        using var context = new CharacterTestContext();
        using var presentation = new CharacterPresentationService(context.Manager,
            new DirectoryCharacterSeedSource(CharacterTestContext.FixtureRoot), context.Settings, new NoImageSurface(), context.Exceptions, TimeProvider.System);
        var started = new TaskCompletionSource();
        var picker = new TestPicker { WaitUntilCancelled = true, Started = started };
        using var viewModel = new CharacterToolsViewModel(context.Manager, presentation, new ResourceTextLocalizer("en-US"), context.Exceptions, picker);
        viewModel.SourcePath = "previous selection";
        viewModel.BrowseZipCommand.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewModel.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(viewModel.BrowseZipCommand.Completion.IsCompletedSuccessfully);
        Assert.Equal("previous selection", viewModel.SourcePath);
        Assert.Empty(await context.Manager.ListAsync(default));
    }
    private sealed class TestPicker : ICharacterPackagePicker
    {
        public CharacterPackageSourceKind? Kind { get; private set; }
        public string? Result { get; init; }
        public Exception? Failure { get; init; }
        public bool WaitUntilCancelled { get; init; }
        public TaskCompletionSource? Started { get; init; }
        public async Task<string?> PickAsync(CharacterPackageSourceKind kind, CancellationToken ct)
        {
            Kind = kind;
            ct.ThrowIfCancellationRequested();
            Started?.SetResult();
            if (WaitUntilCancelled) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            if (Failure is not null) throw Failure;
            return Result;
        }
    }
    private sealed class NoImageSurface : IAnimationSurface
    {
        public Task SetPackageAsync(CharacterPackage package, CancellationToken ct) => Task.CompletedTask;
        public Task PreloadAsync(string path, CancellationToken ct) => Task.CompletedTask;
        public Task PresentAsync(string path, CancellationToken ct) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
