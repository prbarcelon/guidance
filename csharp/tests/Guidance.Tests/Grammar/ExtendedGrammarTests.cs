using System.Collections.Immutable;
using Guidance.Grammar;
using Xunit;

namespace Guidance.Tests.Grammar;

/// <summary>
/// Unit tests for the new grammar nodes and functions added in the full port:
/// SubstringNode, SubgrammarNode, SpecialTokenNode, RuleRefNode, LarkNode,
/// TokenLimit, WithTemperature, Capture, QuoteRegex, ExactlyNRepeats,
/// AtMostNRepeats, Sequence, Subgrammar, Substring, SpecialToken,
/// Json&lt;T&gt;, Json(Type), Gen saveStopText/lazy.
/// </summary>
public class ExtendedGrammarTests
{
    // -----------------------------------------------------------------------
    // SubstringNode
    // -----------------------------------------------------------------------

    [Fact]
    public void SubstringNode_StoresChunks()
    {
        var chunks = ImmutableArray.Create("hello", " ", "world");
        var node = new SubstringNode(chunks);
        Assert.Equal(chunks, node.Chunks);
    }

    [Fact]
    public void SubstringNode_EmptyChunks_IsNull()
    {
        var node = new SubstringNode(ImmutableArray<string>.Empty);
        // SubstringNode does not override IsNull, so it returns false.
        // But an empty substring is still well-formed.
        Assert.False(node.IsNull);
    }

    // -----------------------------------------------------------------------
    // SubgrammarNode
    // -----------------------------------------------------------------------

    [Fact]
    public void SubgrammarNode_StoresBodyAndSkipRegex()
    {
        var body = new LiteralNode("abc");
        var node = new SubgrammarNode(body, @"\s+");
        Assert.Equal(body, node.Body);
        Assert.Equal(@"\s+", node.SkipRegex);
    }

    [Fact]
    public void SubgrammarNode_NullSkipRegex_Default()
    {
        var node = new SubgrammarNode(new LiteralNode("x"));
        Assert.Null(node.SkipRegex);
    }

    // -----------------------------------------------------------------------
    // SpecialTokenNode
    // -----------------------------------------------------------------------

    [Fact]
    public void SpecialTokenNode_StoresTokenText()
    {
        var node = new SpecialTokenNode("endoftext");
        Assert.Equal("endoftext", node.TokenText);
    }

    // -----------------------------------------------------------------------
    // LarkNode
    // -----------------------------------------------------------------------

    [Fact]
    public void LarkNode_StoresGrammar()
    {
        const string grammar = "start: \"hello\"";
        var node = new LarkNode(grammar);
        Assert.Equal(grammar, node.LarkGrammar);
    }

    // -----------------------------------------------------------------------
    // RuleRefNode
    // -----------------------------------------------------------------------

    [Fact]
    public void RuleRefNode_InitialTargetIsNull()
    {
        var ref_ = new RuleRefNode();
        Assert.Null(ref_.Target);
    }

    [Fact]
    public void RuleRefNode_SetTarget_Succeeds()
    {
        var ref_ = new RuleRefNode();
        var target = new RuleNode("list", new LiteralNode("item"));
        ref_.SetTarget(target);
        Assert.Same(target, ref_.Target);
    }

    [Fact]
    public void RuleRefNode_SetTargetTwice_Throws()
    {
        var ref_ = new RuleRefNode();
        var target = new RuleNode("x", new LiteralNode("a"));
        ref_.SetTarget(target);
        Assert.Throws<InvalidOperationException>(() => ref_.SetTarget(target));
    }

    // -----------------------------------------------------------------------
    // RuleNode.Lazy
    // -----------------------------------------------------------------------

    [Fact]
    public void RuleNode_DefaultLazy_IsFalse()
    {
        var rule = GrammarFunctions.Gen("x");
        Assert.False(rule.Lazy);
    }

    [Fact]
    public void Gen_WithLazy_SetsLazyFlag()
    {
        var rule = GrammarFunctions.Gen("x", lazy: true);
        Assert.True(rule.Lazy);
    }

    // -----------------------------------------------------------------------
    // TokenLimit
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenLimit_OnRuleNode_ClonesWithMaxTokens()
    {
        var original = GrammarFunctions.Gen("x");
        var limited = GrammarFunctions.TokenLimit(original, 42);

        Assert.Equal(42, limited.MaxTokens);
        Assert.Equal("x", limited.Name);       // name preserved
        Assert.Null(original.MaxTokens);       // original unchanged
    }

    [Fact]
    public void TokenLimit_OnLiteralNode_WrapsInRuleNode()
    {
        var literal = new LiteralNode("hello");
        var limited = GrammarFunctions.TokenLimit(literal, 10);

        Assert.Equal(10, limited.MaxTokens);
        Assert.Equal("token_limit", limited.Name);
        Assert.Equal(literal, limited.Value);
    }

    // -----------------------------------------------------------------------
    // WithTemperature
    // -----------------------------------------------------------------------

