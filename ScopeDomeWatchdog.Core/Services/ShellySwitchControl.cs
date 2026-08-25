// ScopeDome Watchdog - Automated recovery system for ScopeDome observatory domes
// Copyright (C) 2026
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Core.Services;

public enum ShellyTransport
{
    Tcp,
    Ble
}

public sealed record ShellyOperationResult<T>(T Value, ShellyTransport Transport);

public interface IShellySwitchTransport
{
    ShellyTransport Transport { get; }
    Task<bool> GetOutputAsync(CancellationToken cancellationToken);
    Task SetOutputAsync(bool on, CancellationToken cancellationToken);
}

public sealed class ShellyControlException : Exception
{
    public ShellyControlException(
        string operation,
        bool? targetOutput,
        Exception primaryError,
        Exception fallbackError)
        : base(BuildMessage(operation, targetOutput, primaryError, fallbackError), fallbackError)
    {
        Operation = operation;
        TargetOutput = targetOutput;
        PrimaryError = primaryError;
        FallbackError = fallbackError;
    }

    public string Operation { get; }
    public bool? TargetOutput { get; }
    public Exception PrimaryError { get; }
    public Exception FallbackError { get; }

    private static string BuildMessage(
        string operation,
        bool? targetOutput,
        Exception primaryError,
        Exception fallbackError)
    {
        var target = targetOutput.HasValue ? $" for target output={targetOutput.Value}" : string.Empty;
        return $"Shelly {operation} failed over TCP and BLE{target}: " +
               $"TCP: {DescribeException(primaryError)}; BLE: {DescribeException(fallbackError)}";
    }

    private static string DescribeException(Exception error)
    {
        var message = string.IsNullOrWhiteSpace(error.Message)
            ? "no error text was provided"
            : error.Message;
        return $"{error.GetType().Name} (HRESULT 0x{error.HResult:X8}): {message}";
    }
}

public sealed class ShellyStateVerificationException : Exception
{
    public ShellyStateVerificationException(ShellyTransport transport, bool requested, bool actual)
        : base(
            $"Shelly {transport} verification failed: requested output={requested}, " +
            $"received output={actual}.")
    {
        Transport = transport;
        Requested = requested;
        Actual = actual;
    }

    public ShellyTransport Transport { get; }
    public bool Requested { get; }
    public bool Actual { get; }
}

public sealed class ShellyHttpSwitchTransport : IShellySwitchTransport
{
    private readonly ShellyClient _client;
    private readonly string _ip;
    private readonly int _switchId;

    public ShellyHttpSwitchTransport(ShellyClient client, string ip, int switchId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ip = string.IsNullOrWhiteSpace(ip)
            ? throw new ArgumentException("Shelly IP address is required.", nameof(ip))
            : ip;
        _switchId = switchId >= 0
            ? switchId
            : throw new ArgumentOutOfRangeException(nameof(switchId));
    }

    public ShellyTransport Transport => ShellyTransport.Tcp;
    public string IpAddress => _ip;

    public Task<bool> GetOutputAsync(CancellationToken cancellationToken) =>
        _client.GetSwitchOutputAsync(_ip, _switchId, cancellationToken);

    public Task SetOutputAsync(bool on, CancellationToken cancellationToken) =>
        _client.SetSwitchAsync(_ip, _switchId, on, cancellationToken);
}

public sealed class ShellyTcpFailoverException : IOException
{
    public ShellyTcpFailoverException(string operation, IReadOnlyList<(string Address, Exception Error)> failures)
        : base(
            $"Shelly TCP {operation} failed at every configured address: " +
            string.Join("; ", failures.Select(failure => $"{failure.Address}: {failure.Error.Message}")),
            failures.LastOrDefault().Error)
    {
    }
}

public sealed class ShellyTcpFailoverTransport : IShellySwitchTransport
{
    private readonly IReadOnlyList<ShellyHttpSwitchTransport> _transports;
    private readonly Action<string>? _log;

    public ShellyTcpFailoverTransport(
        IEnumerable<ShellyHttpSwitchTransport> transports,
        Action<string>? log = null)
    {
        _transports = transports?.ToList() ?? throw new ArgumentNullException(nameof(transports));
        if (_transports.Count == 0)
        {
            throw new ArgumentException(
                "At least one Shelly TCP address must be configured.",
                nameof(transports));
        }

        _log = log;
    }

    public ShellyTransport Transport => ShellyTransport.Tcp;

