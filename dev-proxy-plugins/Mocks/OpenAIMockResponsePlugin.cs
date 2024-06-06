// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Titanium.Web.Proxy.Models;

public class OpenAIMockResponsePlugin : BaseProxyPlugin
{
    public OpenAIMockResponsePlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override string Name => nameof(OpenAIMockResponsePlugin);

    public override void Register()
    {
        base.Register();

        using var scope = Logger.BeginScope(Name);

        Logger.LogInformation("Checking language model availability...");
        if (!Context.LanguageModelClient.IsEnabled().Result)
        {
            Logger.LogError("Local language model is not enabled. The {plugin} will not be used.", Name);
            return;
        }

        PluginEvents.BeforeRequest += OnRequest;
    }

    private async Task OnRequest(object sender, ProxyRequestArgs e)
    {
        using var scope = Logger.BeginScope(Name);

        var request = e.Session.HttpClient.Request;
        if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
            !request.HasBody)
        {
            return;
        }

        if (!TryGetOpenAIRequest(request.BodyString, out var openAiRequest))
        {
            return;
        }

        if (openAiRequest is OpenAICompletionRequest completionRequest)
        {
            var ollamaResponse = (await Context.LanguageModelClient.GenerateCompletion(completionRequest.Prompt))
                as OllamaLanguageModelCompletionResponse;
            if (ollamaResponse is null)
            {
                return;
            }
            if (ollamaResponse.Error is not null)
            {
                Logger.LogError("Error from Ollama language model: {error}", ollamaResponse.Error);
                return;
            }

            var openAiResponse = ollamaResponse.ConvertToOpenAIResponse();
            SendMockResponse<OpenAICompletionResponse>(openAiResponse, ollamaResponse.RequestUrl, e);
        }
        else if (openAiRequest is OpenAIChatCompletionRequest chatRequest)
        {
            var ollamaResponse = (await Context.LanguageModelClient
                .GenerateChatCompletion(chatRequest.Messages.ConvertToLanguageModelChatCompletionMessage()))
                as OllamaLanguageModelChatCompletionResponse;
            if (ollamaResponse is null)
            {
                return;
            }
            if (ollamaResponse.Error is not null)
            {
                Logger.LogError("Error from Ollama language model: {error}", ollamaResponse.Error);
                return;
            }

            var openAiResponse = ollamaResponse.ConvertToOpenAIResponse();
            SendMockResponse<OpenAIChatCompletionResponse>(openAiResponse, ollamaResponse.RequestUrl, e);
        }
        else
        {
            Logger.LogError("Unknown OpenAI request type.");
        }
    }

    private bool TryGetOpenAIRequest(string content, out OpenAIRequest? request)
    {
        request = null;

        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        try
        {
            Logger.LogDebug("Checking if the request is an OpenAI request...");

            var rawRequest = JsonSerializer.Deserialize<JsonElement>(content, ProxyUtils.JsonSerializerOptions);

            if (rawRequest.TryGetProperty("prompt", out _))
            {
                Logger.LogDebug("Request is a completion request");
                request = JsonSerializer.Deserialize<OpenAICompletionRequest>(content, ProxyUtils.JsonSerializerOptions);
                return true;
            }

            if (rawRequest.TryGetProperty("messages", out _))
            {
                Logger.LogDebug("Request is a chat completion request");
                request = JsonSerializer.Deserialize<OpenAIChatCompletionRequest>(content, ProxyUtils.JsonSerializerOptions);
                return true;
            }

            Logger.LogDebug("Request is not an OpenAI request.");
            return false;
        }
        catch (JsonException ex)
        {
            Logger.LogDebug(ex, "Failed to deserialize OpenAI request.");
            return false;
        }
    }

    private void SendMockResponse<TResponse>(OpenAIResponse response, string localLmUrl, ProxyRequestArgs e) where TResponse : OpenAIResponse
    {
        e.Session.GenericResponse(
            // we need this cast or else the JsonSerializer drops derived properties
            JsonSerializer.Serialize((TResponse)response, ProxyUtils.JsonSerializerOptions),
            HttpStatusCode.OK,
            [
                new HttpHeader("content-type", "application/json"),
                new HttpHeader("access-control-allow-origin", "*")
            ]
        );
        e.ResponseState.HasBeenSet = true;
        Logger.LogRequest([$"200 {localLmUrl}"], MessageType.Mocked, new LoggingContext(e.Session));
    }
}

#region models

internal abstract class OpenAIRequest
{
    [JsonPropertyName("frequency_penalty")]
    public long FrequencyPenalty { get; set; }
    [JsonPropertyName("max_tokens")]
    public long MaxTokens { get; set; }
    [JsonPropertyName("presence_penalty")]
    public long PresencePenalty { get; set; }
    public object? Stop { get; set; }
    public bool Stream { get; set; }
    public long Temperature { get; set; }
    [JsonPropertyName("top_p")]
    public double TopP { get; set; }
}

internal abstract class OpenAIResponse
{
    public long Created { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Object { get; set; } = "text_completion";
    [JsonPropertyName("prompt_filter_results")]
    public OpenAIResponsePromptFilterResult[] PromptFilterResults { get; set; } = [];
    public OpenAIResponseUsage Usage { get; set; } = new();
}

internal abstract class OpenAIResponse<TChoice> : OpenAIResponse
{
    public TChoice[] Choices { get; set; } = [];
}

internal class OpenAIResponseUsage
{
    [JsonPropertyName("completion_tokens")]
    public long CompletionTokens { get; set; }
    [JsonPropertyName("prompt_tokens")]
    public long PromptTokens { get; set; }
    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }
}

