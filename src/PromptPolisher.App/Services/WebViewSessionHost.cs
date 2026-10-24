using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using PromptPolisher.App.Views;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;

namespace PromptPolisher.App.Services;

/// <summary>
/// WebView2 会话宿主。首次登录显示内嵌窗口；后续调用复用同一会话并在屏幕外运行。
/// 只做“像用户一样操作页面”，不绕过验证码/风控。
/// </summary>
public sealed class WebViewSessionHost : IWebSessionHost, IDisposable
{
    private readonly AppState _state;
    private readonly IRedactingLogger _logger;
    private WebViewHostWindow? _window;

    public WebViewSessionHost(AppState state, IRedactingLogger logger)
    {
        _state = state;
        _logger = logger;
    }

    public async Task<bool> ShowLoginAsync(string providerId, CancellationToken cancellationToken)
    {
        var provider = GetProvider(providerId);
        if (provider is null)
        {
            return false;
        }

        await EnsureHostAsync();
        var url = provider.LoginUrl ?? provider.ChatUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        _logger.Info($"Opening login window for {providerId}: {url}");
        var task = _window!.ShowForLoginAsync($"登录：{provider.DisplayName}", url);
        var completed = await task.WaitAsync(cancellationToken);
        if (completed)
        {
            _logger.Info($"Web login completed for provider {providerId}.");
        }

        return completed;
    }

    public async Task<WebSessionStatus> GetStatusAsync(string providerId, CancellationToken cancellationToken)
    {
        var provider = GetProvider(providerId);
        if (provider is null)
        {
            return WebSessionStatus.Unknown;
        }

        try
        {
            await EnsureHostAsync();
            if (!string.IsNullOrWhiteSpace(provider.ChatUrl))
            {
                _logger.Info($"Web status: navigating to {provider.ChatUrl}");
                await NavigateAsync(provider.ChatUrl!, cancellationToken);
                _logger.Info("Web status: navigation completed.");
            }

            if (await DetectCaptchaAsync(provider, cancellationToken))
            {
                _logger.Warn($"Web status: captcha detected for {providerId}.");
                // 让 UI 打开内嵌窗口，由用户手动完成验证；应用不绕过。
                return WebSessionStatus.NotLoggedIn;
            }

            var needsLogin = await DetectLoginAsync(provider, cancellationToken);
            _logger.Info($"Web status: {providerId} needsLogin={needsLogin}.");
            return needsLogin ? WebSessionStatus.NotLoggedIn : WebSessionStatus.Ready;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Web status check failed for provider {providerId}; will ask user to log in.", ex);
            // 打不开页面/初始化失败时，让 UI 弹出登录窗口，用户能看到实际页面错误。
            return WebSessionStatus.NotLoggedIn;
        }
    }

