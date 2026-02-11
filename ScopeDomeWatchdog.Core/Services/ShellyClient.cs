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
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class ShellyClient
{
    private readonly HttpClient _httpClient;

    public ShellyClient(TimeSpan timeout)
    {
        _httpClient = new HttpClient { Timeout = timeout };
    }

    public async Task<List<(int id, string name)>> EnumerateRelaysAsync(string ip, CancellationToken cancellationToken)
    {
        var relays = new List<(int, string)>();
        
        // Try to get device info to determine how many switches are available
        try
        {
            var infoUrl = $"http://{ip}/rpc/Shelly.GetInfo";
            var json = await _httpClient.GetStringAsync(infoUrl, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            
            // Parse the response to find switch components
            if (doc.RootElement.TryGetProperty("components", out var components))
            {
                var maxId = 0;
                for (int i = 0; i < components.GetArrayLength(); i++)
                {
                    var component = components[i];
                    if (component.TryGetProperty("key", out var key))
                    {
                        var keyStr = key.GetString();
                        if (keyStr?.StartsWith("switch:") == true)
                        {
                            if (int.TryParse(keyStr.Substring("switch:".Length), out var id))
                            {
                                if (id > maxId) maxId = id;
                            }
                        }
                    }
                }
                
                // Query each switch from 0 to maxId
                for (int id = 0; id <= maxId; id++)
                {
                    try
                    {
                        var statusUrl = $"http://{ip}/rpc/Switch.GetStatus?id={id}";
                        var statusJson = await _httpClient.GetStringAsync(statusUrl, cancellationToken);
                        using var statusDoc = JsonDocument.Parse(statusJson);
                        
                        var name = $"Switch {id}";
                        if (statusDoc.RootElement.TryGetProperty("name", out var nameElem))
                        {
                            var nameStr = nameElem.GetString();
                            if (!string.IsNullOrWhiteSpace(nameStr))
                            {
                                name = nameStr;
                            }
                        }
                        
                        relays.Add((id, name));
                    }
                    catch
                    {
                        // Switch doesn't exist or is unavailable, skip it
                    }
                }
            }
        }
        catch
        {
            // If we can't get device info, try a simpler approach: query switches 0-9 individually
            for (int id = 0; id < 10; id++)
            {
                try
                {
                    var statusUrl = $"http://{ip}/rpc/Switch.GetStatus?id={id}";
                    var statusJson = await _httpClient.GetStringAsync(statusUrl, cancellationToken);
                    using var statusDoc = JsonDocument.Parse(statusJson);
                    
                    var name = $"Switch {id}";
                    if (statusDoc.RootElement.TryGetProperty("name", out var nameElem))
                    {
                        var nameStr = nameElem.GetString();
                        if (!string.IsNullOrWhiteSpace(nameStr))
                        {
                            name = nameStr;
                        }
                    }
                    
                    relays.Add((id, name));
                }
                catch
                {
                    // Switch doesn't exist, skip it
                }
            }
        }
        
        return relays;
    }

    public async Task<bool> GetSwitchOutputAsync(string ip, int switchId, CancellationToken cancellationToken)
    {
        var url = $"http://{ip}/rpc/Switch.GetStatus?id={switchId}";
        var json = await _httpClient.GetStringAsync(url, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("output", out var output))
        {
            return output.GetBoolean();
        }

        throw new InvalidOperationException("Switch.GetStatus response missing 'output'.");
    }

    public async Task SetSwitchAsync(string ip, int switchId, bool on, CancellationToken cancellationToken)
    {
        var onText = on ? "true" : "false";
        var url = $"http://{ip}/rpc/Switch.Set?id={switchId}&on={onText}";
        _ = await _httpClient.GetStringAsync(url, cancellationToken);
    }
}
