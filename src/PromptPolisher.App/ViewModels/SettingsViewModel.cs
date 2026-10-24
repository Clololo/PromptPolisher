using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptPolisher.Core.Models;
using PromptPolisher.Core.Services;

namespace PromptPolisher.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly string _defaultTemplate;

    public SettingsViewModel(AppSettings settings, string template, string defaultTemplate)
    {
        _defaultTemplate = string.IsNullOrWhiteSpace(defaultTemplate)
            ? PromptTemplateRenderer.DefaultTemplate
            : defaultTemplate;
        Settings = settings.Clone();
        Providers = new ObservableCollection<ProviderDescriptor>(Settings.Providers.Select(p => p.Clone()));

        SelectedProvider = Providers.FirstOrDefault();
        SelectedDefaultProvider = Providers.FirstOrDefault(p => p.Id == Settings.DefaultProviderId)
            ?? Providers.FirstOrDefault();

        Template = template;
        SaveHistory = Settings.SaveHistory;
        StorePromptContent = Settings.StorePromptContent;
        StreamByDefault = Settings.StreamByDefault;
        WebAutomationConsent = Settings.WebAutomationConsent;
        RequestTimeoutSeconds = Settings.RequestTimeoutSeconds;
        RetryCount = Settings.RetryCount;
    }

    public AppSettings Settings { get; }
    public ObservableCollection<ProviderDescriptor> Providers { get; }

    [ObservableProperty]
    private ProviderDescriptor? selectedProvider;

    [ObservableProperty]
    private ProviderDescriptor? selectedDefaultProvider;

    [ObservableProperty]
    private string template = string.Empty;

    [ObservableProperty]
    private string validationMessage = string.Empty;

    [ObservableProperty]
    private string apiKeyStatus = string.Empty;

    [ObservableProperty]
    private bool saveHistory;

    [ObservableProperty]
    private bool storePromptContent;

    [ObservableProperty]
    private bool streamByDefault;

    [ObservableProperty]
    private bool webAutomationConsent;

    [ObservableProperty]
    private int requestTimeoutSeconds;

    [ObservableProperty]
    private int retryCount;

    public void BuildSettings()
    {
        Settings.Providers = Providers.ToList();
        Settings.DefaultProviderId = SelectedDefaultProvider?.Id ?? Settings.DefaultProviderId;
        Settings.SaveHistory = SaveHistory;
        Settings.StorePromptContent = StorePromptContent;
        Settings.StreamByDefault = StreamByDefault;
        Settings.WebAutomationConsent = WebAutomationConsent;
        Settings.RequestTimeoutSeconds = Math.Clamp(RequestTimeoutSeconds, 5, 600);
        Settings.RetryCount = Math.Clamp(RetryCount, 0, 5);
        Settings.SystemPromptTemplate = string.Empty;
    }

    [RelayCommand]
    private void ResetTemplate()
    {
        Template = _defaultTemplate;
        ValidationMessage = "已恢复程序内置默认模板（点击“保存”后生效）。";
    }

    [RelayCommand]
    private void ValidateTemplate()
    {
        var result = new PromptTemplateRenderer().Validate(Template);
        ValidationMessage = result.Message;
    }
}
