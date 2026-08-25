// ScopeDome Watchdog - Automated recovery system for ScopeDome observatory domes
// Copyright (C) 2026
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Core.Services;

public sealed record ShellyBleOptions(
    string Address,
    int SwitchId = 0,
    TimeSpan? DiscoveryTimeout = null,
    TimeSpan? ConnectTimeout = null,
    TimeSpan? ResponseTimeout = null,
    string ExpectedNamePrefix = "ShellyPlugSG3-")
{
    public TimeSpan EffectiveDiscoveryTimeout => DiscoveryTimeout ?? TimeSpan.FromSeconds(25);
    public TimeSpan EffectiveConnectTimeout => ConnectTimeout ?? TimeSpan.FromSeconds(30);
    public TimeSpan EffectiveResponseTimeout => ResponseTimeout ?? TimeSpan.FromSeconds(12);
}

public interface IShellyBleConnector
{
    Task<IShellyBleConnection> ConnectAsync(
        ShellyBleOptions options,
        CancellationToken cancellationToken);
}

public interface IShellyBleConnection : IAsyncDisposable
{
    Task WriteAsync(
        Guid characteristicUuid,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(Guid characteristicUuid, CancellationToken cancellationToken);
}

public class ShellyBleException : Exception
{
    public ShellyBleException(string message)
        : base(message)
    {
    }

