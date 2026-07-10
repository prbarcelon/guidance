using Guidance.Models;

namespace Guidance.Adapters.OpenAI;

/// <summary>
/// Convenience factory for creating a <see cref="Model"/> backed by
/// <see cref="OpenAIInterpreter"/>.
///
/// <code>
/// var lm = OpenAIModel.Create("gpt-4o", apiKey: "sk-...");
///
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
    /// Creates a <see cref="Model"/> that calls the OpenAI Chat Completions API.
    /// </summary>
    /// <param name="model">Model name, e.g. <c>"gpt-4o"</c> or <c>"gpt-3.5-turbo"</c>.</param>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="baseUrl">
    /// Optional override for the API base URL.  Useful for Azure OpenAI or compatible
    /// endpoints.  Defaults to <c>https://api.openai.com/v1</c>.
    /// </param>
    public static Model Create(
        string model,
        string apiKey,
        string baseUrl = "https://api.openai.com/v1")
    {
        return Model.From(new OpenAIInterpreter(apiKey, model, baseUrl));
    }
}