    public Task<bool> GetOutputAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            "status read",
            (transport, token) => transport.GetOutputAsync(token),
            cancellationToken);

    public async Task SetOutputAsync(bool on, CancellationToken cancellationToken)
    {
        _ = await ExecuteAsync(
            $"state change to output={on}",
            async (transport, token) =>
            {
                await transport.SetOutputAsync(on, token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> ExecuteAsync<T>(
        string operation,
        Func<ShellyHttpSwitchTransport, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var failures = new List<(string Address, Exception Error)>();
        for (var index = 0; index < _transports.Count; index++)
        {
            var transport = _transports[index];
            try
            {
                var result = await action(transport, cancellationToken).ConfigureAwait(false);
                if (index > 0)
                {
                    _log?.Invoke($"Shelly TCP {operation} succeeded at {transport.IpAddress}.");
                }

                return result;
            }
            catch (Exception error) when (ShellySwitchController.ShouldFallback(error, cancellationToken))
            {
                failures.Add((transport.IpAddress, error));
                if (failures.Count < _transports.Count)
                {
                    _log?.Invoke(
                        $"Shelly TCP {operation} failed at {transport.IpAddress} ({error.Message}); " +
                        $"trying {_transports[index + 1].IpAddress}.");
                    continue;
                }

                if (failures.Count == 1)
                {
                    throw;
                }

                throw new ShellyTcpFailoverException(operation, failures);
            }
        }

        throw new ShellyTcpFailoverException(operation, failures);
    }
}

public sealed class ShellySwitchController : IDisposable
{
    private readonly IShellySwitchTransport _primary;
    private readonly Func<IShellySwitchTransport?>? _fallbackFactory;
    private readonly bool _ownsFallback;
    private readonly Action<string>? _log;
    private IShellySwitchTransport? _createdFallback;
    private bool _disposed;

    public ShellySwitchController(
        IShellySwitchTransport primary,
        IShellySwitchTransport? fallback = null,
        Action<string>? log = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallbackFactory = fallback == null ? null : () => fallback;
        _ownsFallback = false;
        _log = log;
    }

    public ShellySwitchController(
        IShellySwitchTransport primary,
        Func<IShellySwitchTransport?> fallbackFactory,
        Action<string>? log = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallbackFactory = fallbackFactory ?? throw new ArgumentNullException(nameof(fallbackFactory));
        _ownsFallback = true;
        _log = log;
    }

    public async Task<ShellyOperationResult<bool>> GetOutputAsync(CancellationToken cancellationToken)
    {
        try
        {
            var output = await _primary.GetOutputAsync(cancellationToken).ConfigureAwait(false);
            return new ShellyOperationResult<bool>(output, _primary.Transport);
        }
        catch (Exception primaryError) when (ShouldFallback(primaryError, cancellationToken))
        {
            if (_fallbackFactory == null)
            {
                throw;
            }

            _log?.Invoke($"Shelly TCP status read failed ({primaryError.Message}); attempting BLE fallback.");
            try
            {
                var fallback = GetFallback();
                var output = await fallback.GetOutputAsync(cancellationToken).ConfigureAwait(false);
                return new ShellyOperationResult<bool>(output, fallback.Transport);
            }
            catch (Exception fallbackError) when (fallbackError is not OperationCanceledException)
            {
                throw new ShellyControlException("status read", null, primaryError, fallbackError);
            }
        }
    }

    public async Task<ShellyOperationResult<bool>> SetOutputAsync(
        bool on,
        CancellationToken cancellationToken)
    {
        try
        {
            await _primary.SetOutputAsync(on, cancellationToken).ConfigureAwait(false);
            return new ShellyOperationResult<bool>(on, _primary.Transport);
        }
        catch (Exception primaryError) when (ShouldFallback(primaryError, cancellationToken))
        {
            if (_fallbackFactory == null)
            {
                throw;
            }

            _log?.Invoke(
                $"Shelly TCP state change failed ({primaryError.Message}); " +
                $"attempting idempotent BLE fallback for output={on}.");
            try
            {
                var fallback = GetFallback();
                await fallback.SetOutputAsync(on, cancellationToken).ConfigureAwait(false);
                return new ShellyOperationResult<bool>(on, fallback.Transport);
            }
            catch (Exception fallbackError) when (fallbackError is not OperationCanceledException)
            {
                throw new ShellyControlException("state change", on, primaryError, fallbackError);
            }
        }
    }

    private IShellySwitchTransport GetFallback()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _createdFallback ??= _fallbackFactory?.Invoke() ??
            throw new InvalidOperationException("Shelly BLE fallback is not available.");
    }

    internal static bool ShouldFallback(Exception error, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested || error is OperationCanceledException && error is not TaskCanceledException)
        {
            return false;
        }

        if (error is TaskCanceledException)
        {
            return !callerToken.IsCancellationRequested;
        }

        if (error is HttpRequestException httpError)
        {
            return httpError.StatusCode is null or
                HttpStatusCode.RequestTimeout or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout;
        }

        return error is IOException;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsFallback && _createdFallback is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
