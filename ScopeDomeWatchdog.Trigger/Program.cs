using System;
using System.IO;
using System.Threading;
using ScopeDomeWatchdog.Core.Services;

namespace ScopeDomeWatchdog.Trigger;

public static class Program
{
	public static int Main(string[] args)
	{
		string? eventName = null;
		string? configPath = null;

		for (var i = 0; i < args.Length; i++)
		{
			var arg = args[i];
			if (string.Equals(arg, "--eventName", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
			{
				eventName = args[++i];
			}
			else if (string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
			{
				configPath = args[++i];
			}
		}

		if (!string.IsNullOrWhiteSpace(configPath))
		{
			try
			{
				if (!File.Exists(configPath))
				{
					Console.WriteLine($"Config not found: {configPath}");
					return 1;
				}

				var cfg = ConfigService.Load(configPath);
				eventName ??= cfg.TriggerEventName;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to load config: {ex.Message}");
				return 1;
			}
		}

		eventName ??= "ScopeDomeWatchdog.TriggerRestart";

		try
		{
			using var evt = new EventWaitHandle(false, EventResetMode.ManualReset, eventName, out _);
			try
			{
				evt.Set();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to signal event '{eventName}': {ex.Message}");
				return 2;
			}

			Console.WriteLine($"Restart requested: event '{eventName}' signaled.");
			return 0;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to open/create event '{eventName}': {ex.Message}");
			return 1;
		}
	}
}