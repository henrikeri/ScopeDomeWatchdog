// Example: Using the NinaPluginService in ScopeDomeWatchdog
// This file demonstrates how the watchdog notifies Nina about dome reconnection events

using System;
using System.Threading;
using System.Threading.Tasks;
using ScopeDomeWatchdog.Core.Services;

namespace ScopeDomeWatchdog.Examples;

/// <summary>
/// Example demonstrating Nina plugin integration with ScopeDomeWatchdog
/// </summary>
public class NinaIntegrationExample
{
    public static async Task Example_BasicFlow()
    {
        // Create the service (already done in App.xaml.cs)
        var ninaService = new NinaPluginService();

        try
        {
            // Scenario 1: Successful reconnection
            Console.WriteLine("Starting dome reconnection...");
            await ninaService.SignalReconnectionStartAsync();

            // Do actual dome reconnection work here...
            await Task.Delay(3000); // Simulate reconnection work

            // Signal completion
            Console.WriteLine("Dome reconnection complete!");
            await ninaService.SignalReconnectionCompleteAsync();

            // Nina will automatically resume the sequencer
            Console.WriteLine("Nina should resume sequencer now");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            
            // Signal failure if something went wrong
            await ninaService.SignalReconnectionFailedAsync(ex.Message);
        }
    }

    public static async Task Example_MonitoringNinaPause()
    {
        var ninaService = new NinaPluginService();

        // Subscribe to state changes
        ninaService.ReconnectionStateChanged += (sender, args) =>
        {
            Console.WriteLine($"Reconnection state: {args.IsReconnecting}, Reason: {args.Reason}");
        };

        // Start reconnection
        await ninaService.SignalReconnectionStartAsync();

        // Wait for Nina to pause (with 30-second timeout)
        var paused = await ninaService.WaitForNinaPauseAsync(TimeSpan.FromSeconds(30));
        
        if (paused)
        {
            Console.WriteLine("Nina has paused the sequencer");
            
            // Do dome reconnection work while Nina is paused
            await DoReconnectionWork();
            
            // Complete and signal resume
            await ninaService.SignalReconnectionCompleteAsync();
            await ninaService.SignalNinaResumeAsync();
            Console.WriteLine("Signaled Nina to resume");
        }
        else
        {
            Console.WriteLine("Nina did not pause within timeout");
        }
    }

    public static async Task Example_ErrorHandling()
    {
        var ninaService = new NinaPluginService();

        try
        {
            await ninaService.SignalReconnectionStartAsync();

            // Simulate reconnection failure
            bool success = await AttemptDomeReconnection();

            if (success)
            {
                await ninaService.SignalReconnectionCompleteAsync();
            }
            else
            {
                // Signal the failure with details
                await ninaService.SignalReconnectionFailedAsync(
                    "Failed to connect to dome ASCOM driver after 5 attempts"
                );
                
                // Nina will resume with the failed state
                // The DomeReconnectionPauseInstruction will see this and fail appropriately
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical error: {ex.Message}");
            await ninaService.SignalReconnectionFailedAsync($"Critical error: {ex.Message}");
        }
    }

    public static async Task Example_PropertyQueryAPI()
    {
        var ninaService = new NinaPluginService();

        // You can query the current state properties
        bool isReconnecting = ninaService.IsReconnectingDome;
        bool isSomeonePaused = ninaService.IsNinaPaused;

        Console.WriteLine($"Is dome reconnecting? {isReconnecting}");
        Console.WriteLine($"Is Nina paused? {isSomeonePaused}");

        // These can be used for logging, UI updates, or conditional logic
        if (isReconnecting)
        {
            Console.WriteLine("Dome reconnection is in progress");
        }
    }

    // Helper methods
    private static async Task DoReconnectionWork()
    {
        // Simulated dome reconnection work
        // In reality, this would be:
        // - Power cycling the dome device
        // - Waiting for ASCOM reconnection
        // - Running homing sequence
        // - Starting remote dome process
        await Task.Delay(5000);
    }

    private static async Task<bool> AttemptDomeReconnection()
    {
        // Simulated reconnection attempt with potential failure
        var random = new Random();
        await Task.Delay(2000);
        return random.Next(0, 2) == 0; // 50% chance of success
    }
}

/// <summary>
/// Example showing how to integrate Nina notifications with logging
/// </summary>
public class NinaLoggingExample
{
    private readonly INinaPluginService _ninaService;
    private readonly ILogger _logger; // Your logging service

    public NinaLoggingExample(INinaPluginService ninaService, ILogger logger)
    {
        _ninaService = ninaService;
        _logger = logger;

        // Subscribe to all state changes for comprehensive logging
        _ninaService.ReconnectionStateChanged += LogStateChange;
    }

    private void LogStateChange(object? sender, DomeReconnectionStateChangedEventArgs args)
    {
        var message = args.IsReconnecting
            ? $"Dome reconnection started: {args.Reason}"
            : $"Dome reconnection ended: {args.Reason}";

        _logger?.Log(message);

        // Could also update UI, metrics, or database
    }

    public async Task RunWithLogging()
    {
        _logger?.Log("Starting dome reconnection sequence...");
        
        try
        {
            await _ninaService.SignalReconnectionStartAsync();
            _logger?.Log("Signals sent to Nina plugin");

            // Do work...

            await _ninaService.SignalReconnectionCompleteAsync();
            _logger?.Log("Reconnection completed successfully");
        }
        catch (Exception ex)
        {
            _logger?.Log($"Error during reconnection: {ex.Message}");
            await _ninaService.SignalReconnectionFailedAsync(ex.Message);
        }
    }
}

/// <summary>
/// Minimal interface definition for logging (implement as needed)
/// </summary>
public interface ILogger
{
    void Log(string message);
}
