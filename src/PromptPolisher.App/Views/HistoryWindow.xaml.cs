using System.Windows;
using PromptPolisher.App.ViewModels;

namespace PromptPolisher.App.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow(HistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