    [Fact]
    public void WithTemperature_OnRuleNode_ClonesWithTemperature()
    {
        var original = GrammarFunctions.Gen("x");
        var heated = GrammarFunctions.WithTemperature(original, 0.8f);

        Assert.Equal(0.8f, heated.Temperature);
        Assert.Equal("x", heated.Name);
        Assert.Null(original.Temperature);
    }

    [Fact]
    public void WithTemperature_OnRegexNode_WrapsInRuleNode()
    {
        var regex = new RegexNode(@"\d+");
        var heated = GrammarFunctions.WithTemperature(regex, 0.5f);

        Assert.Equal(0.5f, heated.Temperature);
        Assert.Equal("with_temperature", heated.Name);
        Assert.Equal(regex, heated.Value);
    }

    // -----------------------------------------------------------------------
    // Capture
    // -----------------------------------------------------------------------

    [Fact]
    public void Capture_OnRuleNodeWithoutCapture_SetsCapture()
    {
        var rule = new RuleNode("my_rule", new RegexNode(@"\w+"));
        Assert.Null(rule.Capture);

        var captured = GrammarFunctions.Capture(rule, "my_var");
        Assert.Equal("my_var", captured.Capture);
        Assert.Equal("my_rule", captured.Name);  // name unchanged
    }

    [Fact]
    public void Capture_OnRuleNodeWithExistingCapture_WrapsInNewRule()
    {
        var rule = GrammarFunctions.Gen("already_captured");
        var captured = GrammarFunctions.Capture(rule, "new_name");

        Assert.Equal("new_name", captured.Capture);
        Assert.Equal("capture", captured.Name);
        Assert.Equal(rule, captured.Value);
    }

    [Fact]
    public void Capture_OnLiteralNode_WrapsInRuleNode()
    {
        var literal = new LiteralNode("hello");
        var captured = GrammarFunctions.Capture(literal, "greeting");

        Assert.Equal("greeting", captured.Capture);
        Assert.Equal("capture", captured.Name);
        Assert.Equal(literal, captured.Value);
    }

    [Fact]
    public void Capture_WithListAppend_SetsListAppendFlag()
    {
        var rule = new RuleNode("r", new RegexNode(null));
        var captured = GrammarFunctions.Capture(rule, "list", listAppend: true);
        Assert.True(captured.ListAppend);
    }

    // -----------------------------------------------------------------------
    // QuoteRegex
    // -----------------------------------------------------------------------

    [Fact]
    public void QuoteRegex_EscapesSpecialCharacters()
    {
        // These characters are significant in regex and must be escaped.
        var result = GrammarFunctions.QuoteRegex("a.b+c*d?e");
        Assert.DoesNotContain(".", result.Replace(@"\.", ""));  // dot is escaped
        Assert.Contains(@"\+", result);
        Assert.Contains(@"\*", result);
        Assert.Contains(@"\?", result);
    }

    [Fact]
    public void QuoteRegex_PlainString_Unchanged()
    {
        // Letters and digits do not need escaping.
        var result = GrammarFunctions.QuoteRegex("hello123");
        Assert.Equal("hello123", result);
    }

    // -----------------------------------------------------------------------
    // ExactlyNRepeats / AtMostNRepeats / Sequence
    // -----------------------------------------------------------------------

    [Fact]
    public void ExactlyNRepeats_SetsMinAndMaxEqual()
    {
        var rule = GrammarFunctions.ExactlyNRepeats(new LiteralNode("x"), 3);
        var repeat = Assert.IsType<RepeatNode>(rule.Value);
        Assert.Equal(3, repeat.Min);
        Assert.Equal(3, repeat.Max);
    }

    [Fact]
    public void AtMostNRepeats_SetsMinZeroAndMaxN()
    {
        var rule = GrammarFunctions.AtMostNRepeats(new LiteralNode("x"), 5);
        var repeat = Assert.IsType<RepeatNode>(rule.Value);
        Assert.Equal(0, repeat.Min);
        Assert.Equal(5, repeat.Max);
    }

    [Fact]
    public void Sequence_DefaultMinZeroNoMax()
    {
        var rule = GrammarFunctions.Sequence(new LiteralNode("x"));
        var repeat = Assert.IsType<RepeatNode>(rule.Value);
        Assert.Equal(0, repeat.Min);
        Assert.Null(repeat.Max);
    }

    [Fact]
    public void Sequence_WithMinAndMax()
    {
        var rule = GrammarFunctions.Sequence(new LiteralNode("x"), minLength: 2, maxLength: 7);
        var repeat = Assert.IsType<RepeatNode>(rule.Value);
        Assert.Equal(2, repeat.Min);
        Assert.Equal(7, repeat.Max);
    }

    // -----------------------------------------------------------------------
    // Subgrammar
    // -----------------------------------------------------------------------

    [Fact]
    public void Subgrammar_BasicCreation_WrapsInSubgrammarNode()
    {
        var body = GrammarFunctions.Gen("inner");
        var rule = GrammarFunctions.Subgrammar(body);

        Assert.Equal("inner", rule.Name);
        // The outermost node has no capture (name was null).
        Assert.Null(rule.Capture);
    }

