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
