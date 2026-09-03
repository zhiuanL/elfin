using System.Windows;
using System.Windows.Controls;
using DesktopPet.App.ViewModels;
using System.ComponentModel;

namespace DesktopPet.App.Views.Pages;

public partial class AiPage : UserControl
{
    private AiViewModel? _viewModel;
    public AiPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => Attach(DataContext as AiViewModel);
        Unloaded += (_, _) => Attach(null);
    }
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    { if (DataContext is AiViewModel viewModel && sender is PasswordBox password) viewModel.ApiKey = password.Password; }
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => Attach(e.NewValue as AiViewModel);
    private void Attach(AiViewModel? viewModel)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = viewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiViewModel.ApiKey) && string.IsNullOrEmpty(_viewModel?.ApiKey) && ApiKeyBox.Password.Length > 0)
            ApiKeyBox.Password = string.Empty;
    }
}