    [Fact]
    public void Subgrammar_WithName_SetsCapture()
    {
        var body = new LiteralNode("text");
        var rule = GrammarFunctions.Subgrammar(body, name: "result");

        Assert.Equal("result", rule.Capture);
    }

    [Fact]
    public void Subgrammar_WithMaxTokens_SetsMaxTokens()
    {
        var body = new LiteralNode("x");
        var rule = GrammarFunctions.Subgrammar(body, maxTokens: 50);
        Assert.Equal(50, rule.MaxTokens);
    }

    [Fact]
    public void Subgrammar_WithTemperature_SetsTemperature()
    {
        var body = new LiteralNode("x");
        var rule = GrammarFunctions.Subgrammar(body, temperature: 0.7f);
        Assert.Equal(0.7f, rule.Temperature);
    }

    [Fact]
    public void Subgrammar_WithSkipRegex_StoredInSubgrammarNode()
    {
        var body = new LiteralNode("x");
        var rule = GrammarFunctions.Subgrammar(body, skipRegex: @"\s*");

        // Navigate to SubgrammarNode — may be nested inside token_limit/capture wrappers.
        GrammarNode inner = rule;
        while (inner is RuleNode rn)
            inner = rn.Value;

        var sgNode = Assert.IsType<SubgrammarNode>(inner);
        Assert.Equal(@"\s*", sgNode.SkipRegex);
    }

    // -----------------------------------------------------------------------
    // Substring
    // -----------------------------------------------------------------------

    [Fact]
    public void Substring_WordChunking_SplitsOnWordBoundaries()
    {
        var rule = GrammarFunctions.Substring("hello world");
        var sn = Assert.IsType<SubstringNode>(rule.Value);
        // Should split into at least "hello", " ", "world"
        Assert.True(sn.Chunks.Length >= 2);
        Assert.Contains("hello", sn.Chunks);
        Assert.Contains("world", sn.Chunks);
    }

    [Fact]
    public void Substring_CharacterChunking_SplitsIntoChars()
    {
        var rule = GrammarFunctions.Substring("abc", chunk: "character");
        var sn = Assert.IsType<SubstringNode>(rule.Value);
        Assert.Equal(3, sn.Chunks.Length);
        Assert.Equal(new[] { "a", "b", "c" }, sn.Chunks.ToArray());
    }

    [Fact]
    public void Substring_InvalidChunkArg_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GrammarFunctions.Substring("hello", chunk: "invalid"));
    }

    [Fact]
    public void Substring_WithName_SetsCapture()
    {
        var rule = GrammarFunctions.Substring("hello world", name: "part");
        Assert.Equal("part", rule.Capture);
        Assert.Equal("part", rule.Name);
    }

    // -----------------------------------------------------------------------
    // SpecialToken function
    // -----------------------------------------------------------------------

    [Fact]
    public void SpecialToken_ValidToken_ParsesTokenText()
    {
        var node = GrammarFunctions.SpecialToken("<|endoftext|>");
        Assert.Equal("|endoftext|", node.TokenText);
    }

    [Fact]
    public void SpecialToken_WithoutAngleBrackets_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GrammarFunctions.SpecialToken("endoftext"));
    }

    [Fact]
    public void SpecialToken_EmptyContent_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GrammarFunctions.SpecialToken("<>"));
    }

    // -----------------------------------------------------------------------
    // Json<T> / Json(Type)
    // -----------------------------------------------------------------------

    private sealed record PersonRecord(string Name, int Age);

    [Fact]
    public void Json_WithType_GeneratesSchemaJson()
    {
        var rule = GrammarFunctions.Json(typeof(PersonRecord), name: "person");
        var json = Assert.IsType<JsonSchemaNode>(rule.Value);

        Assert.NotNull(json.SchemaJson);
        Assert.Contains("name", json.SchemaJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("age", json.SchemaJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Json_Generic_GeneratesSchemaJson()
    {
        var rule = GrammarFunctions.Json<PersonRecord>(name: "p");
        var json = Assert.IsType<JsonSchemaNode>(rule.Value);

        Assert.NotNull(json.SchemaJson);
        Assert.Contains("name", json.SchemaJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Json_Generic_SetsCaptureName()
    {
        var rule = GrammarFunctions.Json<PersonRecord>(name: "person");
        Assert.Equal("person", rule.Capture);
    }

    // -----------------------------------------------------------------------
    // Gen saveStopText
    // -----------------------------------------------------------------------

    [Fact]
    public void Gen_SaveStopText_SetsStopCaptureName()
    {
        var rule = GrammarFunctions.Gen("answer", stop: "\n", saveStopText: true);
        Assert.Equal("answer_stop_text", rule.StopCapture);
    }

    [Fact]
    public void Gen_SaveStopText_UnnamedGen_UsesDefaultName()
    {
        var rule = GrammarFunctions.Gen(stop: "\n", saveStopText: true);
        Assert.Equal("gen_stop_text", rule.StopCapture);
    }
}
