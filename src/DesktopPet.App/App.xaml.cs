using System.Windows;
using DesktopPet.App.Bootstrap;

namespace DesktopPet.App;

public partial class App : System.Windows.Application
{
    private ApplicationHostController? _controller;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _controller = new ApplicationHostController(this);
        await _controller.StartAsync(e.Args);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }
}
