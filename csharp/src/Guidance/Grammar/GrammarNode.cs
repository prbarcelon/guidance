using System.Collections.Immutable;

namespace Guidance.Grammar;

// ---------------------------------------------------------------------------
// Base
// ---------------------------------------------------------------------------

/// <summary>
/// Abstract base class for all grammar nodes in the Guidance AST.
///
/// Grammar nodes are <b>immutable</b> (C# records with value semantics) and
/// compose into larger patterns via the <c>+</c> operator, which concatenates
/// them into a <see cref="JoinNode"/>.
///
/// Corresponds to <c>GrammarNode</c> in <c>guidance/_ast.py</c>.
/// </summary>
public abstract record GrammarNode
{
    /// <summary>
    /// Returns <c>true</c> if this node matches only the empty string
    /// (i.e. is a no-op in a sequence).
    /// </summary>
    public virtual bool IsNull => false;

    /// <summary>Concatenates two grammar nodes into a <see cref="JoinNode"/>.</summary>
    public static GrammarNode operator +(GrammarNode left, GrammarNode right)
    {
        if (left.IsNull) return right;
        if (right.IsNull) return left;

        var builder = ImmutableArray.CreateBuilder<GrammarNode>();
        Flatten(builder, left);
        Flatten(builder, right);
        return new JoinNode(builder.ToImmutable());
    }

    /// <summary>Concatenates a grammar node with a string literal.</summary>
    public static GrammarNode operator +(GrammarNode left, string right)
        => left + new LiteralNode(right);

    /// <summary>Concatenates a string literal with a grammar node.</summary>
    public static GrammarNode operator +(string left, GrammarNode right)
        => new LiteralNode(left) + right;

    // Flatten nested JoinNodes to keep the tree shallow.
    private static void Flatten(ImmutableArray<GrammarNode>.Builder builder, GrammarNode node)
    {
        if (node is JoinNode join)
            foreach (var child in join.Children)
                builder.Add(child);
        else
            builder.Add(node);
    }
}

// ---------------------------------------------------------------------------
// Concrete node types
// ---------------------------------------------------------------------------

/// <summary>
/// Matches an exact string literal.
/// Corresponds to <c>LiteralNode</c> in <c>guidance/_ast.py</c>.
/// </summary>
/// <param name="Value">The exact string to match.</param>
public sealed record LiteralNode(string Value) : GrammarNode
{
    /// <inheritdoc/>
    public override bool IsNull => Value.Length == 0;
}

/// <summary>
/// Constrains model output to text that matches a regular expression.
/// Corresponds to <c>RegexNode</c> in <c>guidance/_ast.py</c>.
/// </summary>
/// <param name="Pattern">
/// The regular expression pattern, or <c>null</c> for unconstrained generation
/// (equivalent to <c>.*</c>).
/// </param>
public sealed record RegexNode(string? Pattern) : GrammarNode;

/// <summary>
/// Concatenates multiple grammar nodes in sequence.
/// Corresponds to <c>JoinNode</c> in <c>guidance/_ast.py</c>.
/// </summary>
/// <param name="Children">The ordered sequence of child nodes.</param>
public sealed record JoinNode(ImmutableArray<GrammarNode> Children) : GrammarNode
{
    /// <inheritdoc/>
    public override bool IsNull => Children.IsEmpty || Children.All(c => c.IsNull);
}

/// <summary>
/// Constrains model output to be exactly one of a set of alternatives.
/// Corresponds to <c>SelectNode</c> in <c>guidance/_ast.py</c>.
/// </summary>
/// <param name="Options">The candidate grammar nodes to choose from.</param>
public sealed record SelectNode(ImmutableArray<GrammarNode> Options) : GrammarNode
{
    /// <inheritdoc/>
    public override bool IsNull => Options.All(o => o.IsNull);
}

/// <summary>
/// Repeats a grammar node between <see cref="Min"/> and <see cref="Max"/> times.
/// Corresponds to <c>RepeatNode</c> in <c>guidance/_ast.py</c>.
/// </summary>
/// <param name="Value">The node to repeat.</param>
/// <param name="Min">Minimum number of repetitions (≥ 0).</param>
/// <param name="Max">Maximum number of repetitions, or <c>null</c> for unbounded.</param>
public sealed record RepeatNode(GrammarNode Value, int Min, int? Max = null) : GrammarNode
{
    /// <inheritdoc/>
    public override bool IsNull => Value.IsNull || (Min == 0 && Max == 0);
}

/// <summary>
/// Constrains model output to valid JSON conforming to an optional JSON schema.
/// Corresponds to <c>JsonNode</c> in <c>guidance/_ast.py</c>.
///
/// On OpenAI-compatible backends this maps to <c>response_format.type = "json_schema"</c>
/// (or <c>"json_object"</c> when no schema is provided).
/// </summary>
/// <param name="SchemaJson">
/// A JSON schema serialised as a string, or <c>null</c> to accept any valid JSON object.
/// </param>
public sealed record JsonSchemaNode(string? SchemaJson = null) : GrammarNode;

/// <summary>
/// A named grammar rule that wraps another node and optionally captures its output,
/// sets temperature/token limits, or specifies stop conditions.
///
/// This is the primary type produced by grammar builder functions such as
/// <see cref="GrammarFunctions.Gen"/>, <see cref="GrammarFunctions.Select"/>, and
/// <see cref="GrammarFunctions.Json"/>.
///
/// Corresponds to <c>RuleNode</c> in <c>guidance/_ast.py</c>.
/// </summary>
/// <param name="Name">Friendly rule name (used as default capture name).</param>
/// <param name="Value">The grammar node this rule wraps.</param>
/// <param name="Capture">
/// If non-<c>null</c>, generated text is stored as <c>model[Capture]</c>.
/// </param>
/// <param name="ListAppend">
/// If <c>true</c>, captured text is appended to a list rather than overwriting.
/// </param>
/// <param name="Temperature">Optional sampling temperature for this rule.</param>
/// <param name="MaxTokens">Optional token budget for generation.</param>
/// <param name="Stop">Optional stop pattern (literal or regex).</param>
/// <param name="Suffix">
/// Optional literal to append immediately after the generated text (not captured).
/// </param>
/// <param name="StopCapture">
/// If non-<c>null</c>, the matched stop text is also stored under this name.
/// </param>
public sealed record RuleNode(
    string Name,
    GrammarNode Value,
    string? Capture = null,
    bool ListAppend = false,
    float? Temperature = null,
    int? MaxTokens = null,
    GrammarNode? Stop = null,
    LiteralNode? Suffix = null,
    string? StopCapture = null
) : GrammarNode;
