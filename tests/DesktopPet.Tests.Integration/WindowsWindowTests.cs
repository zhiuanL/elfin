using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.Application.Commands;
using DesktopPet.App.ViewModels;
using DesktopPet.App.Views;
using DesktopPet.Domain.Platform;
using DesktopPet.Infrastructure.Localization;
using DesktopPet.Windows.Windowing;

namespace DesktopPet.Tests.Integration;

public sealed class WindowsWindowTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public Task RealPetHwndHasTransparentBorderlessTopmostStyleAndPhysicalBounds() => OnSta(() =>
    {
        var window = new PetWindow(new PetWindowViewModel(new ResourceTextLocalizer("zh-CN")));
        using var adapter = new WindowsPetWindow(window);
        adapter.EnsureCreated();
        adapter.Show();
        Assert.Equal(WindowStyle.None, window.WindowStyle);
        Assert.True(window.AllowsTransparency);
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)window.Background).Color);
        Assert.False(window.ShowInTaskbar);
        var style = GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64();
        Assert.NotEqual(0, style & 0x00080000); // WS_EX_LAYERED
        Assert.NotEqual(0, style & 0x00000008); // WS_EX_TOPMOST
        var size = DpiMath.ToPixels(new(window.Width, window.Height), adapter.Dpi);
        output.WriteLine($"Test HWND DPI scale: {adapter.Dpi.X}; physical size: {adapter.Bounds.Width}x{adapter.Bounds.Height}; displays: {new WindowsDisplayService().GetDisplays().Count}");
        Assert.InRange(Math.Abs(adapter.Bounds.Width - size.Width), 0, 2);
        Assert.InRange(Math.Abs(adapter.Bounds.Height - size.Height), 0, 2);
        var primary = new WindowsDisplayService().GetDisplays().First(display => display.IsPrimary);
        var origin = new PixelPoint(primary.WorkingArea.X + 40, primary.WorkingArea.Y + 40);
        adapter.MoveTo(origin);
        Assert.Equal(origin, new PixelPoint(adapter.Bounds.X, adapter.Bounds.Y));
        adapter.Hide();
        Assert.False(adapter.IsVisible);
        adapter.Show();
        Assert.True(adapter.IsVisible);
        CommandId? request = null;
        adapter.CommandRequested += (_, args) => request = args.Command;
        window.Close();
        Assert.Equal(CommandId.HidePet, request);
        Assert.True(adapter.IsVisible); // Close was cancelled; application owns hide policy.
        adapter.Close();
        Assert.False(adapter.IsVisible);
    });

    [Fact]
    public Task ControlCenterCloseIsAnIntentAndTrayDisposalIsIdempotent() => OnSta(() =>
    {
        var text = new ResourceTextLocalizer("en-US");
        var window = new MainWindow(new MainWindowViewModel(text));
        using var control = new WindowsControlCenterWindow(window);
        using var tray = new WindowsTrayService(text);
        control.Show();
        CommandId? request = null;
        control.CommandRequested += (_, args) => request = args.Command;
        window.Close();
        Assert.Equal(CommandId.CloseControlCenter, request);
        Assert.True(control.IsVisible);
        control.Hide();
        Assert.False(control.IsVisible);
        control.Show();
        tray.Start();
        tray.Start();
        Assert.True(tray.IsVisible);
        tray.Dispose();
        tray.Dispose();
        Assert.False(tray.IsVisible);
        control.Close();
        Assert.False(control.IsVisible);
    });

    // Native adapter integration, not a mock of WPF or an external UI automation helper.
    private static Task OnSta(Action test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { test(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
}
