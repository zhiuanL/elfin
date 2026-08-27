using System.Windows;
using DesktopPet.App.ViewModels;

namespace DesktopPet.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += (_, args) =>
        {
            // Phase 0 exits. Tray and configurable close policy belong to Phase 1.
            if (viewModel.IsShuttingDown) return;
            args.Cancel = true;
            viewModel.CloseCommand.Execute(null);
        };
    }
}
