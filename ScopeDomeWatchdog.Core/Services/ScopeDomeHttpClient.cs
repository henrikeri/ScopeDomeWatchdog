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

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class ScopeDomeHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthenticationHeaderValue? _authHeader;

    public ScopeDomeHttpClient(TimeSpan timeout, string? username, string? password)
    {
        _httpClient = new HttpClient { Timeout = timeout };

        if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
        {
            var raw = $"{username}:{password}";
            var bytes = Encoding.ASCII.GetBytes(raw);
            _authHeader = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }
    }

    public async Task<int?> GetEncoderValueAsync(string domeIp, CancellationToken cancellationToken)
    {
        var url = $"http://{domeIp}/?getStatus";
        var response = await SendGetAsync(url, cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var parts = response.Split(';');
        if (parts.Length <= 2)
        {
            return null;
        }

        var valueText = parts[2].Trim();
        if (int.TryParse(valueText, out var value))
        {
            return value;
        }

        return null;
    }

    public Task<string> SetEncoderValueAsync(string domeIp, int encoderValue, CancellationToken cancellationToken)
    {
        var url = $"http://{domeIp}/?setEncoderA={encoderValue}";
        return SendGetAsync(url, cancellationToken);
    }

    private async Task<string> SendGetAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (_authHeader != null)
        {
            request.Headers.Authorization = _authHeader;
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
