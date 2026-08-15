using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using PositionAnalysis.Mcp.Infrastructure.Config;

namespace PositionAnalysis.Mcp.Infrastructure.AiClients;

/// <summary>
/// AI completion result with token usage and estimated cost
/// </summary>
public record AiCompletionResult(
    string Content,
    int PromptTokens,
    int CompletionTokens,
    bool IsSuccess,
    decimal EstimatedCostUsd = 0m,
    string? ErrorMessage = null)
{
    public int TokensUsed => PromptTokens + CompletionTokens;
}

/// <summary>
/// Interface for AI/LLM completion services
/// </summary>
public interface IAiClient
{
    Task<AiCompletionResult> CompleteAsync(string prompt, string systemPrompt = "");
}

/// <summary>
/// Estimates LLM cost from token counts.
/// Loads pricing from llm-pricing.json next to the executable; falls back to built-in defaults if the file
/// is missing or malformed. Config overrides (CostPerInputTokenK / CostPerOutputTokenK) always take precedence.
/// All prices are per 1,000 tokens (USD).
/// </summary>
public static class LlmCostEstimator
{
    private const string PricingFileName = "llm-pricing.json";

    // Built-in fallback — ordered longest-key-first so more-specific models match before their prefixes.
    private static readonly (string Key, decimal InputPerK, decimal OutputPerK)[] BuiltInFallback =
    [
        ("gpt-5.1-codex-mini", 0.000250m, 0.002000m),
        ("gpt-5.1",            0.001250m, 0.010000m),
        ("codex-max",          0.001250m, 0.010000m),
        ("codex-mini",         0.000250m, 0.002000m),
        ("gpt-4o-mini",        0.000150m, 0.000600m),
        ("gpt-4o",             0.002500m, 0.010000m),
        ("gpt-4-turbo",        0.010000m, 0.030000m),
        ("gpt-4",              0.030000m, 0.060000m),
        ("gpt-35-turbo",       0.000500m, 0.001500m),
        ("gpt-3.5-turbo",      0.000500m, 0.001500m),
    ];

    private static readonly Lazy<(string Key, decimal InputPerK, decimal OutputPerK)[]> _table =
        new(LoadPricingTable, isThreadSafe: true);

    private static (string Key, decimal InputPerK, decimal OutputPerK)[] LoadPricingTable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, PricingFileName);
        if (!File.Exists(path))
            return BuiltInFallback;

        try
        {
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<PricingEntry[]>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entries is null || entries.Length == 0)
                return BuiltInFallback;

            // Sort longest-key-first so more-specific models match before their prefixes.
            return entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .OrderByDescending(e => e.Key.Length)
                .Select(e => (e.Key, (decimal)e.InputPerK, (decimal)e.OutputPerK))
                .ToArray();
        }
        catch
        {
            return BuiltInFallback;
        }
    }

    private sealed class PricingEntry
    {
        public string Key { get; set; } = string.Empty;
        public double InputPerK { get; set; }
        public double OutputPerK { get; set; }
    }

    /// <summary>
    /// Returns estimated cost in USD and a label describing which pricing source was used.
    /// Returns (0, "unknown") when no pricing is available.
    /// </summary>
    public static (decimal Cost, string Source) Estimate(
        string deploymentName,
        int promptTokens,
        int completionTokens,
        decimal configInputPerK = 0m,
        decimal configOutputPerK = 0m)
    {
        decimal inputPerK, outputPerK;
        string source;

        if (configInputPerK > 0 || configOutputPerK > 0)
        {
            inputPerK = configInputPerK;
            outputPerK = configOutputPerK;
            source = "config";
        }
        else
        {
            var table = _table.Value;
            var match = table.FirstOrDefault(
                e => deploymentName.Contains(e.Key, StringComparison.OrdinalIgnoreCase));

            if (match == default)
                return (0m, "unknown");

            inputPerK = match.InputPerK;
            outputPerK = match.OutputPerK;
            source = $"file:{match.Key}";
        }

        var cost = (promptTokens / 1000m) * inputPerK + (completionTokens / 1000m) * outputPerK;
        return (cost, source);
    }
}

/// <summary>
/// Ollama AI client implementation.
/// Connects to a local Ollama server — no API cost.
/// </summary>
public class OllamaAiClient : IAiClient
{
    private readonly string _endpoint;
    private readonly string _model;
    private readonly float _temperature;
    private readonly ILogger<OllamaAiClient> _logger;

    public OllamaAiClient(IOptions<AiSettings> aiOptions, ILogger<OllamaAiClient> logger)
    {
        var ollama = aiOptions.Value.Ollama;
        _endpoint = ollama.Endpoint;
        _model = ollama.Model;
        _temperature = (float)ollama.Temperature;
        _logger = logger;

        _logger.LogInformation("OllamaAiClient initialized at {Endpoint} using model {Model}",
            _endpoint, _model);
    }

