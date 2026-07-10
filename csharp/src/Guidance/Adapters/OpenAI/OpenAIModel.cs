using Guidance.Models;

namespace Guidance.Adapters.OpenAI;

/// <summary>
/// Convenience factory for creating a <see cref="Model"/> backed by
/// <see cref="OpenAIInterpreter"/>.
///
/// <b>OpenAI / Azure OpenAI:</b>
/// <code>
/// var lm = OpenAIModel.Create("gpt-4o", apiKey: "sk-...");
/// </code>
///
/// <b>OpenAI API-compatible server (e.g. Ollama, LM Studio, vLLM):</b>
/// <code>
/// var lm = OpenAIModel.Create("llama3", baseUrl: "http://localhost:11434/v1");
/// </code>
///
/// <code>
/// var result = lm
///     .WithSystem("You are a helpful assistant.")
///     .WithUser("What is the capital of France?")
///     .WithAssistant(m => m + GrammarFunctions.Gen("capital", maxTokens: 5));
///
/// Console.WriteLine(result["capital"]);
/// </code>
/// </summary>
public static class OpenAIModel
{
    /// <summary>
    /// Creates a <see cref="Model"/> that calls an OpenAI-compatible Chat Completions API.
    /// </summary>
    /// <param name="model">Model name, e.g. <c>"gpt-4o"</c> or <c>"llama3"</c>.</param>
    /// <param name="apiKey">
    /// API key for authentication.
    /// Pass <c>null</c> or omit for servers that do not require a key
    /// (e.g. Ollama, LM Studio, vLLM running locally).
    /// </param>
    /// <param name="baseUrl">
    /// Base URL of the Chat Completions endpoint.
    /// Defaults to <c>https://api.openai.com/v1</c>.
    /// Set this to target any OpenAI API-compatible endpoint,
    /// e.g. <c>http://localhost:11434/v1</c> for Ollama.
    /// </param>
    public static Model Create(
        string model,
        string? apiKey = null,
        string baseUrl = "https://api.openai.com/v1")
    {
        return Model.From(new OpenAIInterpreter(model, apiKey, baseUrl));
    }
}
