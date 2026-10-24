using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;

namespace PromptPolisher.App.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryRepository _history;

    public HistoryViewModel(IHistoryRepository history)
    {
        _history = history;
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private HistoryEntry? selectedEntry;

    [ObservableProperty]
    private string statusText = string.Empty;

    public async Task InitializeAsync() => await LoadAsync();

    [RelayCommand]
    private async Task SearchAsync() => await LoadAsync();

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedEntry is null)
        {
            StatusText = "请先选择一条记录。";
            return;
        }

        await _history.DeleteAsync(SelectedEntry.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        var result = MessageBox.Show(
            "确定清空全部历史记录？此操作不可撤销。",
            "清空历史",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _history.ClearAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var items = await _history.SearchAsync(SearchText, 200);
            Entries.Clear();
            foreach (var item in items)
            {
                Entries.Add(item);
            }

            StatusText = $"共 {Entries.Count} 条记录。";
        }
        catch (Exception ex)
        {
            StatusText = "加载历史失败：" + ex.Message;
        }
    }
}