    public async Task<AiCompletionResult> CompleteAsync(string prompt, string systemPrompt = "")
    {
        try
        {
            _logger.LogDebug("Sending completion request to Ollama");

            var client = new OllamaApiClient(new Uri(_endpoint));

            var fullPrompt = string.IsNullOrWhiteSpace(systemPrompt)
                ? prompt
                : $"{systemPrompt}\n\n{prompt}";

            var request = new OllamaSharp.Models.GenerateRequest
            {
                Model = _model,
                Prompt = fullPrompt,
                Stream = false
            };

            var responseBuilder = new StringBuilder();
            int promptTokens = 0;
            int completionTokens = 0;

            await foreach (var response in client.GenerateAsync(request))
            {
                if (response == null) continue;
                responseBuilder.Append(response.Response);
                if (response is OllamaSharp.Models.GenerateDoneResponseStream done)
                {
                    promptTokens = done.PromptEvalCount;
                    completionTokens = done.EvalCount;
                }
            }

            var fullResponse = responseBuilder.ToString();

            _logger.LogDebug("Received Ollama response: {ResponseLength} chars, {PromptTokens} prompt + {CompletionTokens} completion tokens (local — no cost)",
                fullResponse.Length, promptTokens, completionTokens);

            return new AiCompletionResult(
                Content: fullResponse,
                PromptTokens: promptTokens,
                CompletionTokens: completionTokens,
                IsSuccess: true,
                EstimatedCostUsd: 0m);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Ollama API");
            return new AiCompletionResult(
                Content: "",
                PromptTokens: 0,
                CompletionTokens: 0,
                IsSuccess: false,
                ErrorMessage: ex.Message);
        }
    }
}

/// <summary>
/// Azure OpenAI client implementation.
/// Connects to Azure OpenAI Service and captures token usage for cost estimation.
/// </summary>
public class AzureOpenAiClient : IAiClient
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _deploymentName;
    private readonly int _maxCompletionTokens;
    private readonly float _temperature;
    private readonly decimal _configInputPerK;
    private readonly decimal _configOutputPerK;
    private readonly ILogger<AzureOpenAiClient> _logger;

    public AzureOpenAiClient(IOptions<AiSettings> aiOptions, ILogger<AzureOpenAiClient> logger)
    {
        var azureSettings = aiOptions.Value.AzureOpenAI;
        _endpoint = azureSettings.Endpoint;
        _apiKey = azureSettings.ApiKey;
        _deploymentName = azureSettings.DeploymentName;
        _maxCompletionTokens = azureSettings.MaxCompletionTokens;
        _temperature = (float)azureSettings.Temperature;
        _configInputPerK = azureSettings.CostPerInputTokenK;
        _configOutputPerK = azureSettings.CostPerOutputTokenK;
        _logger = logger;

        _logger.LogInformation("AzureOpenAiClient initialized at {Endpoint} using deployment {Deployment}",
            _endpoint, _deploymentName);
    }

    public async Task<AiCompletionResult> CompleteAsync(string prompt, string systemPrompt = "")
    {
        try
        {
            _logger.LogDebug("Sending completion request to Azure OpenAI");

            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("api-key", _apiKey);

            var requestBody = new Dictionary<string, object>
            {
                ["messages"] = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = prompt }
                },
                ["max_completion_tokens"] = _maxCompletionTokens
            };
            // Some models (e.g. o-series, gpt-5) only accept the default temperature (1.0)
            // and reject the parameter outright if any value is supplied.
            // Only send it when explicitly overridden from the default.
            if (Math.Abs(_temperature - 1.0f) > 1e-6f)
                requestBody["temperature"] = _temperature;

            var url = $"{_endpoint}/openai/deployments/{_deploymentName}/chat/completions?api-version=2024-08-01-preview";
            var content = new System.Net.Http.StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestBody),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Azure OpenAI API error: {StatusCode} - {ResponseText}",
                    response.StatusCode, responseText);
                return new AiCompletionResult(
                    Content: "",
                    PromptTokens: 0,
                    CompletionTokens: 0,
                    IsSuccess: false,
                    ErrorMessage: $"HTTP {response.StatusCode}");
            }

            var jsonResponse = System.Text.Json.JsonDocument.Parse(responseText);
            var root = jsonResponse.RootElement;
            var message = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

            var promptTokens = 0;
            var completionTokens = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct)) completionTokens = ct.GetInt32();
            }

            var (estimatedCost, costSource) = LlmCostEstimator.Estimate(
                _deploymentName, promptTokens, completionTokens, _configInputPerK, _configOutputPerK);

            _logger.LogDebug("Azure OpenAI response: {ResponseLength} chars, {PromptTokens} prompt + {CompletionTokens} completion tokens, cost source: {CostSource}",
                message.Length, promptTokens, completionTokens, costSource);

            return new AiCompletionResult(
                Content: message,
                PromptTokens: promptTokens,
                CompletionTokens: completionTokens,
                IsSuccess: true,
                EstimatedCostUsd: estimatedCost);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Azure OpenAI API");
            return new AiCompletionResult(
                Content: "",
                PromptTokens: 0,
                CompletionTokens: 0,
                IsSuccess: false,
                ErrorMessage: ex.Message);
        }
    }
}
