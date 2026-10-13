using System;
using System.Diagnostics;
using System.IO;

namespace FastEthernetTransfer.Core.Diagnostics;

public static class Logger
{
    private static readonly Lock _lock = new();
    private static readonly string _logDirectory;
    private static readonly string _logFilePath;

    public static event Action<string>? OnLog;

    static Logger()
    {
        try
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FastEthernetTransfer",
                "Logs");

            Directory.CreateDirectory(_logDirectory);
            _logFilePath = Path.Combine(_logDirectory, $"transfer_{DateTime.Now:yyyyMMdd}.log");
        }
        catch
        {
            _logDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _logFilePath = Path.Combine(_logDirectory, "transfer.log");
        }
    }

    public static string LogDirectory => _logDirectory;

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message, Exception? ex = null)
    {
        string fullMessage = ex != null ? $"{message} - {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}" : message;
        Log("ERROR", fullMessage);
    }

    private static void Log(string level, string message)
    {
        string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        Debug.WriteLine(entry);

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, entry + Environment.NewLine);
            }
            catch
            {
                // Never crash because of logging failure
            }
        }

        try
        {
            OnLog?.Invoke(entry);
        }
        catch
        {
            // Ignore subscriber error
        }
    }

    public static void OpenLogsFolder()
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _logDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }
        catch (Exception ex)
        {
            Error("Failed to open logs folder", ex);
        }
    }
}
