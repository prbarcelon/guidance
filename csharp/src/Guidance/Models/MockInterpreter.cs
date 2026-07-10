using System.Text;
using System.Text.RegularExpressions;
using Guidance.Grammar;

namespace Guidance.Models;

/// <summary>
/// A lightweight, fully offline interpreter used for unit testing and grammar
/// validation without requiring a real LLM backend.
///
/// For generation rules (<see cref="RuleNode"/>s whose body is a <see cref="RegexNode"/>)
/// it returns the first prefix of <c>mockResponse</c> that satisfies any regex /
/// stop constraint, falling back to the whole response string.
/// For selection rules it picks the first option whose text appears as a prefix
/// of <c>mockResponse</c>, or simply the first option.
/// For JSON rules it returns <c>mockResponse</c> unchanged (callers should
/// supply valid JSON as the mock response when testing JSON grammars).
/// For substring rules it returns the longest prefix of <c>mockResponse</c> that
/// can be formed from the available chunks.
/// For subgrammar rules it recursively resolves the inner body.
///
/// Corresponds to <c>Mock</c> / <c>MockEngine</c> in
/// <c>guidance/models/_mock.py</c>.
/// </summary>
public sealed class MockInterpreter : IAsyncInterpreter
{
    private readonly string _mockResponse;
    private readonly StringBuilder _text = new();
    private readonly Dictionary<string, CaptureValue> _captures = new();
    private string? _activeRole;

    /// <summary>
    /// Initialises a new <see cref="MockInterpreter"/>.
    /// </summary>
    /// <param name="mockResponse">
    /// The string the interpreter will produce in response to any generation rule.
    /// Defaults to the empty string.
    /// </param>
    public MockInterpreter(string mockResponse = "")
    {
        _mockResponse = mockResponse;
    }

