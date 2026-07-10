using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Schema;

namespace Guidance.Grammar;

/// <summary>
/// Factory methods for building grammar nodes.
///
/// These correspond to the Python guidance library's top-level functions
/// (<c>gen()</c>, <c>select()</c>, <c>json()</c>, etc.) defined in
/// <c>guidance/_grammar.py</c> and <c>guidance/library/</c>.
/// </summary>
public static class GrammarFunctions
{
    // -----------------------------------------------------------------------
    // gen()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="RuleNode"/> that instructs the model to generate text,
    /// optionally constrained by a regular expression and/or stop conditions.
    ///
    /// Corresponds to <c>gen()</c> in <c>guidance/library/_gen.py</c>.
    /// </summary>
    /// <param name="name">
    /// Optional capture name.  Generated text is stored as <c>model[name]</c>.
    /// </param>
    /// <param name="regex">
    /// Optional regular expression that constrains the generation.
    /// </param>
    /// <param name="stop">
    /// String at which generation stops (mutually exclusive with <paramref name="stopRegex"/>).
    /// </param>
    /// <param name="stopRegex">
    /// Regular expression at which generation stops (mutually exclusive with <paramref name="stop"/>).
    /// </param>
    /// <param name="suffix">Literal appended after generation (not captured).</param>
    /// <param name="temperature">Sampling temperature.</param>
    /// <param name="maxTokens">Maximum tokens to generate.</param>
    /// <param name="listAppend">
    /// When <c>true</c> the capture is appended to a list instead of overwriting.
    /// </param>
    /// <param name="saveStopText">
    /// When <c>true</c> the matched stop text is stored as <c>model[name + "_stop_text"]</c>.
    /// When a non-empty string is given, the matched stop text is stored under that name.
    /// </param>
    /// <param name="lazy">
    /// When <c>true</c>, match as few tokens as possible (non-greedy).
    /// </param>
    public static RuleNode Gen(
        string? name = null,
        string? regex = null,
        string? stop = null,
        string? stopRegex = null,
        string? suffix = null,
        float? temperature = null,
        int? maxTokens = null,
        bool listAppend = false,
        bool saveStopText = false,
        bool lazy = false)
    {
        if (stop is not null && stopRegex is not null)
            throw new ArgumentException(
                "Cannot specify both stop and stopRegex.", nameof(stopRegex));

        GrammarNode? stopValue = stop is not null ? new LiteralNode(stop)
            : stopRegex is not null ? new RegexNode(stopRegex)
            : null;

        string? stopCapture = saveStopText
            ? (name is not null ? name + "_stop_text" : "gen_stop_text")
            : null;

        return new RuleNode(
            Name: name ?? "gen",
            Value: new RegexNode(regex),   // null regex = unconstrained
            Capture: name,
            Temperature: temperature,
            MaxTokens: maxTokens,
            Stop: stopValue,
            Suffix: suffix is not null ? new LiteralNode(suffix) : null,
            ListAppend: listAppend,
            StopCapture: stopCapture,
            Lazy: lazy
        );
    }

    // -----------------------------------------------------------------------
    // select()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="RuleNode"/> that constrains model output to one of
    /// the supplied string <paramref name="options"/>.
    ///
    /// Corresponds to <c>select()</c> in <c>guidance/_grammar.py</c>.
    /// </summary>
    /// <param name="options">The string options the model may choose from.</param>
    /// <param name="name">Optional capture name.</param>
    /// <param name="listAppend">When <c>true</c> the capture is appended to a list.</param>
    public static RuleNode Select(
        IEnumerable<string> options,
        string? name = null,
        bool listAppend = false)
    {
        var nodes = options
            .Select(o => (GrammarNode)new LiteralNode(o))
            .ToImmutableArray();

        if (nodes.IsEmpty)
            throw new ArgumentException("options must not be empty.", nameof(options));

        return new RuleNode(
            Name: name ?? "select",
            Value: new SelectNode(nodes),
            Capture: name,
            ListAppend: listAppend
        );
    }

