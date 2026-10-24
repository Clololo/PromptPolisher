using System.IO;
using System.Windows;
using PromptPolisher.App.Services;
using PromptPolisher.Core.Services;

namespace PromptPolisher.App;

public partial class App : Application
{
    private FileLogger? _logger;
    private WebViewSessionHost? _webHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.Error("Unhandled UI exception.", args.Exception);
            MessageBox.Show(
                "应用遇到未处理的错误，已写入日志。\n\n" + args.Exception.Message,
                "Prompt 润色工具",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var (viewModel, _, logger, webHost) = Bootstrapper.Build();
            _logger = logger;
            _webHost = webHost;

            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            MessageBox.Show(
                "启动失败：\n" + ex.Message,
                "Prompt 润色工具",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void WriteStartupError(Exception exception)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "PromptPolisher-startup-error.txt");
            File.WriteAllText(path, DateTimeOffset.Now + Environment.NewLine + exception);
        }
        catch
        {
            // 启动失败时不再抛出二次异常。
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _webHost?.Dispose();
        base.OnExit(e);
    }
}
