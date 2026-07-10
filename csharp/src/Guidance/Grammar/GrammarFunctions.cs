using System.Collections.Immutable;

namespace Guidance.Grammar;

/// <summary>
/// Factory methods for building grammar nodes.
///
/// These correspond to the Python guidance library's top-level functions
/// (<c>gen()</c>, <c>select()</c>, <c>json()</c>, etc.) defined in
/// <c>guidance/_grammar.py</c> and <c>guidance/library/_gen.py</c>.
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
    public static RuleNode Gen(
        string? name = null,
        string? regex = null,
        string? stop = null,
        string? stopRegex = null,
        string? suffix = null,
        float? temperature = null,
        int? maxTokens = null,
        bool listAppend = false)
    {
        if (stop is not null && stopRegex is not null)
            throw new ArgumentException(
                "Cannot specify both stop and stopRegex.", nameof(stopRegex));

        GrammarNode? stopValue = stop is not null ? new LiteralNode(stop)
            : stopRegex is not null ? new RegexNode(stopRegex)
            : null;

        return new RuleNode(
            Name: name ?? "gen",
            Value: new RegexNode(regex),   // null regex = unconstrained
            Capture: name,
            Temperature: temperature,
            MaxTokens: maxTokens,
            Stop: stopValue,
            Suffix: suffix is not null ? new LiteralNode(suffix) : null,
            ListAppend: listAppend
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

    // -----------------------------------------------------------------------
    // Primitives
    // -----------------------------------------------------------------------

    /// <summary>Creates a <see cref="LiteralNode"/> matching an exact string.</summary>
    public static LiteralNode String(string value) => new(value);

    /// <summary>Creates a <see cref="RegexNode"/> matching a regular expression pattern.</summary>
    public static RegexNode Regex(string pattern) => new(pattern);

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
}