    public async Task<PolisherResult> SendAsync(
        string providerId,
        string renderedPrompt,
        int timeoutSeconds,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = GetProvider(providerId);
        if (provider is null)
        {
            return PolisherResult.Fail(ErrorCodes.InvalidRequest, providerId);
        }

        try
        {
            _logger.Info($"Web send: provider={providerId}, timeout={timeoutSeconds}s.");
            await EnsureHostAsync();
            if (!string.IsNullOrWhiteSpace(provider.ChatUrl))
            {
                _logger.Info($"Web send: navigating to {provider.ChatUrl}");
                await NavigateAsync(provider.ChatUrl!, cancellationToken);
                _logger.Info("Web send: navigation completed.");
            }

            if (await DetectCaptchaAsync(provider, cancellationToken))
            {
                _logger.Warn("Web send: captcha detected; asking user to complete it manually.");
                return PolisherResult.Fail(ErrorCodes.CaptchaRequired, providerId, duration: stopwatch.Elapsed);
            }

            if (await DetectLoginAsync(provider, cancellationToken))
            {
                return PolisherResult.Fail(
                    ErrorCodes.AuthRequired,
                    providerId,
                    requiresLogin: true,
                    duration: stopwatch.Elapsed);
            }

            var before = await GetResponseStateAsync(provider, cancellationToken);

            var fillResult = await ExecuteScriptAsync(BuildFillScript(provider, renderedPrompt), cancellationToken);
            if (!IsOk(fillResult))
            {
                _logger.Warn($"Web send: input selector not found. Result={fillResult}");
                return PolisherResult.Fail(
                    ErrorCodes.WebSelectorNotFound,
                    providerId,
                    rawError: fillResult,
                    duration: stopwatch.Elapsed);
            }

            _logger.Info("Web send: input filled; clicking send.");

            await Task.Delay(350, cancellationToken);

            string clickResult = string.Empty;
            var clicked = false;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                clickResult = await ExecuteScriptAsync(BuildClickScript(provider), cancellationToken);
                if (IsOk(clickResult))
                {
                    clicked = true;
                    break;
                }

                await Task.Delay(300, cancellationToken);
            }

            if (!clicked)
            {
                return PolisherResult.Fail(
                    ErrorCodes.WebSelectorNotFound,
                    providerId,
                    rawError: clickResult,
                    duration: stopwatch.Elapsed);
            }

            var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 10, 600));
            var lastText = before.Text;
            var stableSince = DateTime.UtcNow;
            var sawNewContent = false;

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(800, cancellationToken);

                if (await DetectCaptchaAsync(provider, cancellationToken))
                {
                    return PolisherResult.Fail(
                        ErrorCodes.CaptchaRequired,
                        providerId,
                        duration: stopwatch.Elapsed);
                }

                var state = await GetResponseStateAsync(provider, cancellationToken);
                if (state.Count > before.Count || !string.Equals(state.Text, before.Text, StringComparison.Ordinal))
                {
                    sawNewContent = true;
                }

                if (!string.Equals(state.Text, lastText, StringComparison.Ordinal))
                {
                    lastText = state.Text;
                    stableSince = DateTime.UtcNow;
                }

                if (sawNewContent &&
                    !state.Generating &&
                    !string.IsNullOrWhiteSpace(state.Text) &&
                    (DateTime.UtcNow - stableSince).TotalSeconds >= 2.5)
                {
                    progress?.Report(state.Text);
                    stopwatch.Stop();
                    _logger.Info(
                        $"Web automation success for {providerId} in {stopwatch.Elapsed.TotalSeconds:F1}s.");
                    return PolisherResult.Ok(state.Text.Trim(), providerId, stopwatch.Elapsed);
                }
            }

            return PolisherResult.Fail(
                ErrorCodes.WebAutomationFailed,
                providerId,
                rawError: "Response did not stabilize before timeout.",
                duration: stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PolisherResult.Fail(ErrorCodes.Cancelled, providerId, duration: stopwatch.Elapsed);
        }
        catch (TimeoutException)
        {
            return PolisherResult.Fail(ErrorCodes.Timeout, providerId, duration: stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.Error($"Web automation failed for {providerId}.", ex);
            return PolisherResult.Fail(
                ErrorCodes.WebAutomationFailed,
                providerId,
                rawError: ex.Message,
                duration: stopwatch.Elapsed);
        }
    }

    private ProviderDescriptor? GetProvider(string providerId) =>
        _state.Settings.Providers.FirstOrDefault(p =>
            p.Enabled &&
            p.Kind == ProviderKind.Web &&
            string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

    private async Task EnsureHostAsync()
    {
        if (_window is { IsLoaded: true })
        {
            return;
        }

        var dataFolder = Path.Combine(_state.WebViewDataDirectory, "shared");
        _logger.Info($"Initializing WebView2 host (data={dataFolder})...");

        _window = new WebViewHostWindow();

        // 关键：先 Show 出窗口句柄，再初始化 WebView2。
        // 未 Show 时 EnsureCoreWebView2Async 可能一直等待 HWND。
        _window.HideToBackground();

        try
        {
            await _window.InitializeAsync(dataFolder)
                .WaitAsync(TimeSpan.FromSeconds(25));
            _logger.Info("WebView2 host initialized.");
        }
        catch (Exception ex)
        {
            _logger.Error("WebView2 host initialization failed.", ex);
            try
            {
                _window.Close();
            }
            catch
            {
                // 初始化失败时忽略关闭异常。
            }

            _window = null;
            throw;
        }
    }

    private async Task NavigateAsync(string url, CancellationToken cancellationToken)
    {
        var webView = _window!.BrowserControl;
        if (string.Equals(webView.Source?.ToString(), url, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
        {
            webView.NavigationCompleted -= Handler;
            completion.TrySetResult(args.IsSuccess);
        }

        webView.NavigationCompleted += Handler;
        webView.CoreWebView2.Navigate(url);

        try
        {
            var success = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            if (!success)
            {
                throw new InvalidOperationException("WebView2 navigation failed.");
            }
        }
        catch (TimeoutException)
        {
            webView.NavigationCompleted -= Handler;
            throw;
        }
    }

    private async Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
    {
        var task = _window!.BrowserControl.ExecuteScriptAsync(script);
        return await task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    private async Task<bool> DetectCaptchaAsync(ProviderDescriptor provider, CancellationToken cancellationToken)
    {
        if (provider.Selectors is null)
        {
            return false;
        }

        var json = await ExecuteScriptAsync(BuildCaptchaScript(provider), cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("captcha", out var captcha) &&
                   captcha.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<bool> DetectLoginAsync(ProviderDescriptor provider, CancellationToken cancellationToken)
    {
        if (provider.Selectors is null)
        {
            return false;
        }

        var json = await ExecuteScriptAsync(BuildLoginScript(provider), cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("loggedIn", out var loggedIn))
            {
                return false;
            }

            return loggedIn.ValueKind == JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<ResponseState> GetResponseStateAsync(
        ProviderDescriptor provider,
        CancellationToken cancellationToken)
    {
        if (provider.Selectors is null || provider.Selectors.Response.Count == 0)
        {
            return new ResponseState(0, string.Empty, false);
        }

        var json = await ExecuteScriptAsync(BuildResponseStateScript(provider), cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var count = root.TryGetProperty("count", out var countElement) ? countElement.GetInt32() : 0;
            var text = root.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
            var generating = root.TryGetProperty("generating", out var generatingElement) &&
                             generatingElement.ValueKind == JsonValueKind.True;
            return new ResponseState(count, text, generating);
        }
        catch (JsonException)
        {
            return new ResponseState(0, string.Empty, false);
        }
    }

    private static bool IsOk(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("ok", out var ok) &&
                   ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildFillScript(ProviderDescriptor provider, string prompt)
    {
        var selectors = JsonSerializer.Serialize(provider.Selectors?.Input ?? new List<string>());
        var text = JsonSerializer.Serialize(prompt);

        return $$"""
            (() => {
              const selectors = {{selectors}};
              const text = {{text}};
              let el = null;
              for (const s of selectors) {
                const list = document.querySelectorAll(s);
                if (list.length) { el = list[list.length - 1]; break; }
              }
              if (!el) return { ok: false, reason: 'input_not_found' };
              el.focus();
              if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {
                const proto = el.tagName === 'TEXTAREA'
                  ? window.HTMLTextAreaElement.prototype
                  : window.HTMLInputElement.prototype;
                const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
                setter.call(el, text);
              } else {
                el.focus();
                try {
                  document.execCommand('selectAll', false, null);
                  document.execCommand('insertText', false, text);
                } catch (e) { }
                if (!el.innerText || !el.innerText.trim()) {
                  el.innerText = text;
                }
                el.dispatchEvent(new InputEvent('beforeinput', {
                  bubbles: true, inputType: 'insertText', data: text
                }));
                el.dispatchEvent(new InputEvent('input', {
                  bubbles: true, inputType: 'insertText', data: text
                }));
              }
              el.dispatchEvent(new Event('keyup', { bubbles: true }));
              el.dispatchEvent(new Event('change', { bubbles: true }));
              return { ok: true };
            })()
            """;
    }

    private static string BuildClickScript(ProviderDescriptor provider)
    {
        var selectors = JsonSerializer.Serialize(provider.Selectors?.SendButton ?? new List<string>());

        return $$"""
            (() => {
              const selectors = {{selectors}};
              let button = null;
              for (const s of selectors) {
                const list = document.querySelectorAll(s);
                if (list.length) { button = list[list.length - 1]; break; }
              }
              if (!button) {
                const candidates = Array.from(document.querySelectorAll('button, [role="button"]'));
                button = candidates.find(b => {
                  const text = (b.innerText || '').trim();
                  const meta = ((b.getAttribute('aria-label') || '') + ' ' + (b.getAttribute('title') || '')).toLowerCase();
                  return text === '发送' || text.includes('发送') ||
                         meta.includes('发送') || meta.includes('send');
                }) || null;
              }
              if (!button) {
                const active = document.activeElement;
                if (active) {
                  active.dispatchEvent(new KeyboardEvent('keydown', {
                    key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true
                  }));
                  return { ok: true, reason: 'enter_fallback' };
                }
                return { ok: false, reason: 'send_not_found' };
              }
              if (button.disabled || button.getAttribute('aria-disabled') === 'true') {
                return { ok: false, reason: 'send_disabled' };
              }
              button.click();
              return { ok: true };
            })()
            """;
    }

    private static string BuildResponseStateScript(ProviderDescriptor provider)
    {
        var selectors = JsonSerializer.Serialize(provider.Selectors?.Response ?? new List<string>());
        var generating = JsonSerializer.Serialize(provider.Selectors?.Generating ?? new List<string>());

        return $$"""
            (() => {
              const selectors = {{selectors}};
              const generatingSelectors = {{generating}};
              let nodes = [];
              for (const s of selectors) {
                const list = document.querySelectorAll(s);
                if (list.length) { nodes = Array.from(list); break; }
              }
              if (!nodes.length) {
                nodes = Array.from(document.querySelectorAll(
                  '[class*="markdown"], [class*="message"], [data-testid*="message"]'
                ));
              }
              const text = nodes.length ? (nodes[nodes.length - 1].innerText || '').trim() : '';
              let isGenerating = false;
              for (const s of generatingSelectors) {
                if (document.querySelector(s)) { isGenerating = true; break; }
              }
              return { count: nodes.length, text: text, generating: isGenerating };
            })()
            """;
    }

    private static string BuildCaptchaScript(ProviderDescriptor provider)
    {
        var textPatterns = JsonSerializer.Serialize(provider.Selectors?.CaptchaText ?? new List<string>());
        var urlPatterns = JsonSerializer.Serialize(provider.Selectors?.CaptchaUrl ?? new List<string>());

        return $$"""
            (() => {
              const textPatterns = {{textPatterns}};
              const urlPatterns = {{urlPatterns}};
              const href = location.href.toLowerCase();
              const body = (document.body ? document.body.innerText : '').toLowerCase();
              for (const p of urlPatterns) {
                if (href.includes(p.toLowerCase())) return { captcha: true, reason: 'url:' + p };
              }
              for (const p of textPatterns) {
                if (body.includes(p.toLowerCase())) return { captcha: true, reason: 'text:' + p };
              }
              return { captcha: false };
            })()
            """;
    }

    private static string BuildLoginScript(ProviderDescriptor provider)
    {
        var loggedIn = JsonSerializer.Serialize(provider.Selectors?.LoggedIn ?? new List<string>());
        var loginText = JsonSerializer.Serialize(provider.Selectors?.LoginText ?? new List<string>());
        var loginUrl = JsonSerializer.Serialize(provider.Selectors?.LoginUrl ?? new List<string>());

        return $$"""
            (() => {
              const loggedInSelectors = {{loggedIn}};
              const loginTextPatterns = {{loginText}};
              const loginUrlPatterns = {{loginUrl}};
              for (const s of loggedInSelectors) {
                if (document.querySelector(s)) return { loggedIn: true, reason: 'logged-in selector' };
              }
              const href = location.href.toLowerCase();
              for (const p of loginUrlPatterns) {
                if (href.includes(p.toLowerCase())) return { loggedIn: false, reason: 'url:' + p };
              }
              const body = (document.body ? document.body.innerText : '').toLowerCase();
              for (const p of loginTextPatterns) {
                if (body.includes(p.toLowerCase())) return { loggedIn: false, reason: 'text:' + p };
              }
              return { loggedIn: true, reason: 'no login indicators' };
            })()
            """;
    }

    public void Dispose()
    {
        try
        {
            _window?.Close();
        }
        catch
        {
            // 退出时忽略。
        }
        finally
        {
            _window = null;
        }
    }

    private sealed record ResponseState(int Count, string Text, bool Generating);
}
