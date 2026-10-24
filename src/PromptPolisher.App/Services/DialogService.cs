using System.Windows;
using PromptPolisher.App.ViewModels;
using PromptPolisher.App.Views;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Services;

namespace PromptPolisher.App.Services;

public sealed class DialogService : IDialogService
{
    private readonly AppState _state;
    private readonly JsonSettingsService _settingsService;
    private readonly ICredentialStore _credentials;
    private readonly IHistoryRepository _history;
    private readonly IRedactingLogger _logger;
    private readonly IWebSessionHost _webSessionHost;
    private readonly ProviderCatalog _catalog;

    public DialogService(
        AppState state,
        JsonSettingsService settingsService,
        ICredentialStore credentials,
        IHistoryRepository history,
        IRedactingLogger logger,
        IWebSessionHost webSessionHost,
        ProviderCatalog catalog)
    {
        _state = state;
        _settingsService = settingsService;
        _credentials = credentials;
        _history = history;
        _logger = logger;
        _webSessionHost = webSessionHost;
        _catalog = catalog;
    }

    public bool ShowSettings()
    {
        var viewModel = new SettingsViewModel(_state.Settings, _state.Template, _state.DefaultTemplate);
        var window = new SettingsWindow(viewModel, _credentials, _logger)
        {
            Owner = Application.Current.MainWindow
        };

        if (window.ShowDialog() != true)
        {
            return false;
        }

        _state.Settings = viewModel.Settings;
        _state.Template = viewModel.Template;
        _settingsService.Save(_state.SettingsPath, _state.Settings, _state.TemplatePath, _state.Template);
        _catalog.Rebuild();
        return true;
    }

    public void ShowHistory()
    {
        var viewModel = new HistoryViewModel(_history);
        var window = new HistoryWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public string? ShowManualResult(string renderedPrompt, string providerName)
    {
        var window = new ManualResultWindow(renderedPrompt, providerName)
        {
            Owner = Application.Current.MainWindow
        };

        return window.ShowDialog() == true ? window.ResultText : null;
    }

    public bool ConfirmCompliance()
    {
        var result = MessageBox.Show(
            "网页版自动化可能违反平台服务条款。请确认：\n\n" +
            "1. 该平台允许你使用自动化方式访问；\n" +
            "2. 你不会绕过验证码、风控或访问控制；\n" +
            "3. 你已阅读并遵守平台的服务条款；\n" +
            "4. 你接受账号可能被平台限制的风险。\n\n" +
            "是否继续？",
            "网页版合规确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    public Task<bool> ShowLoginAsync(string providerId) =>
        _webSessionHost.ShowLoginAsync(providerId, CancellationToken.None);
}
