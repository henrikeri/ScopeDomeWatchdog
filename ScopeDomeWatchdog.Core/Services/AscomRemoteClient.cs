// ScopeDome Watchdog - Automated recovery system for ScopeDome observatory domes
// Copyright (C) 2026
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Net.Http;
using System.Text.Json;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class AscomRemoteClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public AscomRemoteClient(string baseUrl, TimeSpan timeout)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("ASCOM Remote URL must be an absolute HTTP or HTTPS URL.", nameof(baseUrl));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "ASCOM Remote timeout must be greater than zero.");
        }

        _baseUri = baseUri;
        _httpClient = new HttpClient { Timeout = timeout };
    }

    public async Task ReloadHostedDriversAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri("/server/v1/restart"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureHttpSuccess(response, json, "ASCOM Remote driver reload");
        EnsureAlpacaSuccess(json, "ASCOM Remote driver reload");
    }

    public async Task<bool> GetDomeConnectedAsync(int deviceNumber, CancellationToken cancellationToken)
    {
        if (deviceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceNumber), "ASCOM Remote dome device number cannot be negative.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri($"/api/v1/dome/{deviceNumber}/connected"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureHttpSuccess(response, json, "ASCOM Remote dome connection check");
        EnsureAlpacaSuccess(json, "ASCOM Remote dome connection check");

        using var document = JsonDocument.Parse(json);
        if (!TryGetProperty(document.RootElement, "Value", out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            throw new InvalidOperationException("ASCOM Remote dome connection response did not contain a Boolean Value.");
        }

        return value.GetBoolean();
    }

    private Uri BuildUri(string path)
    {
        var builder = new UriBuilder(_baseUri)
        {
            Path = path,
            Query = string.Empty
        };
        return builder.Uri;
    }

    private static void EnsureHttpSuccess(HttpResponseMessage response, string responseBody, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = string.IsNullOrWhiteSpace(responseBody) ? string.Empty : $": {responseBody.Trim()}";
        throw new HttpRequestException(
            $"{operation} returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}{detail}",
            null,
            response.StatusCode);
    }

    private static void EnsureAlpacaSuccess(string json, string operation)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        if (!TryGetProperty(document.RootElement, "ErrorNumber", out var errorNumberElement) ||
            errorNumberElement.ValueKind != JsonValueKind.Number ||
            !errorNumberElement.TryGetInt32(out var errorNumber) ||
            errorNumber == 0)
        {
            return;
        }

        var errorMessage = TryGetProperty(document.RootElement, "ErrorMessage", out var errorMessageElement)
            ? errorMessageElement.GetString()
            : null;
        throw new InvalidOperationException($"{operation} failed with Alpaca error {errorNumber}: {errorMessage}");
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
