using System.Windows;
using System.Windows.Threading;
using FileSearch.App.Services;
using FileSearch.App.ViewModels;

namespace FileSearch.App;

public partial class App : Application
{
    private readonly AppLog _log = new(AppLog.DefaultPath);
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Last line of defence: record anything nobody caught, so bug reports have something to go on.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        var opts = CommandLineOptions.Parse(e.Args);

        if (opts.InstallShell || opts.UninstallShell)
        {
            try
            {
                if (opts.InstallShell) ShellIntegration.Install(Environment.ProcessPath!);
                else ShellIntegration.Uninstall();
            }
            catch (Exception ex)
            {
                _log.Error("Explorer context menu change failed", ex);
                MessageBox.Show(ex.Message, "File Search", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        _instance = SingleInstance.Acquire();
        if (!_instance.IsFirst)
        {
            if (SingleInstance.Send(e.Args)) { Shutdown(); return; }
            _log.Warning("The running instance did not answer; starting a separate one.");
        }

        var settings = AppSettings.Load(onError: ex => _log.Warning("Could not read settings, using defaults", ex));
        settings.SaveFailed = ex => _log.Warning("Could not save settings", ex);

        var vm = new MainViewModel(settings, engine: null, new WpfDialogService(), _log);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;

        if (_instance.IsFirst)
        {
            _instance.StartServer(
                args => Dispatcher.BeginInvoke(() =>
                {
                    vm.OpenFromCommandLine(CommandLineOptions.Parse(args));
                    window.BringToFront();
                }),
                ex => _log.Warning("Single-instance pipe error", ex));
        }

        window.Show();
        vm.OpenFromCommandLine(opts);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            $"Something went wrong: {e.Exception.Message}\n\nDetails were written to:\n{AppLog.DefaultPath}",
            "File Search", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // keep the window and the user's other tabs alive
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