    public ShellyBleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ShellyBleDiscoveryException : ShellyBleException
{
    public ShellyBleDiscoveryException(string message)
        : base(message)
    {
    }

    public ShellyBleDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ShellyBleConnectionException : ShellyBleException
{
    public ShellyBleConnectionException(string message)
        : base(message)
    {
    }

    public ShellyBleConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ShellyBleIdentityException : ShellyBleException
{
    public ShellyBleIdentityException(string message)
        : base(message)
    {
    }
}

public sealed class ShellyBleProvisioningException : ShellyBleException
{
    public ShellyBleProvisioningException(string message)
        : base(message)
    {
    }
}

public sealed class ShellyBleAuthorizationException : ShellyBleException
{
    public ShellyBleAuthorizationException(string message)
        : base(message)
    {
    }

    public ShellyBleAuthorizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ShellyBleProtocolException : ShellyBleException
{
    public ShellyBleProtocolException(string message)
        : base(message)
    {
    }

    public ShellyBleProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ShellyBleRpcException : ShellyBleException
{
    public ShellyBleRpcException(string errorJson)
        : base($"Shelly BLE RPC error: {errorJson}")
    {
        ErrorJson = errorJson;
    }

    public string ErrorJson { get; }
}

public sealed class ShellyBlePlug : IShellySwitchTransport, IDisposable
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private static int _nextRequestId = Environment.TickCount & int.MaxValue;

    public static readonly Guid RpcServiceUuid = Guid.Parse("5f6d4f53-5f52-5043-5f53-56435f49445f");
    public static readonly Guid RpcDataUuid = Guid.Parse("5f6d4f53-5f52-5043-5f64-6174615f5f5f");
    public static readonly Guid RpcTxControlUuid = Guid.Parse("5f6d4f53-5f52-5043-5f74-785f63746c5f");
    public static readonly Guid RpcRxControlUuid = Guid.Parse("5f6d4f53-5f52-5043-5f72-785f63746c5f");

    private readonly IShellyBleConnector _connector;
    private readonly ShellyBleOptions _options;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly Func<int> _requestIdFactory;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private bool _disposed;

    public ShellyBlePlug(IShellyBleConnector connector, ShellyBleOptions options)
        : this(connector, options, CreateRequestId, Task.Delay)
    {
    }

    internal ShellyBlePlug(
        IShellyBleConnector connector,
        ShellyBleOptions options,
        Func<int> requestIdFactory,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _options = ValidateOptions(options);
        _requestIdFactory = requestIdFactory ?? throw new ArgumentNullException(nameof(requestIdFactory));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public ShellyTransport Transport => ShellyTransport.Ble;

    public Task<bool> GetOutputAsync(CancellationToken cancellationToken) =>
        ExecuteSerializedAsync(GetOutputCoreAsync, cancellationToken);

    public Task SetOutputAsync(bool on, CancellationToken cancellationToken) =>
        ExecuteSerializedAsync(
            async (connection, token) =>
            {
                await RpcAsync(
                    connection,
                    "Switch.Set",
                    new Dictionary<string, object> { ["id"] = _options.SwitchId, ["on"] = on },
                    token).ConfigureAwait(false);

                await _delay(TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
                var actual = await GetOutputCoreAsync(connection, token).ConfigureAwait(false);
                if (actual != on)
                {
                    throw new ShellyStateVerificationException(Transport, on, actual);
                }

                return true;
            },
            cancellationToken);

    private async Task<T> ExecuteSerializedAsync<T>(
        Func<IShellyBleConnection, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var connection = await _connector
                        .ConnectAsync(_options, cancellationToken)
                        .ConfigureAwait(false);
                    return await operation(connection, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (
                    attempt == 0 &&
                    !cancellationToken.IsCancellationRequested &&
                    error is ShellyBleDiscoveryException or ShellyBleConnectionException)
                {
                    // Retry one complete discovery/connection attempt. RPC and authorization
                    // failures are deterministic and are deliberately not retried here.
                }
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<bool> GetOutputCoreAsync(
        IShellyBleConnection connection,
        CancellationToken cancellationToken)
    {
        using var response = await RpcAsync(
            connection,
            "Switch.GetStatus",
            new Dictionary<string, object> { ["id"] = _options.SwitchId },
            cancellationToken).ConfigureAwait(false);

        if (!response.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("output", out var output) ||
            output.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ShellyBleProtocolException(
                "Switch.GetStatus response did not contain a Boolean result.output value.");
        }

        return output.GetBoolean();
    }

    private async Task<JsonDocument> RpcAsync(
        IShellyBleConnection connection,
        string method,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        responseTimeout.CancelAfter(_options.EffectiveResponseTimeout);
        try
        {
            var requestId = _requestIdFactory();
            var request = new Dictionary<string, object>
            {
                ["id"] = requestId,
                ["src"] = "scopedome_watchdog_ble_fallback",
                ["method"] = method,
                ["params"] = parameters
            };
            var encoded = JsonSerializer.SerializeToUtf8Bytes(request);
            var length = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)encoded.Length));

            await connection
                .WriteAsync(RpcTxControlUuid, length, responseTimeout.Token)
                .ConfigureAwait(false);
            await _delay(TimeSpan.FromSeconds(1), responseTimeout.Token).ConfigureAwait(false);
            await connection
                .WriteAsync(RpcDataUuid, encoded, responseTimeout.Token)
                .ConfigureAwait(false);

            uint responseLength = 0;
            while (responseLength == 0)
            {
                var lengthBytes = await connection
                    .ReadAsync(RpcRxControlUuid, responseTimeout.Token)
                    .ConfigureAwait(false);
                responseLength = ReadAdvertisedLength(lengthBytes, method);
                if (responseLength == 0)
                {
                    await _delay(TimeSpan.FromMilliseconds(200), responseTimeout.Token).ConfigureAwait(false);
                }
            }

            if (responseLength > MaximumResponseBytes)
            {
                throw new ShellyBleProtocolException(
                    $"BLE RPC response for {method} advertised {responseLength} bytes; " +
                    $"the limit is {MaximumResponseBytes}.");
            }

            var responseBytes = new byte[checked((int)responseLength)];
            var offset = 0;
            while (offset < responseBytes.Length)
            {
                var chunk = await connection
                    .ReadAsync(RpcDataUuid, responseTimeout.Token)
                    .ConfigureAwait(false);
                if (chunk.Length == 0)
                {
                    throw new ShellyBleProtocolException(
                        $"Received an empty BLE RPC data chunk for {method}.");
                }
                if (chunk.Length > responseBytes.Length - offset)
                {
                    throw new ShellyBleProtocolException(
                        $"BLE RPC data for {method} exceeded its advertised response length.");
                }

                chunk.CopyTo(responseBytes.AsMemory(offset));
                offset += chunk.Length;
            }

            JsonDocument response;
            try
            {
                response = JsonDocument.Parse(responseBytes);
            }
            catch (JsonException error)
            {
                throw new ShellyBleProtocolException(
                    $"Invalid JSON in BLE RPC response for {method}.",
                    error);
            }

            if (!response.RootElement.TryGetProperty("id", out var responseId) ||
                !responseId.TryGetInt32(out var actualId) ||
                actualId != requestId)
            {
                var actual = response.RootElement.TryGetProperty("id", out responseId)
                    ? responseId.GetRawText()
                    : "missing";
                response.Dispose();
                throw new ShellyBleProtocolException(
                    $"BLE RPC response ID {actual} did not match request ID {requestId}.");
            }

            if (response.RootElement.TryGetProperty("error", out var rpcError))
            {
                var errorJson = rpcError.GetRawText();
                response.Dispose();
                throw new ShellyBleRpcException(errorJson);
            }

            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"BLE RPC {method} did not complete within {_options.EffectiveResponseTimeout}.");
        }
    }

    private static uint ReadAdvertisedLength(byte[] lengthBytes, string method)
    {
        if (lengthBytes.Length < sizeof(uint))
        {
            throw new ShellyBleProtocolException(
                $"BLE RPC RX control returned {lengthBytes.Length} bytes for {method}; expected four.");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(lengthBytes.AsSpan(0, sizeof(uint)));
    }

    private static ShellyBleOptions ValidateOptions(ShellyBleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Address))
        {
            throw new ArgumentException("A BLE address is required.", nameof(options));
        }
        if (options.SwitchId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Switch ID cannot be negative.");
        }
        if (options.EffectiveDiscoveryTimeout <= TimeSpan.Zero ||
            options.EffectiveConnectTimeout <= TimeSpan.Zero ||
            options.EffectiveResponseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BLE timeouts must be positive.");
        }

        return options;
    }

    private static int CreateRequestId() =>
        Interlocked.Increment(ref _nextRequestId) & int.MaxValue;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operationLock.Dispose();
    }
}
