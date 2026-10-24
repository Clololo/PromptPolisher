using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Security;

namespace PromptPolisher.Core.Services;

/// <summary>
/// 极简文件日志：按天滚动；所有消息先脱敏。默认不记录 Prompt 正文。
/// </summary>
public sealed class FileLogger : IRedactingLogger
{
    private readonly object _gate = new();

    public FileLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        LogFilePath = Path.Combine(logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
    }

    public string LogFilePath { get; }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {Redaction.Redact(message)}";

        if (exception is not null)
        {
            line += Environment.NewLine + Redaction.Redact(exception.ToString());
        }

        try
        {
            lock (_gate)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志失败不能让应用崩溃。
        }
    }
}
