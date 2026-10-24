namespace PromptPolisher.App.Services;

public interface IDialogService
{
    bool ShowSettings();
    void ShowHistory();
    string? ShowManualResult(string renderedPrompt, string providerName);
    bool ConfirmCompliance();
    Task<bool> ShowLoginAsync(string providerId);
}