internal abstract class OpenAIResponseChoice
{
    [JsonPropertyName("content_filter_results")]
    public Dictionary<string, OpenAIResponseContentFilterResult> ContentFilterResults { get; set; } = new();
    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; } = "length";
    public long Index { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? Logprobs { get; set; }
}

internal class OpenAIResponsePromptFilterResult
{
    [JsonPropertyName("content_filter_results")]
    public Dictionary<string, OpenAIResponseContentFilterResult> ContentFilterResults { get; set; } = new();
    [JsonPropertyName("prompt_index")]
    public long PromptIndex { get; set; }
}

internal class OpenAIResponseContentFilterResult
{
    public bool Filtered { get; set; }
    public string Severity { get; set; } = "safe";
}

internal class OpenAICompletionRequest : OpenAIRequest
{
    public string Prompt { get; set; } = string.Empty;
}

internal class OpenAICompletionResponse : OpenAIResponse<OpenAICompletionResponseChoice>
{
}

internal class OpenAICompletionResponseChoice : OpenAIResponseChoice
{
    public string Text { get; set; } = string.Empty;
}

internal class OpenAIChatCompletionRequest : OpenAIRequest
{
    public OpenAIChatMessage[] Messages { get; set; } = [];
}

internal class OpenAIChatMessage
{
    public string Content { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

internal class OpenAIChatCompletionResponse : OpenAIResponse<OpenAIChatCompletionResponseChoice>
{
}

internal class OpenAIChatCompletionResponseChoice : OpenAIResponseChoice
{
    public OpenAIChatCompletionResponseChoiceMessage Message { get; set; } = new();
}

internal class OpenAIChatCompletionResponseChoiceMessage
{
    public string Content { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

#endregion

#region extensions

internal static class OllamaLanguageModelCompletionResponseExtensions
{
    public static OpenAICompletionResponse ConvertToOpenAIResponse(this OllamaLanguageModelCompletionResponse response)
    {
        return new OpenAICompletionResponse
        {
            Id = Guid.NewGuid().ToString(),
            Object = "text_completion",
            Created = ((DateTimeOffset)response.CreatedAt).ToUnixTimeSeconds(),
            Model = response.Model,
            PromptFilterResults =
            [
                new OpenAIResponsePromptFilterResult
                {
                    PromptIndex = 0,
                    ContentFilterResults = new Dictionary<string, OpenAIResponseContentFilterResult>
                    {
                        { "hate", new() { Filtered = false, Severity = "safe" } },
                        { "self_harm", new() { Filtered = false, Severity = "safe" } },
                        { "sexual", new() { Filtered = false, Severity = "safe" } },
                        { "violence", new() { Filtered = false, Severity = "safe" } }
                    }
                }
            ],
            Choices =
            [
                new OpenAICompletionResponseChoice
                {
                    Text = response.Response ?? string.Empty,
                    Index = 0,
                    FinishReason = "length",
                    ContentFilterResults = new Dictionary<string, OpenAIResponseContentFilterResult>
                    {
                        { "hate", new() { Filtered = false, Severity = "safe" } },
                        { "self_harm", new() { Filtered = false, Severity = "safe" } },
                        { "sexual", new() { Filtered = false, Severity = "safe" } },
                        { "violence", new() { Filtered = false, Severity = "safe" } }
                    }
                }
            ],
            Usage = new OpenAIResponseUsage
            {
                PromptTokens = response.PromptEvalCount,
                CompletionTokens = response.EvalCount,
                TotalTokens = response.PromptEvalCount + response.EvalCount
            }
        };
    }
}

internal static class OllamaLanguageModelChatCompletionResponseExtensions
{
    public static OpenAIChatCompletionResponse ConvertToOpenAIResponse(this OllamaLanguageModelChatCompletionResponse response)
    {
        return new OpenAIChatCompletionResponse
        {
            Choices = [new OpenAIChatCompletionResponseChoice
            {
                ContentFilterResults = new Dictionary<string, OpenAIResponseContentFilterResult>
                {
                    { "hate", new() { Filtered = false, Severity = "safe" } },
                    { "self_harm", new() { Filtered = false, Severity = "safe" } },
                    { "sexual", new() { Filtered = false, Severity = "safe" } },
                    { "violence", new() { Filtered = false, Severity = "safe" } }
                },
                FinishReason = "stop",
                Index = 0,
                Message = new()
                {
                    Content = response.Message.Content,
                    Role = response.Message.Role
                }
            }],
            Created = ((DateTimeOffset)response.CreatedAt).ToUnixTimeSeconds(),
            Id = Guid.NewGuid().ToString(),
            Model = response.Model,
            Object = "chat.completion",
            PromptFilterResults =
            [
                new OpenAIResponsePromptFilterResult
                {
                    PromptIndex = 0,
                    ContentFilterResults = new Dictionary<string, OpenAIResponseContentFilterResult>
                    {
                        { "hate", new() { Filtered = false, Severity = "safe" } },
                        { "self_harm", new() { Filtered = false, Severity = "safe" } },
                        { "sexual", new() { Filtered = false, Severity = "safe" } },
                        { "violence", new() { Filtered = false, Severity = "safe" } }
                    }
                }
            ],
            Usage = new OpenAIResponseUsage
            {
                PromptTokens = response.PromptEvalCount,
                CompletionTokens = response.EvalCount,
                TotalTokens = response.PromptEvalCount + response.EvalCount
            }
        };
    }
}

internal static class OpenAIChatMessageExtensions
{
    public static ILanguageModelChatCompletionMessage[] ConvertToLanguageModelChatCompletionMessage(this OpenAIChatMessage[] messages)
    {
        return messages.Select(m => new OllamaLanguageModelChatCompletionMessage
        {
            Content = m.Content,
            Role = m.Role
        }).ToArray();
    }
}

#endregion