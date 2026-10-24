using System.Windows;
using PromptPolisher.App.ViewModels;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Security;
using PromptPolisher.Core.Services;

namespace PromptPolisher.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ICredentialStore _credentials;
    private readonly IRedactingLogger _logger;

    public SettingsWindow(
        SettingsViewModel viewModel,
        ICredentialStore credentials,
        IRedactingLogger logger)
    {
        InitializeComponent();
        DataContext = viewModel;
        _credentials = credentials;
        _logger = logger;

        Loaded += async (_, _) => await RefreshApiKeyStatusAsync();
        ProviderList.SelectionChanged += async (_, _) => await RefreshApiKeyStatusAsync();
    }

    private async Task RefreshApiKeyStatusAsync()
    {
        if (DataContext is not SettingsViewModel viewModel || viewModel.SelectedProvider is null)
        {
            return;
        }

        if (!viewModel.SelectedProvider.IsApi)
        {
            viewModel.ApiKeyStatus = "该提供方不需要 API Key。";
            return;
        }

        try
        {
            var key = await _credentials.GetSecretAsync(
                CredentialKeys.ApiKey(viewModel.SelectedProvider.Id));
            viewModel.ApiKeyStatus = string.IsNullOrWhiteSpace(key)
                ? "未配置"
                : "已保存（不回显）";
        }
        catch (Exception ex)
        {
            _logger.Warn("Failed to read API key status.", ex);
            viewModel.ApiKeyStatus = "读取失败";
        }
    }

    private async void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel || viewModel.SelectedProvider is null)
        {
            return;
        }

        if (!viewModel.SelectedProvider.IsApi)
        {
            MessageBox.Show("只有 API 提供方需要密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            viewModel.ApiKeyStatus = "请输入密钥后再保存。";
            return;
        }

        try
        {
            await _credentials.SetSecretAsync(
                CredentialKeys.ApiKey(viewModel.SelectedProvider.Id),
                ApiKeyBox.Password);
            ApiKeyBox.Clear();
            viewModel.ApiKeyStatus = "已保存（不回显）";
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save API key.", ex);
            MessageBox.Show("保存密钥失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ClearKey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel || viewModel.SelectedProvider is null)
        {
            return;
        }

        try
        {
            await _credentials.DeleteSecretAsync(
                CredentialKeys.ApiKey(viewModel.SelectedProvider.Id));
            viewModel.ApiKeyStatus = "已清除";
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to delete API key.", ex);
            MessageBox.Show("清除密钥失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var validation = new PromptTemplateRenderer().Validate(viewModel.Template);
        if (!validation.IsValid)
        {
            MessageBox.Show(validation.Message, "模板校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        viewModel.BuildSettings();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
