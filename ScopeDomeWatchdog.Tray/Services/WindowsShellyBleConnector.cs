// ScopeDome Watchdog - Automated recovery system for ScopeDome observatory domes
// Copyright (C) 2026
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScopeDomeWatchdog.Core.Services;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

namespace ScopeDomeWatchdog.Tray.Services;

/// <summary>
/// Uses the Windows BLE stack and the persistent bond created during installation.
/// This class never pairs, removes bonds, or changes device configuration.
/// </summary>
public sealed class WindowsShellyBleConnector : IShellyBleConnector, IShellyBleScanner
{
    public async Task<IReadOnlyList<ShellyBleScanResult>> ScanAsync(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "BLE scan duration must be positive.");
        }

        var devices = new ConcurrentDictionary<ulong, ShellyBleScanResult>();
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        void Received(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var address = FormatBluetoothAddress(args.BluetoothAddress);
            var observedName = args.Advertisement.LocalName ?? string.Empty;
            devices.AddOrUpdate(
                args.BluetoothAddress,
                _ => new ShellyBleScanResult(address, observedName, args.RawSignalStrengthInDBm),
                (_, current) => new ShellyBleScanResult(
                    address,
                    string.IsNullOrWhiteSpace(observedName) ? current.AdvertisedName : observedName,
                    Math.Max(current.SignalStrengthDbm, args.RawSignalStrengthInDBm)));
        }

        watcher.Received += Received;
        try
        {
            try
            {
                watcher.Start();
            }
            catch (UnauthorizedAccessException error)
            {
                throw new ShellyBleAuthorizationException(
                    "Windows denied access to Bluetooth scanning. Check Bluetooth permissions " +
                    "for the account running ScopeDomeWatchdog.",
                    error);
            }

            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
            if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Aborted)
            {
                throw new ShellyBleDiscoveryException(
                    "The Windows Bluetooth scan stopped unexpectedly. Confirm that a BLE adapter " +
                    "is installed and Bluetooth is enabled.");
            }
        }
        finally
        {
            try
            {
                watcher.Stop();
            }
            catch
            {
                // The watcher may already be stopped or aborted.
            }
            watcher.Received -= Received;
        }

        return OrderScanResults(devices.Values);
    }

    internal static IReadOnlyList<ShellyBleScanResult> OrderScanResults(
        IEnumerable<ShellyBleScanResult> devices) =>
        devices
            .OrderBy(device =>
                device.AdvertisedName.StartsWith("Shelly", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(device => string.IsNullOrWhiteSpace(device.AdvertisedName) ? 1 : 0)
            .ThenByDescending(device => device.SignalStrengthDbm)
            .ThenBy(device => device.AdvertisedName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<IShellyBleConnection> ConnectAsync(
        ShellyBleOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var expectedAddress = ParseBluetoothAddress(options.Address);
        DiscoveredDevice discovered;
        try
        {
            discovered = await DiscoverAsync(
                expectedAddress,
                options.ExpectedNamePrefix,
                options.EffectiveDiscoveryTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException error)
        {
            throw CreateAuthorizationError(error);
        }

        if (!string.IsNullOrWhiteSpace(discovered.Name) &&
            !string.IsNullOrWhiteSpace(options.ExpectedNamePrefix) &&
            !discovered.Name.StartsWith(options.ExpectedNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ShellyBleIdentityException(
                $"Unexpected BLE device name '{discovered.Name}' at {options.Address}.");
        }

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(options.EffectiveConnectTimeout);

        BluetoothLEDevice? device = null;
        GattDeviceService? service = null;
        var stage = "opening the BLE device";
        try
        {
            device = await BluetoothLEDevice
                .FromBluetoothAddressAsync(discovered.Address)
                .AsTask(connectTimeout.Token)
                .ConfigureAwait(false);
            if (device == null)
            {
                throw new ShellyBleConnectionException(
                    $"Windows could not open BLE device {options.Address}.");
            }

            if (!device.DeviceInformation.Pairing.IsPaired)
            {
                throw new ShellyBleAuthorizationException(
                    "The Shelly BLE device is not bonded for the current Windows account. " +
                    "Enable RPC over Bluetooth and pair it once in Windows Settings.");
            }

            stage = "discovering the Shelly RPC service";
            var services = await device
                .GetGattServicesForUuidAsync(ShellyBlePlug.RpcServiceUuid, BluetoothCacheMode.Cached)
                .AsTask(connectTimeout.Token)
                .ConfigureAwait(false);
            EnsureGattSuccess(services.Status, services.ProtocolError, "discover Shelly RPC service");
            service = services.Services.FirstOrDefault();
            if (service == null)
            {
                throw new ShellyBleProvisioningException(
                    "The Shelly RPC BLE service was not advertised. Confirm ble.rpc.enable=true.");
            }

            var characteristics = new Dictionary<Guid, GattCharacteristic>();
            foreach (var uuid in new[]
                     {
                         ShellyBlePlug.RpcDataUuid,
                         ShellyBlePlug.RpcTxControlUuid,
                         ShellyBlePlug.RpcRxControlUuid
                     })
            {
                stage = $"discovering Shelly RPC characteristic {uuid}";
                var result = await service
                    .GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Cached)
                    .AsTask(connectTimeout.Token)
                    .ConfigureAwait(false);
                EnsureGattSuccess(result.Status, result.ProtocolError, $"discover characteristic {uuid}");
                var characteristic = result.Characteristics.FirstOrDefault();
                if (characteristic == null)
                {
                    throw new ShellyBleProvisioningException(
                        $"Shelly RPC BLE characteristic {uuid} was not found.");
                }

                characteristics.Add(uuid, characteristic);
            }

            return new WindowsShellyBleConnection(device, service, characteristics);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DisposeBestEffort(service);
            DisposeBestEffort(device);
            throw new ShellyBleConnectionException(
                $"Timed out connecting to Shelly BLE device {options.Address}.");
        }
        catch (UnauthorizedAccessException error)
        {
            DisposeBestEffort(service);
            DisposeBestEffort(device);
            throw CreateAuthorizationError(error);
        }
        catch (ShellyBleException)
        {
            DisposeBestEffort(service);
            DisposeBestEffort(device);
            throw;
        }
        catch (Exception error)
        {
            DisposeBestEffort(service);
            DisposeBestEffort(device);
            throw new ShellyBleConnectionException(
                $"Windows BLE failed while {stage}: {DescribeException(error)}",
                error);
        }
    }

    private static async Task<DiscoveredDevice> DiscoverAsync(
        ulong expectedAddress,
        string expectedNamePrefix,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<DiscoveredDevice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        void Received(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (args.BluetoothAddress != expectedAddress)
            {
                return;
            }

            var name = args.Advertisement.LocalName;
            if (string.IsNullOrWhiteSpace(expectedNamePrefix))
            {
                completion.TrySetResult(new DiscoveredDevice(args.BluetoothAddress, name));
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                if (name.StartsWith(expectedNamePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    completion.TrySetResult(new DiscoveredDevice(args.BluetoothAddress, name));
                }
                else
                {
                    completion.TrySetException(new ShellyBleIdentityException(
                        $"Unexpected BLE device name '{name}' at " +
                        $"{FormatBluetoothAddress(expectedAddress)}."));
                }
            }
        }

        watcher.Received += Received;
        try
        {
            try
            {
                watcher.Start();
            }
            catch (UnauthorizedAccessException error)
            {
                throw CreateAuthorizationError(error);
            }
            catch (Exception error)
            {
                throw new ShellyBleDiscoveryException(
                    $"Windows could not start BLE discovery: {DescribeException(error)}",
                    error);
            }

            try
            {
                return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException error)
            {
                throw new ShellyBleDiscoveryException(
                    $"Shelly BLE device {FormatBluetoothAddress(expectedAddress)}" +
                    (string.IsNullOrWhiteSpace(expectedNamePrefix)
                        ? string.Empty
                        : $" with name prefix '{expectedNamePrefix}'") +
                    $" was not found within {timeout}.",
                    error);
            }
        }
        finally
        {
            try
            {
                watcher.Stop();
            }
            catch
            {
                // A watcher that Windows has already aborted may reject Stop().
            }
            watcher.Received -= Received;
        }
    }

    private static ulong ParseBluetoothAddress(string address)
    {
        var compact = address.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (compact.Length != 12 ||
            !ulong.TryParse(compact, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException(
                "BLE address must contain twelve hexadecimal digits, for example 8C:BF:EA:99:9C:DE.",
                nameof(address));
        }

        return parsed;
    }

    private static string FormatBluetoothAddress(ulong address)
    {
        var hex = address.ToString("X12", CultureInfo.InvariantCulture);
        return string.Join(":", Enumerable.Range(0, 6).Select(index => hex.Substring(index * 2, 2)));
    }

    private static void EnsureGattSuccess(
        GattCommunicationStatus status,
        byte? protocolError,
        string operation)
    {
        if (status == GattCommunicationStatus.Success)
        {
            return;
        }

        if (status == GattCommunicationStatus.AccessDenied || IsAuthorizationProtocolError(protocolError))
        {
            throw CreateAuthorizationError(protocolError);
        }

        throw new ShellyBleConnectionException(
            $"BLE GATT operation '{operation}' failed with status {status}" +
            (protocolError.HasValue ? $" and protocol error 0x{protocolError.Value:X2}." : "."));
    }

    private static bool IsAuthorizationProtocolError(byte? protocolError) =>
        protocolError is 0x05 or 0x08 or 0x0F;

    private static ShellyBleAuthorizationException CreateAuthorizationError(Exception innerException) =>
        new(
            "Shelly BLE authorization failed. Confirm RPC over Bluetooth is enabled and the device " +
            "is bonded in Windows for the account running ScopeDomeWatchdog.",
            innerException);

    private static ShellyBleAuthorizationException CreateAuthorizationError(byte? protocolError) =>
        new(
            "Shelly BLE authorization failed" +
            (protocolError.HasValue ? $" (GATT 0x{protocolError.Value:X2})" : string.Empty) +
            ". Confirm RPC over Bluetooth is enabled and the device is bonded in Windows for the " +
            "account running ScopeDomeWatchdog.");

    private sealed record DiscoveredDevice(ulong Address, string? Name);

    private sealed class WindowsShellyBleConnection : IShellyBleConnection
    {
        private readonly BluetoothLEDevice _device;
        private readonly GattDeviceService _service;
        private readonly IReadOnlyDictionary<Guid, GattCharacteristic> _characteristics;
        private bool _disposed;

        public WindowsShellyBleConnection(
            BluetoothLEDevice device,
            GattDeviceService service,
            IReadOnlyDictionary<Guid, GattCharacteristic> characteristics)
        {
            _device = device;
            _service = service;
            _characteristics = characteristics;
        }

        public async Task WriteAsync(
            Guid characteristicUuid,
            ReadOnlyMemory<byte> value,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var characteristic = GetCharacteristic(characteristicUuid);
            var buffer = CryptographicBuffer.CreateFromByteArray(value.ToArray());
            try
            {
                var result = await characteristic
                    .WriteValueWithResultAsync(buffer, GattWriteOption.WriteWithResponse)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                EnsureGattSuccess(result.Status, result.ProtocolError, $"write characteristic {characteristicUuid}");
            }
            catch (UnauthorizedAccessException error)
            {
                throw CreateAuthorizationError(error);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ShellyBleException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new ShellyBleConnectionException(
                    $"Windows BLE write to characteristic {characteristicUuid} failed: " +
                    DescribeException(error),
                    error);
            }
        }

        public async Task<byte[]> ReadAsync(
            Guid characteristicUuid,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var characteristic = GetCharacteristic(characteristicUuid);
            try
            {
                var result = await characteristic
                    .ReadValueAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                EnsureGattSuccess(result.Status, result.ProtocolError, $"read characteristic {characteristicUuid}");
                CryptographicBuffer.CopyToByteArray(result.Value, out var bytes);
                return bytes;
            }
            catch (UnauthorizedAccessException error)
            {
                throw CreateAuthorizationError(error);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ShellyBleException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new ShellyBleConnectionException(
                    $"Windows BLE read from characteristic {characteristicUuid} failed: " +
                    DescribeException(error),
                    error);
            }
        }

        private GattCharacteristic GetCharacteristic(Guid uuid) =>
            _characteristics.TryGetValue(uuid, out var characteristic)
                ? characteristic
                : throw new ShellyBleConnectionException($"BLE characteristic {uuid} is not available.");

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            try
            {
                _service.Dispose();
            }
            catch
            {
                // Disposal must not hide the result of a completed BLE RPC operation.
            }
            try
            {
                _device.Dispose();
            }
            catch
            {
                // Disposal must not hide the result of a completed BLE RPC operation.
            }
            return ValueTask.CompletedTask;
        }
    }

    private static string DescribeException(Exception error)
    {
        var message = string.IsNullOrWhiteSpace(error.Message)
            ? "no Windows error text was provided"
            : error.Message;
        return $"{error.GetType().Name} (HRESULT 0x{error.HResult:X8}): {message}";
    }

    private static void DisposeBestEffort(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
            // Cleanup must not replace the actionable connection error.
        }
    }
}
