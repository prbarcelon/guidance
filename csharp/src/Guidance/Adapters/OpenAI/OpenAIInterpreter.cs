using System.ClientModel;
using System.Text;
using OpenAI;
using OpenAI.Chat;
using Guidance.Grammar;
using Guidance.Models;

namespace Guidance.Adapters.OpenAI;

/// <summary>
/// An <see cref="IInterpreter"/> that calls the OpenAI Chat Completions API
/// using the official <c>OpenAI</c> .NET SDK to execute grammar rules.
///
/// Supports OpenAI, Azure OpenAI, and any OpenAI API-compatible server
/// (e.g. Ollama, LM Studio, vLLM) via the <paramref name="baseUrl"/> parameter.
///
/// State management:
/// <list type="bullet">
///   <item><term>System / user messages</term><description>
///     Content accumulated inside <c>WithSystem</c> / <c>WithUser</c> blocks is
///     stored as completed chat messages.
///   </description></item>
///   <item><term>Assistant block</term><description>
///     Static text appended before <c>Gen</c> / <c>Select</c> becomes a prefix
///     that is sent as a partial assistant message.  The API call fills in the rest.
///   </description></item>
///   <item><term>Captures</term><description>
///     Generated text is stored by the name supplied to <c>RuleNode.Capture</c>.
///   </description></item>
/// </list>
///
/// <b>Note on blocking:</b> Completions are requested synchronously via the SDK's
/// <c>CompleteChat</c> method.  This mirrors the Python library's synchronous
/// behaviour.  A future <c>IAsyncInterpreter</c> interface can replace this.
///
/// <b>Note on ChatClient lifecycle:</b> A single <see cref="ChatClient"/> instance
/// is created per public constructor call and is then <em>shared</em> by all clones
/// of that interpreter (see the private copy constructor).  Do not create a new
/// <see cref="OpenAIInterpreter"/> per request; instead, create it once via
/// <see cref="OpenAIModel.Create"/> and reuse the resulting <see cref="Model"/>.
///
/// Corresponds to <c>BaseOpenAIInterpreter</c> in
/// <c>guidance/models/_openai_base.py</c>.
/// </summary>
public sealed class OpenAIInterpreter : IInterpreter
{
    // -----------------------------------------------------------------------
    // The ChatClient is shared between an interpreter and all of its clones.
    // ChatClient is thread-safe; sharing it avoids the overhead of re-creating
    // the underlying HTTP pipeline for every Model branch.
    // -----------------------------------------------------------------------
    private readonly ChatClient _chatClient;

    // Accumulated chat history (completed messages).
    private readonly List<ChatMessage> _messages;

    // Content being built up within the currently open role block.
    private readonly StringBuilder _currentContent;

    // Overall text representation (ChatML-style, for compatibility with Model.Text).
    private readonly StringBuilder _text;

    // Captured variables.
    private readonly Dictionary<string, CaptureValue> _captures;

    private string? _activeRole;

