using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopPet.Application.Characters;
using DesktopPet.Infrastructure.Localization;
using DesktopPet.Windows.Characters;
using DesktopPet.Windows.Windowing;

namespace DesktopPet.Tests.Integration;

[CollectionDefinition("Native package dialogs", DisableParallelization = true)]
public sealed class NativePackageDialogCollection;

[Collection("Native package dialogs")]
public sealed class CharacterPickerTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task PreCancelledPickerNeverRequestsUiWork()
    {
        var dispatcher = new RejectDispatcher();
        var picker = new WindowsCharacterPackagePicker(dispatcher, new ResourceTextLocalizer("en-US"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => picker.PickAsync(CharacterPackageSourceKind.Zip, cancellation.Token));
        Assert.Equal(0, dispatcher.Calls);
    }
    [Fact]
    public async Task InvalidPickerKindIsRejectedBeforeRequestingUiWork()
    {
        var dispatcher = new RejectDispatcher();
        var picker = new WindowsCharacterPackagePicker(dispatcher, new ResourceTextLocalizer("en-US"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => picker.PickAsync((CharacterPackageSourceKind)99, default));
        Assert.Equal(0, dispatcher.Calls);
    }
    [Theory]
    [InlineData(CharacterPackageSourceKind.Zip)]
    [InlineData(CharacterPackageSourceKind.Directory)]
    public Task NativeDialogOpensAndLifetimeCancellationClosesOnlyItsOwnedWindow(CharacterPackageSourceKind kind) => OnSta(async () =>
    {
        var owner = new Window { Title = "elfin package picker integration", Width = 240, Height = 120, ShowInTaskbar = false };
        owner.Show();
        owner.Activate();
        var handle = new WindowInteropHelper(owner).Handle;
        var threadId = GetWindowThreadProcessId(handle, out _);
        var picker = new WindowsCharacterPackagePicker(new WpfUiDispatcher(Dispatcher.CurrentDispatcher), new ResourceTextLocalizer("en-US"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var observed = 0;
        // Read our test window's ownership only; no external app automation or user input injection.
        using var observer = new System.Threading.Timer(_ =>
        {
            var popup = FindOwnedDialog(threadId, handle);
            if (popup != 0 && popup != handle)
            {
                if (Interlocked.Exchange(ref observed, 1) == 0) output.WriteLine($"Native popup observed; cancelling: {popup}");
                cancellation.Cancel();
            }
        }, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => picker.PickAsync(kind, cancellation.Token));
            output.WriteLine("Picker cancellation completed.");
            Assert.Equal(1, Volatile.Read(ref observed));
            Assert.True(owner.IsVisible);
            Assert.Equal(0, FindOwnedDialog(threadId, handle));
        }
        finally { await observer.DisposeAsync(); owner.Close(); }
    });
    private static Task OnSta(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try { await test(); completion.TrySetResult(); }
                catch (Exception exception) { completion.TrySetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
    private sealed class RejectDispatcher : DesktopPet.Application.Windows.IUiDispatcher
    {
        public int Calls { get; private set; }
        public Task InvokeAsync(Func<Task> action, CancellationToken ct) { Calls++; throw new InvalidOperationException("UI must not be requested."); }
    }
    private static nint FindOwnedDialog(uint thread, nint owner)
    {
        nint result = 0;
        EnumThreadWindows(thread, (window, _) =>
        {
            var name = new System.Text.StringBuilder(64);
            GetClassName(window, name, name.Capacity);
            if (GetWindow(window, 4) == owner && name.ToString() == "#32770") result = window;
            return true;
        }, 0);
        return result;
    }
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, System.Text.StringBuilder name, int count);
}
