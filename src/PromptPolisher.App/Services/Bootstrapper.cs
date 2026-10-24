using System.IO;
using System.Net.Http;
using PromptPolisher.App.ViewModels;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Security;
using PromptPolisher.Core.Services;
using PromptPolisher.Core.Storage;

namespace PromptPolisher.App.Services;

public static class Bootstrapper
{
    public static (MainViewModel ViewModel, AppState State, FileLogger Logger, WebViewSessionHost WebHost) Build()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptPolisher");
        Directory.CreateDirectory(dataDirectory);

        var state = new AppState { DataDirectory = dataDirectory };
        var logger = new FileLogger(state.LogDirectory);
        var settingsService = new JsonSettingsService(logger);

        state.Settings = settingsService.Load(
            state.SettingsPath, state.TemplatePath, state.DefaultTemplatePath, out var template);
        state.Template = template;
        state.DefaultTemplate = File.Exists(state.DefaultTemplatePath)
            ? File.ReadAllText(state.DefaultTemplatePath)
            : PromptTemplateRenderer.DefaultTemplate;
        settingsService.Save(state.SettingsPath, state.Settings, state.TemplatePath, state.Template);

        var credentials = new WindowsCredentialStore(logger, Path.Combine(dataDirectory, "secrets"));
        var history = new SqliteHistoryRepository(state.DatabasePath);
        history.InitializeAsync().GetAwaiter().GetResult();

        var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var webHost = new WebViewSessionHost(state, logger);
        var catalog = new ProviderCatalog(state, httpClient, credentials, logger, webHost);
        var service = new PolisherService(catalog, new PromptTemplateRenderer(), history, logger);
        var dialogs = new DialogService(state, settingsService, credentials, history, logger, webHost, catalog);

        var viewModel = new MainViewModel(service, state, dialogs, history, credentials, logger, catalog);
        return (viewModel, state, logger, webHost);
    }
}
