using System.Collections.Immutable;
using Guidance.Grammar;
using Xunit;

namespace Guidance.Tests.Grammar;

/// <summary>
/// Unit tests for grammar node construction and composition.
/// Corresponds to the assertions exercised by <c>tests/unit/test_grammar.py</c>
/// and <c>tests/unit/test_ast.py</c> in the Python library.
/// </summary>
public class GrammarTests
{
    // -----------------------------------------------------------------------
    // LiteralNode
    // -----------------------------------------------------------------------

    [Fact]
    public void LiteralNode_EmptyString_IsNull()
    {
        var node = new LiteralNode(string.Empty);
        Assert.True(node.IsNull);
    }

    [Fact]
    public void LiteralNode_NonEmptyString_IsNotNull()
    {
        var node = new LiteralNode("hello");
        Assert.False(node.IsNull);
    }

    [Fact]
    public void LiteralNode_RecordEquality()
    {
        var a = new LiteralNode("hello");
        var b = new LiteralNode("hello");
        Assert.Equal(a, b);
    }

    // -----------------------------------------------------------------------
    // RegexNode
    // -----------------------------------------------------------------------

    [Fact]
    public void RegexNode_StoresPattern()
    {
        var node = new RegexNode(@"\d+");
        Assert.Equal(@"\d+", node.Pattern);
    }

    [Fact]
    public void RegexNode_NullPattern_Allowed()
    {
        var node = new RegexNode(null);
        Assert.Null(node.Pattern);
    }

    // -----------------------------------------------------------------------
    // JoinNode / + operator
    // -----------------------------------------------------------------------

    [Fact]
    public void Join_TwoLiterals_FlattensIntoJoinNode()
    {
        var a = new LiteralNode("foo");
        var b = new LiteralNode("bar");
        var joined = a + b;

        var join = Assert.IsType<JoinNode>(joined);
        Assert.Equal(2, join.Children.Length);
        Assert.Equal(a, join.Children[0]);
        Assert.Equal(b, join.Children[1]);
    }

    [Fact]
    public void Join_NestedJoins_Flattens()
    {
        var a = new LiteralNode("a");
        var b = new LiteralNode("b");
        var c = new LiteralNode("c");

        var join = (a + b) + c;   // should produce JoinNode with 3 children, not 2
        var flat = Assert.IsType<JoinNode>(join);
        Assert.Equal(3, flat.Children.Length);
    }

    [Fact]
    public void Join_WithNullLeft_ReturnsRight()
    {
        GrammarNode left = new LiteralNode(string.Empty);
        var right = new LiteralNode("hello");
        var result = left + right;
        Assert.Equal(right, result);
    }

    [Fact]
    public void Join_WithNullRight_ReturnsLeft()
    {
        var left = new LiteralNode("hello");
        GrammarNode right = new LiteralNode(string.Empty);
        var result = left + right;
        Assert.Equal(left, result);
    }

    [Fact]
    public void Join_StringPlusGrammarNode_CreatesJoin()
    {
        var node = new RegexNode(@"\d+");
        var result = "prefix_" + node;
        var join = Assert.IsType<JoinNode>(result);
        Assert.Equal(new LiteralNode("prefix_"), join.Children[0]);
        Assert.Equal(node, join.Children[1]);
    }

    [Fact]
    public void Join_GrammarNodePlusString_CreatesJoin()
    {
        var node = new RegexNode(@"\d+");
        var result = node + "_suffix";
        var join = Assert.IsType<JoinNode>(result);
        Assert.Equal(node, join.Children[0]);
        Assert.Equal(new LiteralNode("_suffix"), join.Children[1]);
    }

    // -----------------------------------------------------------------------
    // SelectNode
    // -----------------------------------------------------------------------

    [Fact]
    public void SelectNode_AllNullOptions_IsNull()
    {
        var node = new SelectNode(ImmutableArray.Create<GrammarNode>(
            new LiteralNode(string.Empty),
            new LiteralNode(string.Empty)));
        Assert.True(node.IsNull);
    }

    [Fact]
    public void SelectNode_WithNonEmptyOption_IsNotNull()
    {
        var node = new SelectNode(ImmutableArray.Create<GrammarNode>(
            new LiteralNode("A"),
            new LiteralNode("B")));
        Assert.False(node.IsNull);
    }

    // -----------------------------------------------------------------------
    // RepeatNode
    // -----------------------------------------------------------------------

    [Fact]
    public void RepeatNode_ZeroToZero_IsNull()
    {
        var node = new RepeatNode(new LiteralNode("x"), 0, 0);
        Assert.True(node.IsNull);
    }

