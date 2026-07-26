using System.Diagnostics;
using System.Windows;
using LumaClip.Core;
using LumaClip.Services;

namespace LumaClip;

public partial class App : System.Windows.Application
{
    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    CancellationTokenSource? _listenerCancellation;
    bool _restartRequested;
    public AppServices Services { get; private set; } = null!;
    public MainWindow MainWindowInstance { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Local\LumaClip.SingleInstance.v1", out var createdNew);
        if (!createdNew) {
            try { EventWaitHandle.OpenExisting(@"Local\LumaClip.ShowExisting.v1").Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LumaClip.ShowExisting.v1");
        _listenerCancellation = new CancellationTokenSource();
        _ = ListenForSecondInstanceAsync(_listenerCancellation.Token);

        try {
            Services = new AppServices();
            ThemeManager.Apply(Services.Settings.Current.Theme, Services.Settings.Current.ReducedTransparency);
            await Services.InitializeAsync();
            DispatcherUnhandledException += (_, args) => {
                Services.Logger.Error("ui_unhandled_exception", args.Exception);
                MessageBox.Show("LumaClip 遇到一个可恢复错误：\n" + args.Exception.Message, "LumaClip", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };
            MainWindowInstance = new MainWindow(Services);
            MainWindowInstance.Show();
            if (e.Args.Contains("--minimized") || Services.Settings.Current.LaunchMinimized) MainWindowInstance.Hide();
        } catch (Exception ex) {
            MessageBox.Show($"LumaClip 启动失败：\n{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    async Task ListenForSecondInstanceAsync(CancellationToken token)
    {
        if (_showEvent is null) return;
        while (!token.IsCancellationRequested) {
            try {
                var signaled = await Task.Run(() => WaitHandle.WaitAny([_showEvent, token.WaitHandle]) == 0, token);
                if (signaled && MainWindowInstance is not null) await Dispatcher.InvokeAsync(MainWindowInstance.ShowMain);
            } catch (OperationCanceledException) { return; }
        }
    }

    public void Restart()
    {
        _restartRequested = true;
        MainWindowInstance.ExitApplication();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _listenerCancellation?.Cancel();
        _showEvent?.Dispose();
        try { Services?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
        if (_restartRequested && !string.IsNullOrWhiteSpace(Environment.ProcessPath)) {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true }); } catch { }
        }
    }
}
