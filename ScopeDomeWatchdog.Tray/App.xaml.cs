using System;
using System.Windows;
using WpfApplication = System.Windows.Application;
using ScopeDomeWatchdog.Core.Models;
using ScopeDomeWatchdog.Core.Services;
using ScopeDomeWatchdog.Tray.Services;

namespace ScopeDomeWatchdog.Tray;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : WpfApplication
{
	private WatchdogRunner? _runner;
	private TrayIconService? _trayIcon;
	private string? _configPath;
	private WatchdogConfig? _config;
	private MainWindow? _mainWindow;
	private RestartSequenceService? _restartService;
	private ScopeDomeEncoderCacheService? _encoderCacheService;
	private NinaPluginService? _ninaService;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		ShutdownMode = ShutdownMode.OnExplicitShutdown;

		_configPath = ConfigService.GetDefaultConfigPath();
		_config = ConfigService.LoadOrCreate(_configPath);

		var configDir = ConfigService.GetDefaultConfigDirectory();
		
		// Initialize Nina plugin service for sequencer integration
		_ninaService = new NinaPluginService();
		
		_restartService = new RestartSequenceService(_config, configDir, _ninaService);
		_runner = new WatchdogRunner(_config, ct => _restartService.ExecuteAsync(ct), _ninaService);
		_encoderCacheService = new ScopeDomeEncoderCacheService(_config, _configPath);
		_encoderCacheService.Start();
        
		_mainWindow = new MainWindow(_runner, _config, _configPath, _restartService, _ => _encoderCacheService?.RequestImmediateRefresh());
		
		// Subscribe to encoder updates so UI refreshes when encoder value changes
		_encoderCacheService.EncoderUpdated += () => _mainWindow?.Dispatcher.Invoke(_mainWindow.RefreshEncoderDisplay);
        // Don't auto-start; user clicks the button
        // _runner.Start();
		_mainWindow.Closing += (_, args) =>
		{
			if (!_mainWindow.IsExitRequested)
			{
				args.Cancel = true;
				_mainWindow.Hide();
			}
		};

		_trayIcon = new TrayIconService(
			onOpen: () => _mainWindow.ShowAndActivate(),
			onRestartNow: () => _runner.TriggerManualRestart(),
			onViewLogs: () => _mainWindow.OpenLogsFolder(),
			onSettings: () => _mainWindow.OpenSettings(),
			onExit: ExitApplication);

		if (_config.StartMinimizedToTray)
		{
			_mainWindow.Hide();
		}
		else
		{
			_mainWindow.Show();
		}
	}

	private void ExitApplication()
	{
		if (_mainWindow != null)
		{
			_mainWindow.IsExitRequested = true;
			_mainWindow.Close();
		}

		_trayIcon?.Dispose();
		_encoderCacheService?.Dispose();
		_restartService?.Dispose();
		_ninaService?.Dispose();
		_runner?.Dispose();

		Shutdown();
	}
}

