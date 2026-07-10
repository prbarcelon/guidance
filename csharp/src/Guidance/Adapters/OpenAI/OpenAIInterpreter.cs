using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Guidance.Grammar;
using Guidance.Models;

namespace Guidance.Adapters.OpenAI;

// ---------------------------------------------------------------------------
// OpenAI wire types
// ---------------------------------------------------------------------------

internal record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal record ChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("temperature")] float? Temperature = null,
    [property: JsonPropertyName("stop")] string[]? Stop = null,
    [property: JsonPropertyName("response_format")] object? ResponseFormat = null);

internal record ChatChoice(
    [property: JsonPropertyName("message")] ChatMessage Message);

internal record ChatCompletionResponse(
    [property: JsonPropertyName("choices")] List<ChatChoice> Choices);

// ---------------------------------------------------------------------------
// Interpreter
// ---------------------------------------------------------------------------

/// <summary>
/// An <see cref="IInterpreter"/> that calls the OpenAI Chat Completions API
/// (<c>/v1/chat/completions</c>) to execute grammar rules.
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
/// <b>Note on blocking:</b> HTTP calls are made synchronously via
/// <c>GetAwaiter().GetResult()</c>.  This mirrors the Python library's
/// synchronous behaviour.  A future <c>IAsyncInterpreter</c> interface can
/// replace this.
///
/// <b>Note on HttpClient lifecycle:</b> A single <see cref="HttpClient"/> instance
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
    // Static fields first, then instance fields.

    // JSON serialiser options — shared across all instances.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // -----------------------------------------------------------------------
    // The HttpClient is shared between an interpreter and all of its clones.
    // This avoids socket exhaustion: all Model branches that descend from the
    // same OpenAIModel.Create() call reuse a single socket pool.
    // -----------------------------------------------------------------------
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _baseUrl;

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
    /// <param name="apiKey">OpenAI API key (or Azure OpenAI key).</param>
    /// <param name="model">Model name, e.g. <c>"gpt-4o"</c>.</param>
    /// <param name="baseUrl">
    /// Base URL of the Chat Completions endpoint.
    /// Defaults to <c>https://api.openai.com/v1</c>.
    /// </param>
    public OpenAIInterpreter(
        string apiKey,
        string model,
        string baseUrl = "https://api.openai.com/v1")
    {
        _model = model;
        _baseUrl = baseUrl.TrimEnd('/');

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);

        _messages = new List<ChatMessage>();
        _currentContent = new StringBuilder();
        _text = new StringBuilder();
        _captures = new Dictionary<string, CaptureValue>();
    }

    // Copy constructor used by Clone().
    // The HttpClient is intentionally shared (it is thread-safe and reuse is recommended).
    private OpenAIInterpreter(
        HttpClient httpClient,
        string model,
        string baseUrl,
        List<ChatMessage> messages,
        string currentContent,
        string text,
        Dictionary<string, CaptureValue> captures,
        string? activeRole)
    {
        _httpClient = httpClient;
        _model = model;
        _baseUrl = baseUrl;
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
        _httpClient,
        _model,
        _baseUrl,
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
            _messages.Add(new ChatMessage(role, content));

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
            RegexNode => null, // stop_regex not directly supported by OpenAI API
            _ => null,
        };

        return CallCompletion(
            maxTokens: rule.MaxTokens ?? 256,
            temperature: rule.Temperature,
            stop: stopSeqs,
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
            stop: null,
            responseFormat: null);

        // Pick the option that the response best matches (longest prefix match).
        var best = options
            .OrderByDescending(o => o.Length)
            .FirstOrDefault(o => raw.StartsWith(o, StringComparison.OrdinalIgnoreCase))
            ?? options[0];

        return best;
    }

    private string ExecuteJson(JsonSchemaNode json, RuleNode rule)
    {
        object responseFormat;

        if (json.SchemaJson is not null)
        {
            var schema = JsonDocument.Parse(json.SchemaJson).RootElement;
            responseFormat = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = rule.Name,
                    strict = true,
                    schema = schema,
                },
            };
        }
        else
        {
            responseFormat = new { type = "json_object" };
        }

        return CallCompletion(
            maxTokens: rule.MaxTokens ?? 1024,
            temperature: rule.Temperature,
            stop: null,
            responseFormat: responseFormat);
    }

    // -----------------------------------------------------------------------
    // HTTP
    // -----------------------------------------------------------------------

    private string CallCompletion(
        int? maxTokens,
        float? temperature,
        string[]? stop,
        object? responseFormat)
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
            messages.Add(new ChatMessage("assistant", assistantPrefix));
        }

        var request = new ChatRequest(
            Model: _model,
            Messages: messages,
            MaxTokens: maxTokens,
            Temperature: temperature,
            Stop: stop,
            ResponseFormat: responseFormat);

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Synchronous HTTP call (mirrors Python's blocking behaviour).
        var httpResponse = _httpClient
            .PostAsync($"{_baseUrl}/chat/completions", jsonContent)
            .GetAwaiter()
            .GetResult();

        httpResponse.EnsureSuccessStatusCode();

        var responseBody = httpResponse.Content
            .ReadAsStringAsync()
            .GetAwaiter()
            .GetResult();

        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody)
            ?? throw new InvalidOperationException("OpenAI returned an empty response.");

        if (parsed.Choices.Count == 0)
            throw new InvalidOperationException("OpenAI returned no choices.");

        return parsed.Choices[0].Message.Content;
    }

    // -----------------------------------------------------------------------
    // ChatML helpers
    // -----------------------------------------------------------------------
    // The text representation uses ChatML format (<|im_start|>role … <|im_end|>)
    // because that is what most OpenAI-compatible APIs use internally.
    // MockInterpreter uses a simpler <|role|> … <|/role|> format that is easier
    // to assert against in unit tests.  Both formats are purely cosmetic: the
    // actual wire representation sent to the API is always a list of JSON messages.
    // -----------------------------------------------------------------------

    private static string GetRoleStart(string role) => $"<|im_start|>{role}\n";
    private static string GetRoleEnd(string role) => "\n<|im_end|>\n";

    // -----------------------------------------------------------------------
    // Capture storage
    // -----------------------------------------------------------------------

    private void StoreCapture(string name, string value, bool listAppend)
    {
        if (listAppend && _captures.TryGetValue(name, out var existing))
            _captures[name] = existing.Append(value);
        else
            _captures[name] = new CaptureValue(value);
    }
}
