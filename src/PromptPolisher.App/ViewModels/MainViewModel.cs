using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PromptPolisher.App.Services;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;
using PromptPolisher.Core.Services;

namespace PromptPolisher.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PolisherService _service;
    private readonly AppState _state;
    private readonly IDialogService _dialogs;
    private readonly IHistoryRepository _history;
    private readonly ICredentialStore _credentials;
    private readonly IRedactingLogger _logger;
    private readonly ProviderCatalog _catalog;

    private CancellationTokenSource? _cts;

    public MainViewModel(
        PolisherService service,
        AppState state,
        IDialogService dialogs,
        IHistoryRepository history,
        ICredentialStore credentials,
        IRedactingLogger logger,
        ProviderCatalog catalog)
    {
        _service = service;
        _state = state;
        _dialogs = dialogs;
        _history = history;
        _credentials = credentials;
        _logger = logger;
        _catalog = catalog;

        StrengthOptions = new[]
        {
            new EnumOption<PolishStrength>(PolishStrength.Light, "轻度"),
            new EnumOption<PolishStrength>(PolishStrength.Standard, "标准"),
            new EnumOption<PolishStrength>(PolishStrength.Deep, "深度")
        };
        StyleOptions = new[]
        {
            new EnumOption<PolishStyle>(PolishStyle.General, "通用"),
            new EnumOption<PolishStyle>(PolishStyle.Rigorous, "严谨"),
            new EnumOption<PolishStyle>(PolishStyle.Concise, "简洁"),
            new EnumOption<PolishStyle>(PolishStyle.Creative, "创意"),
            new EnumOption<PolishStyle>(PolishStyle.Technical, "技术/工程"),
            new EnumOption<PolishStyle>(PolishStyle.Marketing, "营销"),
            new EnumOption<PolishStyle>(PolishStyle.Academic, "学术")
        };
        LanguageOptions = new[]
        {
            new EnumOption<OutputLanguage>(OutputLanguage.KeepOriginal, "保持原文"),
            new EnumOption<OutputLanguage>(OutputLanguage.Chinese, "中文"),
            new EnumOption<OutputLanguage>(OutputLanguage.English, "英文"),
            new EnumOption<OutputLanguage>(OutputLanguage.Other, "其他")
        };
        LengthOptions = new[]
        {
            new EnumOption<LengthControl>(LengthControl.Shorter, "缩短"),
            new EnumOption<LengthControl>(LengthControl.Keep, "保持"),
            new EnumOption<LengthControl>(LengthControl.Expand, "适度扩写")
        };
        FormatOptions = new[]
        {
            new EnumOption<OutputFormat>(OutputFormat.ResultOnly, "仅润色结果"),
            new EnumOption<OutputFormat>(OutputFormat.ResultWithNotes, "结果 + 修改说明"),
            new EnumOption<OutputFormat>(OutputFormat.Markdown, "结构化 Markdown")
        };

        SelectedStrength = StrengthOptions[1];
        SelectedStyle = StyleOptions[0];
        SelectedLanguage = LanguageOptions[0];
        SelectedLength = LengthOptions[1];
        SelectedFormat = FormatOptions[0];
    }

    public ObservableCollection<ProviderDescriptor> Providers { get; } = new();

    public IReadOnlyList<EnumOption<PolishStrength>> StrengthOptions { get; }
    public IReadOnlyList<EnumOption<PolishStyle>> StyleOptions { get; }
    public IReadOnlyList<EnumOption<OutputLanguage>> LanguageOptions { get; }
    public IReadOnlyList<EnumOption<LengthControl>> LengthOptions { get; }
    public IReadOnlyList<EnumOption<OutputFormat>> FormatOptions { get; }

    [ObservableProperty]
    private ProviderDescriptor? selectedProvider;

    [ObservableProperty]
    private string inputText = string.Empty;

    [ObservableProperty]
    private string outputText = string.Empty;

    [ObservableProperty]
    private string statusText = "就绪。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPolish))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    private bool isBusy;

    [ObservableProperty]
    private bool streamOutput = true;

    [ObservableProperty]
    private int timeoutSeconds = 60;

    [ObservableProperty]
    private int retryCount = 2;

    [ObservableProperty]
    private bool webAutomationConsent;

    [ObservableProperty]
    private bool preserveMeaning = true;

    [ObservableProperty]
    private string preserveTerms = string.Empty;

    [ObservableProperty]
    private string extraInstructions = string.Empty;

    [ObservableProperty]
    private EnumOption<PolishStrength>? selectedStrength;

    [ObservableProperty]
    private EnumOption<PolishStyle>? selectedStyle;

    [ObservableProperty]
    private EnumOption<OutputLanguage>? selectedLanguage;

    [ObservableProperty]
    private EnumOption<LengthControl>? selectedLength;

    [ObservableProperty]
    private EnumOption<OutputFormat>? selectedFormat;

    public int InputCharCount => InputText.Length;
    public int OutputCharCount => OutputText.Length;
    public bool CanPolish => !IsBusy;
    public bool CanCancel => IsBusy;

    public Task InitializeAsync()
    {
        RefreshProviders();

        StreamOutput = _state.Settings.StreamByDefault;
        TimeoutSeconds = _state.Settings.RequestTimeoutSeconds;
        RetryCount = _state.Settings.RetryCount;
        WebAutomationConsent = _state.Settings.WebAutomationConsent;

        StatusText = "就绪。默认使用免费网页版：首次点击“润色”会引导登录；遇到验证码请手动完成。官方 API 仅作可选备用。";
        _logger.Info("Main window initialized (web-first defaults).");
        return Task.CompletedTask;
    }

    partial void OnInputTextChanged(string value) => OnPropertyChanged(nameof(InputCharCount));

    partial void OnOutputTextChanged(string value) => OnPropertyChanged(nameof(OutputCharCount));

    private void RefreshProviders()
    {
        var previousId = SelectedProvider?.Id;
        Providers.Clear();

        foreach (var descriptor in _catalog.Descriptors)
        {
            Providers.Add(descriptor);
        }

        SelectedProvider = Providers.FirstOrDefault(p => p.Id == previousId)
            ?? Providers.FirstOrDefault(p => p.Id == _state.Settings.DefaultProviderId)
            ?? Providers.FirstOrDefault();
    }

    [RelayCommand]
    private async Task PolishAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(InputText))
        {
            StatusText = "请输入原始 Prompt。";
            return;
        }

        if (SelectedProvider is null)
        {
            StatusText = "请选择模型提供方。";
            return;
        }

        if (SelectedProvider.Kind == ProviderKind.Web && !WebAutomationConsent)
        {
            _logger.Info($"Compliance confirmation requested for provider {SelectedProvider.Id} (kind={SelectedProvider.Kind}).");
            if (!_dialogs.ConfirmCompliance())
            {
                StatusText = "已取消：未确认网页版合规声明。";
                return;
            }

            WebAutomationConsent = true;
        }

        _logger.Info($"Polish requested: provider={SelectedProvider.Id}, kind={SelectedProvider.Kind}, consent={WebAutomationConsent}.");

        var request = BuildRequest();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        OutputText = string.Empty;
        StatusText = $"正在调用 {SelectedProvider.DisplayName}…";

        try
        {
            var progress = new Progress<string>(chunk =>
            {
                if (StreamOutput)
                {
                    OutputText += chunk;
                }
            });

            var result = await _service.PolishAsync(
                request,
                _state.Template,
                _state.Settings,
                progress,
                _cts.Token);

            if (result.RequiresManualAction)
            {
                var manual = _dialogs.ShowManualResult(
                    result.Content ?? string.Empty,
                    SelectedProvider.DisplayName);

                if (manual is not null)
                {
                    OutputText = manual;
                    await _service.SaveManualResultAsync(
                        request,
                        SelectedProvider,
                        manual,
                        _state.Settings);
                    StatusText = "完成（手动模式）。";
                }
                else
                {
                    StatusText = "已取消手动模式。";
                }

                return;
            }

            if (result.Success)
            {
                OutputText = result.Content ?? string.Empty;
                StatusText = $"完成，用时 {result.Duration.TotalSeconds:F1}s。";
                return;
            }

            StatusText = $"[{result.ErrorCode}] {result.ErrorMessage}";

            if (result.RequiresLogin && SelectedProvider.Kind == ProviderKind.Web)
            {
                StatusText = "需要登录：请在内嵌窗口完成登录（扫码/手机号），完成后点“完成登录”。";
                try
                {
                    var loggedIn = await _dialogs.ShowLoginAsync(SelectedProvider.Id);
                    if (loggedIn)
                    {
                        StatusText = "登录完成，请再次点击“润色”。";
                    }
                    else
                    {
                        StatusText = "已取消登录。";
                    }
                }
                catch (Exception loginEx)
                {
                    _logger.Error("Failed to open the web login window.", loginEx);
                    StatusText = "打开登录窗口失败：" + loginEx.Message;
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消。";
        }
        catch (Exception ex)
        {
            _logger.Error("Polish failed.", ex);
            StatusText = "发生错误：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private PolisherRequest BuildRequest() => new()
    {
        UserPrompt = InputText,
        ProviderId = SelectedProvider!.Id,
        Strength = SelectedStrength?.Value ?? PolishStrength.Standard,
        Style = SelectedStyle?.Value ?? PolishStyle.General,
        Language = SelectedLanguage?.Value ?? OutputLanguage.KeepOriginal,
        Length = SelectedLength?.Value ?? LengthControl.Keep,
        Format = SelectedFormat?.Value ?? OutputFormat.ResultOnly,
        PreserveTerms = PreserveTerms,
        PreserveMeaning = PreserveMeaning,
        ExtraInstructions = ExtraInstructions,
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 600),
        RetryCount = Math.Clamp(RetryCount, 0, 5),
        Stream = StreamOutput,
        WebAutomationConsent = WebAutomationConsent
    };

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>窗口关闭时调用（命令生成为私有，窗口需要直接调用）。</summary>
    public void CancelCurrentOperation() => _cts?.Cancel();

    [RelayCommand]
    private void Copy()
    {
        if (string.IsNullOrEmpty(OutputText))
        {
            StatusText = "没有可复制的结果。";
            return;
        }

        try
        {
            Clipboard.SetText(OutputText);
            StatusText = "已复制到剪贴板。";
        }
        catch (Exception ex)
        {
            _logger.Warn("Clipboard copy failed.", ex);
            StatusText = "复制失败：剪贴板被占用，请重试。";
        }
    }

    [RelayCommand]
    private void Export()
    {
        if (string.IsNullOrEmpty(OutputText))
        {
            StatusText = "没有可导出的结果。";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "文本文件|*.txt|所有文件|*.*",
            FileName = $"polished-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, OutputText);
            StatusText = "已导出：" + dialog.FileName;
        }
        catch (Exception ex)
        {
            _logger.Warn("Export failed.", ex);
            StatusText = "导出失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        InputText = string.Empty;
        OutputText = string.Empty;
        StatusText = "已清空。";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (_dialogs.ShowSettings())
        {
            RefreshProviders();
            StatusText = "设置已保存。";
        }
    }

    [RelayCommand]
    private void OpenHistory()
    {
        _dialogs.ShowHistory();
    }

    [RelayCommand]
    private Task RepolishAsync() => PolishAsync();
}
