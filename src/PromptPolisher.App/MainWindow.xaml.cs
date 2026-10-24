using System.Windows;
using PromptPolisher.App.ViewModels;

namespace PromptPolisher.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closing += (_, _) => _viewModel.CancelCurrentOperation();
    }
}
