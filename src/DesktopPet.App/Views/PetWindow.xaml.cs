using System.Windows;
using DesktopPet.App.ViewModels;

namespace DesktopPet.App.Views;

public partial class PetWindow : Window
{
    public PetWindow(PetWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
