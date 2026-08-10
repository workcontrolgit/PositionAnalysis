using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using SchedulePCMcp.Infrastructure.Config;

namespace SchedulePCMcp.Infrastructure.AiClients;

/// <summary>
/// AI completion result
/// </summary>
public record AiCompletionResult(string Content, int TokensUsed, bool IsSuccess, string? ErrorMessage = null);

/// <summary>
/// Interface for AI/LLM completion services
/// </summary>
public interface IAiClient
{
    Task<AiCompletionResult> CompleteAsync(string prompt, string systemPrompt = "");
}

/// <summary>
/// Ollama AI client implementation
/// Connects to local Ollama server for model inference
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
            
            // Use GenerateAsync with the proper API - it returns IAsyncEnumerable for streaming
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
            
            // Collect all streaming responses
            await foreach (var response in client.GenerateAsync(request))
            {
                if (response != null)
                    responseBuilder.Append(response.Response);
            }

            var fullResponse = responseBuilder.ToString();
            _logger.LogDebug("Received completion response: {ResponseLength} chars", fullResponse.Length);

            return new AiCompletionResult(
                Content: fullResponse,
                TokensUsed: 0,
                IsSuccess: true
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Ollama API");
            return new AiCompletionResult(
                Content: "",
                TokensUsed: 0,
                IsSuccess: false,
                ErrorMessage: ex.Message
            );
        }
    }
}

/// <summary>
/// Azure OpenAI client implementation
/// Connects to Azure OpenAI Service for model inference
/// </summary>
public class AzureOpenAiClient : IAiClient
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _deploymentName;
    private readonly float _temperature;
    private readonly ILogger<AzureOpenAiClient> _logger;

    public AzureOpenAiClient(IOptions<AiSettings> aiOptions, ILogger<AzureOpenAiClient> logger)
    {
        var azureSettings = aiOptions.Value.AzureOpenAI;
        _endpoint = azureSettings.Endpoint;
        _apiKey = azureSettings.ApiKey;
        _deploymentName = azureSettings.DeploymentName;
        _temperature = (float)azureSettings.Temperature;
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

            var requestBody = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = prompt }
                },
                max_completion_tokens = 16384,
                temperature = _temperature
            };

            var url = $"{_endpoint}/openai/deployments/{_deploymentName}/chat/completions?api-version=2024-08-01-preview";
            var content = new System.Net.Http.StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestBody),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await client.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Azure OpenAI API error: {StatusCode} - {ResponseText}", 
                    response.StatusCode, responseText);
                return new AiCompletionResult(
                    Content: "",
                    TokensUsed: 0,
                    IsSuccess: false,
                    ErrorMessage: $"HTTP {response.StatusCode}"
                );
            }

            var jsonResponse = System.Text.Json.JsonDocument.Parse(responseText);
            var root = jsonResponse.RootElement;
            var message = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

            _logger.LogDebug("Received completion response: {ResponseLength} chars", message.Length);

            return new AiCompletionResult(
                Content: message,
                TokensUsed: 0,
                IsSuccess: true
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Azure OpenAI API");
            return new AiCompletionResult(
                Content: "",
                TokensUsed: 0,
                IsSuccess: false,
                ErrorMessage: ex.Message
            );
        }
    }
}