    /// <summary>
    /// Creates a <see cref="RuleNode"/> that constrains model output to one of
    /// the supplied grammar node <paramref name="options"/>.
    /// </summary>
    public static RuleNode Select(
        IEnumerable<GrammarNode> options,
        string? name = null,
        bool listAppend = false)
    {
        var nodes = options.ToImmutableArray();

        if (nodes.IsEmpty)
            throw new ArgumentException("options must not be empty.", nameof(options));

        return new RuleNode(
            Name: name ?? "select",
            Value: new SelectNode(nodes),
            Capture: name,
            ListAppend: listAppend
        );
    }

    // -----------------------------------------------------------------------
    // json()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="RuleNode"/> that constrains model output to valid JSON.
    ///
    /// Corresponds to <c>json()</c> in <c>guidance/library/_json.py</c>.
    /// On OpenAI-compatible backends this maps to <c>response_format.type = "json_schema"</c>
    /// or <c>"json_object"</c> when no schema is supplied.
    /// </summary>
    /// <param name="name">Optional capture name.</param>
    /// <param name="schemaJson">
    /// Optional JSON schema as a serialised string.  When omitted, any valid JSON
    /// object is accepted.
    /// </param>
    /// <param name="temperature">Sampling temperature.</param>
    /// <param name="maxTokens">Maximum tokens to generate.</param>
    public static RuleNode Json(
        string? name = null,
        string? schemaJson = null,
        float? temperature = null,
        int? maxTokens = null)
    {
        return new RuleNode(
            Name: name ?? "json",
            Value: new JsonSchemaNode(schemaJson),
            Capture: name,
            Temperature: temperature,
            MaxTokens: maxTokens
        );
    }

