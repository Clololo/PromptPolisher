using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace PromptPolisher.App.Views;

/// <summary>
/// 单例 WebView2 宿主：登录时显示为正常窗口，后台调用时移到屏幕外。
/// 这样不会弹出外部浏览器，也不会每次新建会话。
/// </summary>
public partial class WebViewHostWindow : Window
{
    private TaskCompletionSource<bool>? _loginTcs;

    public WebViewHostWindow()
    {
        InitializeComponent();
    }

    public WebView2 BrowserControl => Browser;

    public async Task InitializeAsync(string userDataFolder)
    {
        Directory.CreateDirectory(userDataFolder);
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await Browser.EnsureCoreWebView2Async(environment);

        Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
    }

    public Task<bool> ShowForLoginAsync(string title, string url)
    {
        _loginTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        LoginTitle.Text = title;
        LoginBar.Visibility = Visibility.Visible;
        Title = title;

        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        Width = 900;
        Height = 600;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        ShowInTaskbar = true;
        Topmost = true;

        Show();
        Activate();
        Browser.CoreWebView2.Navigate(url);

        return _loginTcs.Task;
    }

    public void HideToBackground()
    {
        LoginBar.Visibility = Visibility.Collapsed;
        Title = "PromptPolisher Web Session";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Width = 1024;
        Height = 768;
        Left = -20000;
        Top = -20000;
        ShowInTaskbar = false;
        Topmost = false;

        if (!IsVisible)
        {
            Show();
        }
    }

    private void CompleteLogin_Click(object sender, RoutedEventArgs e)
    {
        _loginTcs?.TrySetResult(true);
        HideToBackground();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _loginTcs?.TrySetResult(false);
        base.OnClosing(e);
    }
}
