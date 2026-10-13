using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FastEthernetTransfer.Core.Diagnostics;

namespace FastEthernetTransfer.Wpf;

public partial class App : Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            Logger.Info("Fast Ethernet Transfer v2.0 starting up...");
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal startup error in App.OnStartup", ex);
            MessageBox.Show(
                $"Fast Ethernet Transfer encountered an error during startup:\n\n{ex.Message}\n\nCheck logs at:\n{Logger.LogDirectory}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled Dispatcher Exception", e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails have been written to:\n{Logger.LogDirectory}",
            "Application Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Logger.Error("Fatal AppDomain Unhandled Exception", ex);
        }
        else
        {
            Logger.Error($"Fatal AppDomain Unhandled Exception: {e.ExceptionObject}");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.Error("Unobserved Task Exception", e.Exception);
        e.SetObserved();
    }
}