    /// <summary>
    /// Initialises a new <see cref="OpenAIInterpreter"/>.
    /// </summary>
    /// <param name="model">Model name, e.g. <c>"gpt-4o"</c>.</param>
    /// <param name="apiKey">
    /// API key for authentication.
    /// Pass <c>null</c> or an empty string for OpenAI API-compatible servers
    /// that do not require a key (e.g. Ollama, LM Studio, vLLM).
    /// </param>
    /// <param name="baseUrl">
    /// Base URL of the Chat Completions endpoint.
    /// Defaults to <c>https://api.openai.com/v1</c>.
    /// Set this to your server's base URL to target any OpenAI API-compatible
    /// endpoint (e.g. <c>http://localhost:11434/v1</c> for Ollama).
    /// </param>
    public OpenAIInterpreter(
        string model,
        string? apiKey = null,
        string baseUrl = "https://api.openai.com/v1")
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(baseUrl.TrimEnd('/'))
        };

        // Use a placeholder for servers that do not require authentication.
        var credential = new ApiKeyCredential(
            string.IsNullOrWhiteSpace(apiKey) ? "no-key" : apiKey);

        _chatClient = new ChatClient(model, credential, options);

        _messages = [];
        _currentContent = new StringBuilder();
        _text = new StringBuilder();
        _captures = [];
    }

    // Copy constructor used by Clone().
    // ChatClient is intentionally shared (it is thread-safe and reuse is recommended).
    private OpenAIInterpreter(
        ChatClient chatClient,
        List<ChatMessage> messages,
        string currentContent,
        string text,
        Dictionary<string, CaptureValue> captures,
        string? activeRole)
    {
        _chatClient = chatClient;
        _messages = new List<ChatMessage>(messages);
        _currentContent = new StringBuilder(currentContent);
        _text = new StringBuilder(text);
        _captures = new Dictionary<string, CaptureValue>(captures);
        _activeRole = activeRole;
    }

    // -----------------------------------------------------------------------
    // IInterpreter
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public string CurrentText => _text.ToString();

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, CaptureValue> Captures => _captures;

    /// <inheritdoc/>
    public string? ActiveRole => _activeRole;

    /// <inheritdoc/>
    public IInterpreter Clone() => new OpenAIInterpreter(
        _chatClient,
        _messages,
        _currentContent.ToString(),
        _text.ToString(),
        _captures,
        _activeRole);

    /// <inheritdoc/>
    public void AppendLiteral(string text)
    {
        _currentContent.Append(text);
        _text.Append(text);
    }

    /// <inheritdoc/>
    public void ApplyRule(RuleNode rule)
    {
        if (_activeRole != "assistant")
            throw new InvalidOperationException(
                "OpenAI models can only generate inside an assistant role block " +
                "(use model.WithAssistant(...)).");

        string generated = rule.Value switch
        {
            SelectNode select => ExecuteSelect(select, rule),
            JsonSchemaNode json => ExecuteJson(json, rule),
            _ => ExecuteGen(rule),
        };

        _currentContent.Append(generated);
        _text.Append(generated);

        if (rule.Capture is not null)
            StoreCapture(rule.Capture, generated, rule.ListAppend);

        if (rule.Suffix is not null)
        {
            _currentContent.Append(rule.Suffix.Value);
            _text.Append(rule.Suffix.Value);
        }
    }

    /// <inheritdoc/>
    public void StartRole(string role)
    {
        if (_activeRole is not null)
            throw new InvalidOperationException(
                $"Cannot open role '{role}': role '{_activeRole}' is already open.");

        _activeRole = role;
        _text.Append(GetRoleStart(role));
    }

    /// <inheritdoc/>
    public void EndRole(string role)
    {
        if (_activeRole is null)
            throw new InvalidOperationException("Cannot close role: no role is currently open.");
        if (_activeRole != role)
            throw new InvalidOperationException(
                $"Cannot close role '{role}': role '{_activeRole}' is open.");

        // Commit the accumulated content as a completed message.
        var content = _currentContent.ToString();
        if (content.Length > 0 || role == "assistant")
            _messages.Add(BuildChatMessage(role, content));

        _currentContent.Clear();
        _text.Append(GetRoleEnd(role));
        _activeRole = null;
    }

    // -----------------------------------------------------------------------
    // Generation helpers
    // -----------------------------------------------------------------------

    private string ExecuteGen(RuleNode rule)
    {
        var stopSeqs = rule.Stop switch
        {
            LiteralNode lit => new[] { lit.Value },
            RegexNode => null, // stop_regex not directly supported by the Chat Completions API
            _ => null,
        };

        return CallCompletion(
            maxTokens: rule.MaxTokens ?? 256,
            temperature: rule.Temperature,
            stopSequences: stopSeqs,
            responseFormat: null);
    }

    private string ExecuteSelect(SelectNode select, RuleNode rule)
    {
        // Collect literal options.
        var options = select.Options
            .OfType<LiteralNode>()
            .Select(l => l.Value)
            .ToList();

        if (options.Count == 0)
            throw new InvalidOperationException(
                "SelectNode must contain at least one LiteralNode option for the OpenAI adapter.");

        // Use max option length as the token budget.
        var maxLen = options.Max(o => (int)Math.Ceiling(o.Length / 4.0));

        var raw = CallCompletion(
            maxTokens: Math.Max(maxLen, 1),
            temperature: rule.Temperature,
            stopSequences: null,
            responseFormat: null);

        // Pick the option that the response best matches (longest prefix match).
        return options
            .OrderByDescending(o => o.Length)
            .FirstOrDefault(o => raw.StartsWith(o, StringComparison.OrdinalIgnoreCase))
            ?? options[0];
    }

    private string ExecuteJson(JsonSchemaNode json, RuleNode rule)
    {
        var responseFormat = json.SchemaJson is not null
            ? ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: rule.Name,
                jsonSchema: BinaryData.FromString(json.SchemaJson),
                jsonSchemaIsStrict: true)
            : ChatResponseFormat.CreateJsonObjectFormat();

        return CallCompletion(
            maxTokens: rule.MaxTokens ?? 1024,
            temperature: rule.Temperature,
            stopSequences: null,
            responseFormat: responseFormat);
    }

    // -----------------------------------------------------------------------
    // SDK call
    // -----------------------------------------------------------------------

    private string CallCompletion(
        int? maxTokens,
        float? temperature,
        string[]? stopSequences,
        ChatResponseFormat? responseFormat)
    {
        // Build the messages list, including a partial assistant turn if we have
        // any assistant prefix content.
        var messages = new List<ChatMessage>(_messages);
        var assistantPrefix = _currentContent.ToString();
        if (_activeRole == "assistant" && assistantPrefix.Length > 0)
        {
            // Some providers support a prefill/prefix by passing a partial
            // assistant message.  OpenAI itself does not support this officially
            // but Anthropic and others do.  We include it here for completeness;
            // it will simply be ignored by providers that don't support it.
            messages.Add(new AssistantChatMessage(assistantPrefix));
        }

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxTokens,
            Temperature = temperature,
            ResponseFormat = responseFormat,
        };
        if (stopSequences is not null)
            foreach (var s in stopSequences)
                chatOptions.StopSequences.Add(s);

        var result = _chatClient.CompleteChat(messages, chatOptions);
        return string.Concat(result.Value.Content.Select(p => p.Text));
    }

    // -----------------------------------------------------------------------
    // ChatML helpers
    // -----------------------------------------------------------------------
    // The text representation uses ChatML format (<|im_start|>role … <|im_end|>)
    // because that is what most OpenAI-compatible APIs use internally.
    // MockInterpreter uses a simpler <|role|> … <|/role|> format that is easier
    // to assert against in unit tests.  Both formats are purely cosmetic: the
    // actual wire representation sent to the API is always a list of SDK messages.
    // -----------------------------------------------------------------------

    private static string GetRoleStart(string role) => $"<|im_start|>{role}\n";
    private static string GetRoleEnd(string role) => "\n<|im_end|>\n";

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates the appropriate SDK <see cref="ChatMessage"/> subtype for the given role.
    /// </summary>
    private static ChatMessage BuildChatMessage(string role, string content) => role switch
    {
        "system" => new SystemChatMessage(content),
        "user" => new UserChatMessage(content),
        "assistant" => new AssistantChatMessage(content),
        _ => new UserChatMessage(content), // fallback for custom roles
    };

    private void StoreCapture(string name, string value, bool listAppend)
    {
        if (listAppend && _captures.TryGetValue(name, out var existing))
            _captures[name] = existing.Append(value);
        else
            _captures[name] = new CaptureValue(value);
    }
}