    /// <summary>
    /// Creates a <see cref="RuleNode"/> that constrains model output to valid JSON
    /// conforming to the schema inferred from the given .NET <paramref name="type"/>.
    ///
    /// Uses <see cref="JsonSchemaExporter"/> (available in .NET 9+) to derive the
    /// schema, mirroring the Pydantic-based approach in the Python library.
    /// </summary>
    /// <param name="type">
    /// The .NET type whose JSON schema is to be generated.  The type must be
    /// serialisable by <c>System.Text.Json</c>.
    /// </param>
    /// <param name="name">Optional capture name.</param>
    /// <param name="options">
    /// Optional <see cref="JsonSerializerOptions"/> used during schema inference.
    /// When <c>null</c>, <see cref="JsonSerializerOptions.Default"/> is used.
    /// </param>
    /// <param name="temperature">Sampling temperature.</param>
    /// <param name="maxTokens">Maximum tokens to generate.</param>
    public static RuleNode Json(
        Type type,
        string? name = null,
        JsonSerializerOptions? options = null,
        float? temperature = null,
        int? maxTokens = null)
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
        };
        var schemaNode = JsonSchemaExporter.GetJsonSchemaAsNode(
            options ?? JsonSerializerOptions.Default,
            type,
            exporterOptions);
        var schemaJson = schemaNode.ToJsonString();

        return new RuleNode(
            Name: name ?? "json",
            Value: new JsonSchemaNode(schemaJson),
            Capture: name,
            Temperature: temperature,
            MaxTokens: maxTokens
        );
    }

    /// <summary>
    /// Creates a <see cref="RuleNode"/> that constrains model output to valid JSON
    /// conforming to the schema inferred from <typeparamref name="T"/>.
    ///
    /// Convenience generic overload of <see cref="Json(Type,string?,JsonSerializerOptions?,float?,int?)"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The .NET type whose JSON schema is to be generated.
    /// </typeparam>
    /// <param name="name">Optional capture name.</param>
    /// <param name="options">
    /// Optional <see cref="JsonSerializerOptions"/> used during schema inference.
    /// </param>
    /// <param name="temperature">Sampling temperature.</param>
    /// <param name="maxTokens">Maximum tokens to generate.</param>
    public static RuleNode Json<T>(
        string? name = null,
        JsonSerializerOptions? options = null,
        float? temperature = null,
        int? maxTokens = null)
        => Json(typeof(T), name, options, temperature, maxTokens);

    // -----------------------------------------------------------------------
    // Primitives
    // -----------------------------------------------------------------------

    /// <summary>Creates a <see cref="LiteralNode"/> matching an exact string.</summary>
    public static LiteralNode String(string value) => new(value);

    /// <summary>Creates a <see cref="RegexNode"/> matching a regular expression pattern.</summary>
    public static RegexNode Regex(string pattern) => new(pattern);

    // -----------------------------------------------------------------------
    // token_limit() / with_temperature()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns a copy of <paramref name="value"/> with <see cref="RuleNode.MaxTokens"/>
    /// set to <paramref name="maxTokens"/>.
    ///
    /// If <paramref name="value"/> is already a <see cref="RuleNode"/>, the existing
    /// node is cloned with the new token limit.  Otherwise, it is wrapped in a new
    /// <see cref="RuleNode"/>.
    ///
    /// Corresponds to <c>token_limit()</c> in <c>guidance/_grammar.py</c>.
    /// </summary>
    public static RuleNode TokenLimit(GrammarNode value, int maxTokens)
    {
        if (value is RuleNode rule)
            return rule with { MaxTokens = maxTokens };
        return new RuleNode(Name: "token_limit", Value: value, MaxTokens: maxTokens);
    }

    /// <summary>
    /// Returns a copy of <paramref name="value"/> with <see cref="RuleNode.Temperature"/>
    /// set to <paramref name="temperature"/>.
    ///
    /// If <paramref name="value"/> is already a <see cref="RuleNode"/>, the existing
    /// node is cloned with the new temperature.  Otherwise, it is wrapped in a new
    /// <see cref="RuleNode"/>.
    ///
    /// Corresponds to <c>with_temperature()</c> in <c>guidance/_grammar.py</c>.
    /// </summary>
    public static RuleNode WithTemperature(GrammarNode value, float temperature)
    {
        if (value is RuleNode rule)
            return rule with { Temperature = temperature };
        return new RuleNode(Name: "with_temperature", Value: value, Temperature: temperature);
    }

    // -----------------------------------------------------------------------
    // capture()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Wraps <paramref name="value"/> so its generated text is stored under
    /// <paramref name="name"/>.
    ///
    /// If <paramref name="value"/> is a <see cref="RuleNode"/> that has no capture
    /// yet, the capture name is applied in-place (clone); otherwise a new wrapper
    /// <see cref="RuleNode"/> is created.
    ///
    /// Corresponds to <c>capture()</c> in <c>guidance/_grammar.py</c>.
    /// </summary>
    public static RuleNode Capture(
        GrammarNode value,
        string name,
        bool listAppend = false)
    {
        if (value is RuleNode rule && rule.Capture is null)
            return rule with { Capture = name, ListAppend = listAppend };
        return new RuleNode(
            Name: "capture",
            Value: value,
            Capture: name,
            ListAppend: listAppend);
    }

    // -----------------------------------------------------------------------
    // quote_regex()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Escapes all regex special characters in <paramref name="value"/> so that
    /// the result can be used as a literal pattern inside a larger regular expression.
    ///
    /// Corresponds to <c>quote_regex()</c> in <c>guidance/_grammar.py</c>.
    /// </summary>
    public static string QuoteRegex(string value)
        => System.Text.RegularExpressions.Regex.Escape(value)
            // Python's quote_regex only escapes: \ + * ? ^ $ ( ) { } [ ] . | —
            // System.Text.RegularExpressions.Regex.Escape additionally escapes # and spaces,
            // which is harmless for our purposes.
            ;

    // -----------------------------------------------------------------------
    // Repeat helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="RuleNode"/> whose body repeats <paramref name="value"/>
    /// between <paramref name="min"/> and <paramref name="max"/> times.
    ///
    /// Corresponds to <c>repeat()</c> in <c>guidance/_grammar.py</c>.
    /// </summary>
    public static RuleNode Repeat(GrammarNode value, int min, int? max = null)
    {
        if (min < 0)
            throw new ArgumentOutOfRangeException(nameof(min), "min must be >= 0.");
        if (max is not null && max < min)
            throw new ArgumentOutOfRangeException(nameof(max), "max must be >= min.");

        return new RuleNode(
            Name: "repeat",
            Value: new RepeatNode(value, min, max)
        );
    }

    /// <summary>
    /// Zero or more repetitions of <paramref name="value"/>.
    /// Corresponds to <c>zero_or_more()</c>.
    /// </summary>
    public static RuleNode ZeroOrMore(GrammarNode value) => Repeat(value, 0);

    /// <summary>
    /// One or more repetitions of <paramref name="value"/>.
    /// Corresponds to <c>one_or_more()</c>.
    /// </summary>
    public static RuleNode OneOrMore(GrammarNode value) => Repeat(value, 1);

    /// <summary>
    /// Zero or one repetition of <paramref name="value"/> (optional).
    /// Corresponds to <c>optional()</c>.
    /// </summary>
    public static RuleNode Optional(GrammarNode value) => Repeat(value, 0, 1);

    /// <summary>
    /// Exactly <paramref name="nRepeats"/> repetitions of <paramref name="value"/>.
    /// Corresponds to <c>exactly_n_repeats()</c> in <c>guidance/library/_sequences.py</c>.
    /// </summary>
    public static RuleNode ExactlyNRepeats(GrammarNode value, int nRepeats)
        => Repeat(value, nRepeats, nRepeats);

    /// <summary>
    /// At most <paramref name="nRepeats"/> repetitions of <paramref name="value"/> (zero or more).
    /// Corresponds to <c>at_most_n_repeats()</c> in <c>guidance/library/_sequences.py</c>.
    /// </summary>
    public static RuleNode AtMostNRepeats(GrammarNode value, int nRepeats)
        => Repeat(value, 0, nRepeats);

    /// <summary>
    /// Alias for <see cref="Repeat"/> that mirrors the Python
    /// <c>sequence()</c> helper in <c>guidance/library/_sequences.py</c>.
    /// </summary>
    public static RuleNode Sequence(GrammarNode value, int minLength = 0, int? maxLength = null)
        => Repeat(value, minLength, maxLength);

    // -----------------------------------------------------------------------
    // subgrammar()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Wraps <paramref name="body"/> in a <see cref="SubgrammarNode"/> so it is
    /// treated as a self-contained grammar unit.
    ///
    /// Corresponds to <c>subgrammar()</c> in <c>guidance/_grammar.py</c>.
    /// </summary>
    /// <param name="body">The inner grammar.</param>
    /// <param name="name">Optional capture name and rule name.</param>
    /// <param name="skipRegex">
    /// Optional regex pattern for tokens to skip between body elements.
    /// </param>
    /// <param name="maxTokens">Optional token budget.</param>
    /// <param name="temperature">Optional sampling temperature.</param>
    public static RuleNode Subgrammar(
        GrammarNode body,
        string? name = null,
        string? skipRegex = null,
        int? maxTokens = null,
        float? temperature = null)
    {
        var ruleName = name
            ?? (body is RuleNode rn ? rn.Name : "subgrammar");

        RuleNode node = new RuleNode(
            Name: ruleName,
            Value: new SubgrammarNode(body, skipRegex));

        if (maxTokens is not null)
            node = TokenLimit(node, maxTokens.Value);
        if (temperature is not null)
            node = WithTemperature(node, temperature.Value);
        if (name is not null)
            node = Capture(node, name);
        return node;
    }

    // -----------------------------------------------------------------------
    // substring()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Constrains the model to generate any contiguous sub-sequence of the
    /// chunks derived from <paramref name="targetString"/>.
    ///
    /// Corresponds to <c>substring()</c> in <c>guidance/library/_substring.py</c>.
    /// </summary>
    /// <param name="targetString">The full string the model must produce a substring of.</param>
    /// <param name="chunk">
    /// <c>"word"</c> (default) — split on word boundaries; <c>"character"</c> — split
    /// character by character.
    /// </param>
    /// <param name="name">Optional capture name.</param>
    public static RuleNode Substring(
        string targetString,
        string chunk = "word",
        string? name = null)
    {
        ImmutableArray<string> chunks = chunk switch
        {
            "word" => ChunkOnWord(targetString),
            "character" => [.. targetString.Select(c => c.ToString())],
            _ => throw new ArgumentException(
                "chunk must be \"word\" or \"character\".", nameof(chunk))
        };

        return new RuleNode(
            Name: name ?? "substring",
            Value: new SubstringNode(chunks),
            Capture: name);
    }

    // -----------------------------------------------------------------------
    // lark()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="RuleNode"/> whose body is a raw Lark EBNF grammar string
    /// interpreted directly by the llguidance engine.
    ///
    /// On remote (OpenAI) backends this falls back to unconstrained generation because
    /// local constrained decoding is not yet supported.  When a local backend is available
    /// the grammar string is compiled by llguidance and used to constrain token sampling.
    ///
    /// Corresponds to <c>lark()</c> in <c>guidance/library/_ebnf.py</c>.
    /// </summary>
    /// <param name="larkGrammar">
    /// A Lark grammar string.  See
    /// https://github.com/guidance-ai/llguidance/blob/main/docs/syntax.md for syntax details.
    /// </param>
    /// <param name="name">Optional capture name.</param>
    /// <param name="temperature">Optional sampling temperature.</param>
    /// <param name="maxTokens">Optional token budget.</param>
    public static RuleNode Lark(
        string larkGrammar,
        string? name = null,
        float? temperature = null,
        int? maxTokens = null)
    {
        RuleNode node = new RuleNode(
            Name: name ?? "lark",
            Value: new LarkNode(larkGrammar));

        if (temperature is not null)
            node = WithTemperature(node, temperature.Value);
        if (maxTokens is not null)
            node = TokenLimit(node, maxTokens.Value);
        if (name is not null)
            node = Capture(node, name);
        return node;
    }

    // -----------------------------------------------------------------------
    // special_token()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="SpecialTokenNode"/> from a token string of the form
    /// <c>&lt;token_name&gt;</c>.
    ///
    /// Corresponds to <c>special_token()</c> in <c>guidance/_grammar.py</c>.
    /// </summary>
    /// <param name="token">
    /// A string matching <c>&lt;[^&lt;&gt;]+&gt;</c>, e.g. <c>"&lt;|endoftext|&gt;"</c>.
    /// </param>
    public static SpecialTokenNode SpecialToken(string token)
    {
        var match = System.Text.RegularExpressions.Regex.Match(token, @"^<([^<>]+)>$");
        if (!match.Success)
            throw new ArgumentException(
                "token must be of the form \"<token_name>\", e.g. \"<|endoftext|>\".",
                nameof(token));
        return new SpecialTokenNode(match.Groups[1].Value);
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    // Word-boundary chunking that mirrors Python's chunk_on_word():
    //   re.findall(r"(\s+|\w+|[^\s\w]+)", text)
    private static ImmutableArray<string> ChunkOnWord(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            text, @"(\s+|\w+|[^\s\w]+)");
        var builder = ImmutableArray.CreateBuilder<string>(matches.Count);
        foreach (System.Text.RegularExpressions.Match m in matches)
            builder.Add(m.Value);
        return builder.ToImmutable();
    }
}