    [Fact]
    public void RepeatNode_ZeroOrMore_NotNull()
    {
        var node = new RepeatNode(new LiteralNode("x"), 0);
        Assert.False(node.IsNull);
    }

    // -----------------------------------------------------------------------
    // RuleNode
    // -----------------------------------------------------------------------

    [Fact]
    public void RuleNode_DefaultsAreNull()
    {
        var rule = new RuleNode("my_rule", new RegexNode(@"\w+"));
        Assert.Null(rule.Capture);
        Assert.Null(rule.Temperature);
        Assert.Null(rule.MaxTokens);
        Assert.Null(rule.Stop);
        Assert.False(rule.ListAppend);
    }

    [Fact]
    public void RuleNode_WithCapture()
    {
        var rule = new RuleNode("answer", new RegexNode(@"\w+"), Capture: "answer");
        Assert.Equal("answer", rule.Capture);
    }

    // -----------------------------------------------------------------------
    // GrammarFunctions
    // -----------------------------------------------------------------------

    [Fact]
    public void Gen_NoArgs_ReturnsRuleNodeWithNullPatternRegex()
    {
        var rule = GrammarFunctions.Gen();
        Assert.Equal("gen", rule.Name);
        Assert.IsType<RegexNode>(rule.Value);
        Assert.Null(rule.Capture);
    }

    [Fact]
    public void Gen_WithName_SetsCapture()
    {
        var rule = GrammarFunctions.Gen("result");
        Assert.Equal("result", rule.Name);
        Assert.Equal("result", rule.Capture);
    }

    [Fact]
    public void Gen_WithStop_SetsStopLiteral()
    {
        var rule = GrammarFunctions.Gen(stop: "\n");
        var stopNode = Assert.IsType<LiteralNode>(rule.Stop);
        Assert.Equal("\n", stopNode.Value);
    }

    [Fact]
    public void Gen_WithStopRegex_SetsStopRegex()
    {
        var rule = GrammarFunctions.Gen(stopRegex: @"\n");
        var stopNode = Assert.IsType<RegexNode>(rule.Stop);
        Assert.Equal(@"\n", stopNode.Pattern);
    }

    [Fact]
    public void Gen_BothStopAndStopRegex_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GrammarFunctions.Gen(stop: "\n", stopRegex: @"\n"));
    }

    [Fact]
    public void Select_StringOptions_ReturnsRuleNodeWithSelectValue()
    {
        var rule = GrammarFunctions.Select(["A", "B", "C"], name: "choice");
        Assert.Equal("choice", rule.Capture);
        var select = Assert.IsType<SelectNode>(rule.Value);
        Assert.Equal(3, select.Options.Length);
    }

    [Fact]
    public void Select_EmptyOptions_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GrammarFunctions.Select(Array.Empty<string>()));
    }

    [Fact]
    public void Json_NoSchema_SetsJsonSchemaNodeWithNullSchema()
    {
        var rule = GrammarFunctions.Json("output");
        Assert.Equal("output", rule.Capture);
        var json = Assert.IsType<JsonSchemaNode>(rule.Value);
        Assert.Null(json.SchemaJson);
    }

    [Fact]
    public void Json_WithSchema_StoresSchemaString()
    {
        const string schema = """{"type":"object"}""";
        var rule = GrammarFunctions.Json(schemaJson: schema);
        var json = Assert.IsType<JsonSchemaNode>(rule.Value);
        Assert.Equal(schema, json.SchemaJson);
    }

    [Fact]
    public void Repeat_InvalidMin_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GrammarFunctions.Repeat(new LiteralNode("x"), min: -1));
    }

    [Fact]
    public void Repeat_MaxLessThanMin_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GrammarFunctions.Repeat(new LiteralNode("x"), min: 3, max: 1));
    }

    [Fact]
    public void ZeroOrMore_WrapsInRepeatNodeWithMin0()
    {
        var rule = GrammarFunctions.ZeroOrMore(new LiteralNode("x"));
        var repeat = Assert.IsType<RepeatNode>(rule.Value);
        Assert.Equal(0, repeat.Min);
        Assert.Null(repeat.Max);
    }

    [Fact]
    public void OneOrMore_WrapsInRepeatNodeWithMin1()
    {
        var rule = GrammarFunctions.OneOrMore(new LiteralNode("x"));
        var repeat = Assert.IsType<RepeatNode>(rule.Value);
        Assert.Equal(1, repeat.Min);
    }
}