    // Copy constructor used by Clone().
    private MockInterpreter(
        string mockResponse,
        string text,
        IReadOnlyDictionary<string, CaptureValue> captures,
        string? activeRole)
    {
        _mockResponse = mockResponse;
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
    public IInterpreter Clone()
        => new MockInterpreter(_mockResponse, _text.ToString(), _captures, _activeRole);

    /// <inheritdoc/>
    public void AppendLiteral(string text)
    {
        _text.Append(text);
    }

    /// <inheritdoc/>
    public void ApplyRule(RuleNode rule)
    {
        string generated = ResolveGenerated(rule);

        _text.Append(generated);

        if (rule.Capture is not null)
            StoreCapture(rule.Capture, generated, rule.ListAppend);

        if (rule.StopCapture is not null)
            StoreCapture(rule.StopCapture, ResolveStop(rule, generated), listAppend: false);

        if (rule.Suffix is not null)
            _text.Append(rule.Suffix.Value);
    }

    /// <inheritdoc/>
    public void StartRole(string role)
    {
        if (_activeRole is not null)
            throw new InvalidOperationException(
                $"Cannot open role '{role}': role '{_activeRole}' is already open.");
        _activeRole = role;
        _text.Append($"<|{role}|>");
    }

    /// <inheritdoc/>
    public void EndRole(string role)
    {
        if (_activeRole is null)
            throw new InvalidOperationException("Cannot close role: no role is currently open.");
        if (_activeRole != role)
            throw new InvalidOperationException(
                $"Cannot close role '{role}': role '{_activeRole}' is open.");
        _activeRole = null;
        _text.Append($"<|/{role}|>");
    }

    // -----------------------------------------------------------------------
    // IAsyncInterpreter
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public Task AppendLiteralAsync(string text, CancellationToken cancellationToken = default)
    {
        AppendLiteral(text);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ApplyRuleAsync(RuleNode rule, CancellationToken cancellationToken = default)
    {
        ApplyRule(rule);
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private string ResolveGenerated(RuleNode rule)
    {
        // --- Select: pick the first option that is a prefix of the mock response,
        //             or fall back to the first option.
        if (rule.Value is SelectNode select)
        {
            foreach (var option in select.Options.OfType<LiteralNode>())
            {
                if (_mockResponse.StartsWith(option.Value, StringComparison.Ordinal))
                    return option.Value;
            }
            // No prefix match — return the first literal option (or empty string).
            return select.Options.OfType<LiteralNode>().FirstOrDefault()?.Value ?? string.Empty;
        }

        // --- JSON: return mock response unchanged.
        if (rule.Value is JsonSchemaNode)
            return ApplyLimits(rule, _mockResponse);

        // --- Subgrammar: resolve the inner body recursively.
        if (rule.Value is SubgrammarNode subgrammar)
        {
            var innerRule = subgrammar.Body is RuleNode innerRuleNode
                ? innerRuleNode
                : new RuleNode("subgrammar_inner", subgrammar.Body);
            return ApplyLimits(rule, ResolveGenerated(innerRule));
        }

        // --- Substring: pick the longest prefix of mockResponse that can be
        //     assembled from contiguous chunks in order.
        if (rule.Value is SubstringNode substring)
            return ApplyLimits(rule, ResolveSubstring(substring.Chunks, _mockResponse));

        // --- SpecialToken: return the token text formatted as <text>.
        if (rule.Value is SpecialTokenNode specialToken)
            return $"<{specialToken.TokenText}>";

        // --- LarkNode: return mock response unchanged.
        if (rule.Value is LarkNode)
            return ApplyLimits(rule, _mockResponse);

        // --- Regex / gen: return the portion of _mockResponse that satisfies
        //                  any regex constraint and stop condition.
        string candidate = _mockResponse;

        if (rule.Value is RegexNode { Pattern: { } pattern })
        {
            var match = Regex.Match(candidate, pattern);
            if (match.Success)
                candidate = match.Value;
        }

        if (rule.Stop is LiteralNode stopLit)
        {
            var idx = candidate.IndexOf(stopLit.Value, StringComparison.Ordinal);
            if (idx >= 0)
                candidate = candidate[..idx];
        }
        else if (rule.Stop is RegexNode stopRegex && stopRegex.Pattern is not null)
        {
            var m = Regex.Match(candidate, stopRegex.Pattern);
            if (m.Success)
                candidate = candidate[..m.Index];
        }

        return ApplyLimits(rule, candidate);
    }

    /// <summary>Applies the MaxTokens limit to a resolved candidate string.</summary>
    private static string ApplyLimits(RuleNode rule, string candidate)
    {
        if (rule.MaxTokens is { } maxTok)
        {
            // Very rough approximation: ~4 chars per token.
            var approxMax = maxTok * 4;
            if (candidate.Length > approxMax)
                candidate = candidate[..approxMax];
        }
        return candidate;
    }

    /// <summary>
    /// Resolves the stop text that would have matched to produce the given
    /// <paramref name="generated"/> value (used for <c>StopCapture</c>).
    /// </summary>
    private string ResolveStop(RuleNode rule, string generated)
    {
        if (rule.Stop is LiteralNode stopLit)
        {
            var after = _mockResponse[generated.Length..];
            return after.StartsWith(stopLit.Value, StringComparison.Ordinal)
                ? stopLit.Value
                : string.Empty;
        }
        if (rule.Stop is RegexNode stopRegex && stopRegex.Pattern is not null)
        {
            var after = _mockResponse[generated.Length..];
            var m = Regex.Match(after, stopRegex.Pattern);
            return m.Success ? m.Value : string.Empty;
        }
        return string.Empty;
    }

    /// <summary>
    /// Greedily assembles chunks from <paramref name="chunks"/> to match a prefix
    /// of <paramref name="candidate"/>.
    /// </summary>
    private static string ResolveSubstring(
        System.Collections.Immutable.ImmutableArray<string> chunks,
        string candidate)
    {
        var result = new StringBuilder();
        int pos = 0;
        foreach (var chunk in chunks)
        {
            if (pos + chunk.Length <= candidate.Length
                && candidate.AsSpan(pos, chunk.Length).SequenceEqual(chunk))
            {
                result.Append(chunk);
                pos += chunk.Length;
            }
        }
        return result.ToString();
    }

    private void StoreCapture(string name, string value, bool listAppend)
    {
        if (listAppend && _captures.TryGetValue(name, out var existing))
            _captures[name] = existing.Append(value);
        else
            _captures[name] = new CaptureValue(value);
    }
}

/// <summary>
/// Convenience factory for creating a <see cref="Model"/> backed by
/// <see cref="MockInterpreter"/>.
/// </summary>
public static class MockModel
{
    /// <summary>
    /// Creates a <see cref="Model"/> that returns <paramref name="mockResponse"/>
    /// for every generation rule, without calling any real LLM.
    /// </summary>
    public static Model Create(string mockResponse = "") =>
        Model.From(new MockInterpreter(mockResponse));
}


/// <summary>
/// A lightweight, fully offline interpreter used for unit testing and grammar
/// validation without requiring a real LLM backend.
///
/// For generation rules (<see cref="RuleNode"/>s whose body is a <see cref="RegexNode"/>)
/// it returns the first prefix of <c>mockResponse</c> that satisfies any regex /
/// stop constraint, falling back to the whole response string.
/// For selection rules it picks the first option whose text appears as a prefix
/// of <c>mockResponse</c>, or simply the first option.
/// For JSON rules it returns <c>mockResponse</c> unchanged (callers should
/// supply valid JSON as the mock response when testing JSON grammars).
///
/// Corresponds to <c>Mock</c> / <c>MockEngine</c> in
/// <c>guidance/models/_mock.py</c>.
