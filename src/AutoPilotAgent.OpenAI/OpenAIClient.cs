using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AutoPilotAgent.OpenAI;

public sealed class OpenAIClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIOptions _options;
    private readonly ILogger<OpenAIClient> _logger;

    public OpenAIClient(HttpClient httpClient, OpenAIOptions options, ILogger<OpenAIClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<T> CreateResponseStructuredAsync<T>(string apiKey, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);

        var delay = TimeSpan.FromMilliseconds(250);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = CreateRequest(apiKey, json);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    return DeserializeStructured<T>(body);
                }

                if (IsRetryable(response.StatusCode) && attempt < 5)
                {
                    _logger.LogWarning("OpenAI call failed with {Status}; retrying attempt {Attempt}/5", response.StatusCode, attempt);
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    continue;
                }

                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"OpenAI call failed: {(int)response.StatusCode} {response.StatusCode}. Body: {error}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 5)
            {
                _logger.LogWarning("OpenAI call timed out; retrying attempt {Attempt}/5", attempt);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
        }

        throw new InvalidOperationException("OpenAI call failed after retries.");
    }

    public async Task<string> CreateResponseTextAsync(string apiKey, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);

        var delay = TimeSpan.FromMilliseconds(250);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = CreateRequest(apiKey, json);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    return ExtractOutputText(body);
                }

                if (IsRetryable(response.StatusCode) && attempt < 5)
                {
                    _logger.LogWarning("OpenAI call failed with {Status}; retrying attempt {Attempt}/5", response.StatusCode, attempt);
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    continue;
                }

                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"OpenAI call failed: {(int)response.StatusCode} {response.StatusCode}. Body: {error}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 5)
            {
                _logger.LogWarning("OpenAI call timed out; retrying attempt {Attempt}/5", attempt);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
        }

        throw new InvalidOperationException("OpenAI call failed after retries.");
    }

    private HttpRequestMessage CreateRequest(string apiKey, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static bool IsRetryable(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static T DeserializeStructured<T>(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return JsonSerializer.Deserialize<T>(outputText.GetString()!)!;
        }

        if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var c in content.EnumerateArray())
                {
                    if (c.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        var s = text.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            return JsonSerializer.Deserialize<T>(s!)!;
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("Could not locate structured JSON in OpenAI response.");
    }

    private static string ExtractOutputText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var c in content.EnumerateArray())
                {
                    if (c.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        var s = text.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            return s;
                        }
                    }
                }
            }
        }

        return string.Empty;
    }
}
