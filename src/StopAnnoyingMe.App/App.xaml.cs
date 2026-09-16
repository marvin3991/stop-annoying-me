using System.IO;
using System.Windows;
using System.Windows.Threading;
using StopAnnoyingMe.App.Services;
using StopAnnoyingMe.App.Views;
using StopAnnoyingMe.Core;
using StopAnnoyingMe.Core.Data;
using StopAnnoyingMe.Core.Services;

namespace StopAnnoyingMe.App;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;
    private TrayIconController? _tray;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _instanceGuard = new SingleInstanceGuard();
        if (!_instanceGuard.IsFirstInstance)
        {
            // 已經有一份在跑，請它把視窗叫出來然後自己退場。
            _instanceGuard.SignalExistingInstance();
            _instanceGuard.Dispose();
            _instanceGuard = null;
            Shutdown();
            return;
        }

        var paths = AppPaths.Default;
        try
        {
            paths.EnsureCreated();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "打擾記錄器", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        ErrorLog.Initialize(paths.RootDirectory);

        string? startupWarning = null;

        DatabaseInitResult databaseResult;
        try
        {
            databaseResult = DatabaseInitializer.Ensure(paths.DatabasePath);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("資料庫初始化失敗", ex);
            MessageBox.Show(
                $"無法建立或開啟資料庫：\n{paths.DatabasePath}\n\n{ex.Message}",
                "打擾記錄器", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (databaseResult.RecoveredFromCorruption)
        {
            startupWarning =
                $"原本的資料檔無法讀取，已改名保留為 {Path.GetFileName(databaseResult.BackupPath)}，並重新開始記錄。";
        }

        var settingsStore = new SettingsService(paths.SettingsPath);
        var settings = settingsStore.Load();
        if (settingsStore.LastRecoveryBackupPath is not null)
        {
            startupWarning ??= "設定檔損毀，已改名保留並回復成預設值。";
        }

        var repository = new InterruptionRepository(paths.DatabasePath);
        var services = new AppServices
        {
            Paths = paths,
            SettingsStore = settingsStore,
            Settings = settings,
            Repository = repository,
            Interruptions = new InterruptionService(repository),
            Statistics = new StatisticsService(repository),
        };

        _tray = new TrayIconController();
        _mainWindow = new MainWindow(services, _tray, startupWarning);

        _instanceGuard.ActivationRequested += OnActivationRequested;
        _instanceGuard.StartListening();

        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    private void OnActivationRequested() =>
        Dispatcher.BeginInvoke(() => _mainWindow?.BringToFront());

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("未處理的例外", e.Exception);

        MessageBox.Show(
            $"發生未預期的錯誤，詳細內容已記錄到：\n{Path.Combine(AppPaths.Default.RootDirectory, "error.log")}\n\n{e.Exception.Message}",
            "打擾記錄器", MessageBoxButton.OK, MessageBoxImage.Error);

        // 已經回報給使用者，讓程式繼續跑，不要因為一次錯誤就整個關掉。
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ToastWindow.Shutdown();
        _tray?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}
