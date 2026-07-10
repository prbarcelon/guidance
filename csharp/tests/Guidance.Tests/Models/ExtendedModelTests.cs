using Guidance.Grammar;
using Guidance.Models;
using Xunit;

namespace Guidance.Tests.Models;

/// <summary>
/// Tests for the new model-level features added in the full port:
/// async API, SubstringNode resolution, SubgrammarNode resolution,
/// SpecialTokenNode resolution, StopCapture, Lazy gen, and the
/// <see cref="IAsyncInterpreter"/> contract on <see cref="MockInterpreter"/>.
/// </summary>
public class ExtendedModelTests
{
    // -----------------------------------------------------------------------
    // Async API — AppendAsync / WithRoleAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AppendAsync_StringLiteral_ProducesCorrectText()
    {
        var lm = MockModel.Create();
        var result = await lm.AppendAsync("hello async");
        Assert.Equal("hello async", result.Text);
    }

    [Fact]
    public async Task AppendAsync_DoesNotMutateOriginal()
    {
        var original = MockModel.Create();
        _ = await original.AppendAsync("side-effect?");
        Assert.Equal(string.Empty, original.Text);
    }

    [Fact]
    public async Task AppendAsync_GrammarNode_ExecutesRule()
    {
        var lm = MockModel.Create(mockResponse: "async answer");
        var result = await lm.AppendAsync(GrammarFunctions.Gen("out"));
        Assert.Equal("async answer", result["out"]);
    }

    [Fact]
    public async Task WithSystemAsync_String_AddsRoleMarkers()
    {
        var lm = MockModel.Create();
        var result = await lm.WithSystemAsync("async system");
        Assert.Contains("<|system|>", result.Text);
        Assert.Contains("async system", result.Text);
        Assert.Contains("<|/system|>", result.Text);
    }

    [Fact]
    public async Task WithUserAsync_String_AddsRoleMarkers()
    {
        var lm = MockModel.Create();
        var result = await lm.WithUserAsync("question?");
        Assert.Contains("<|user|>", result.Text);
        Assert.Contains("question?", result.Text);
    }

    [Fact]
    public async Task WithAssistantAsync_Callback_GeneratesText()
    {
        var lm = MockModel.Create(mockResponse: "42");
        var result = await lm.WithAssistantAsync(
            async (m, ct) => await m.AppendAsync(GrammarFunctions.Gen("answer"), ct));

        Assert.Equal("42", result["answer"]);
        Assert.Contains("<|assistant|>", result.Text);
    }

    [Fact]
    public async Task AsyncChain_FullConversation_CapturesCorrectly()
    {
        var lm = MockModel.Create(mockResponse: "Paris");

        var result = await (await (await lm
            .WithSystemAsync("You are geography."))
            .WithUserAsync("Capital of France?"))
            .WithAssistantAsync(async (m, ct) =>
                await m.AppendAsync(GrammarFunctions.Gen("capital", maxTokens: 5), ct));

        Assert.Equal("Paris", result["capital"]);
    }

    [Fact]
    public async Task AppendAsync_EmptyString_ReturnsSameInstance()
    {
        var lm = MockModel.Create();
        var result = await lm.AppendAsync(string.Empty);
        Assert.Same(lm, result);
    }

    [Fact]
    public async Task CancellationToken_DoesNotBreakMockExecution()
    {
        using var cts = new CancellationTokenSource();
        var lm = MockModel.Create("value");
        var result = await lm.AppendAsync(GrammarFunctions.Gen("x"), cts.Token);
        Assert.Equal("value", result["x"]);
    }

    // -----------------------------------------------------------------------
    // MockInterpreter — IAsyncInterpreter contract
    // -----------------------------------------------------------------------

    [Fact]
    public void MockInterpreter_ImplementsIAsyncInterpreter()
    {
        IAsyncInterpreter interpreter = new MockInterpreter("test");
        Assert.NotNull(interpreter);
    }

    [Fact]
    public async Task MockInterpreter_AppendLiteralAsync_Works()
    {
        var interp = new MockInterpreter();
        await interp.AppendLiteralAsync("hello");
        Assert.Equal("hello", interp.CurrentText);
    }

    [Fact]
    public async Task MockInterpreter_ApplyRuleAsync_Works()
    {
        var interp = new MockInterpreter("world");
        var rule = GrammarFunctions.Gen("x");
        await interp.ApplyRuleAsync(rule);
        Assert.Equal("world", interp.CurrentText);
        Assert.Equal("world", interp.Captures["x"].Value);
    }

    // -----------------------------------------------------------------------
    // SubstringNode in MockInterpreter
    // -----------------------------------------------------------------------

    [Fact]
    public void Substring_MockInterpreter_MatchesFromChunks()
    {
        // Mock response is "hello world" and target is the full phrase split into chunks.
        var lm = MockModel.Create("hello world");
        var rule = GrammarFunctions.Substring("hello world", name: "sub");
        var result = lm + rule;
        // Should produce "hello world" (all chunks matched).
        Assert.Equal("hello world", result["sub"]);
    }

    [Fact]
    public void Substring_MockInterpreter_PartialMatch()
    {
        // Mock response is "hello" so only the first chunk matches.
        var lm = MockModel.Create("hello");
        var rule = GrammarFunctions.Substring("hello world", name: "sub");
        var result = lm + rule;
        Assert.Equal("hello", result["sub"]);
    }

    // -----------------------------------------------------------------------
    // SubgrammarNode in MockInterpreter
    // -----------------------------------------------------------------------

    [Fact]
    public void Subgrammar_MockInterpreter_ResolvesInnerBody()
    {
        var lm = MockModel.Create("42");
        var inner = GrammarFunctions.Gen("inner_val");
        var rule = GrammarFunctions.Subgrammar(inner, name: "outer_val");
        var result = lm + rule;
        Assert.Equal("42", result["outer_val"]);
    }

    // -----------------------------------------------------------------------
    // SpecialTokenNode in MockInterpreter
    // -----------------------------------------------------------------------

    [Fact]
    public void SpecialToken_MockInterpreter_ReturnsFormattedText()
    {
        var lm = MockModel.Create();
        var node = GrammarFunctions.SpecialToken("<|endoftext|>");
        // Wrap in a RuleNode so it can be applied via +.
        var rule = GrammarFunctions.Capture(node, "tok");
        var result = lm + rule;
        Assert.Equal("<|endoftext|>", result["tok"]);
    }

    // -----------------------------------------------------------------------
    // StopCapture
    // -----------------------------------------------------------------------

    [Fact]
    public void Gen_StopCapture_StoresStopText()
    {
        var lm = MockModel.Create("answer\nstop_was_here");
        var rule = GrammarFunctions.Gen("answer", stop: "\n", saveStopText: true);
        var result = lm + rule;

        Assert.Equal("answer", result["answer"]);
        Assert.True(result.Contains("answer_stop_text"));
        Assert.Equal("\n", result["answer_stop_text"]);
    }

    // -----------------------------------------------------------------------
    // TokenLimit / WithTemperature applied through model
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenLimit_Applied_LimitsOutput()
    {
        var lm = MockModel.Create("verylongresponse");
        var rule = GrammarFunctions.TokenLimit(GrammarFunctions.Gen("x"), 1);  // 1 token ≈ 4 chars
        var result = lm + rule;
        Assert.True(result["x"].Length <= 4);
    }

    [Fact]
    public void WithTemperature_DoesNotAffectMockOutput()
    {
        var lm = MockModel.Create("output");
        var rule = GrammarFunctions.WithTemperature(GrammarFunctions.Gen("x"), 0.5f);
        var result = lm + rule;
        Assert.Equal("output", result["x"]);
    }

    // -----------------------------------------------------------------------
    // ExactlyNRepeats / AtMostNRepeats through model (via Capture helper)
    // -----------------------------------------------------------------------

    [Fact]
    public void ExactlyNRepeats_WrappedInCapture_Works()
    {
        var lm = MockModel.Create("abc");
        var node = GrammarFunctions.Capture(
            GrammarFunctions.ExactlyNRepeats(new LiteralNode("a"), 3),
            "letters");
        var result = lm + node;
        // The MockInterpreter returns the mock response for the inner regex;
        // the capture name is exercised without error.
        Assert.True(result.Contains("letters"));
    }

    // -----------------------------------------------------------------------
    // RuleRefNode (structural only — MockInterpreter does not execute it)
    // -----------------------------------------------------------------------

    [Fact]
    public void RuleRefNode_CanBeBuiltAndLinked()
    {
        var listRef = new RuleRefNode();

        // item ::= "x" | list
        var item = new RuleNode(
            "item",
            new SelectNode(
                System.Collections.Immutable.ImmutableArray.Create<GrammarNode>(
                    new LiteralNode("x"),
                    listRef)));

        // list ::= "[" item "]"
        var list = new RuleNode(
            "list",
            new LiteralNode("[") + item + new LiteralNode("]"));

        listRef.SetTarget(list);

        Assert.Same(list, listRef.Target);
    }

    // -----------------------------------------------------------------------
    // Branching is not affected by async
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AsyncBranching_TwoBranchesDoNotAffectEachOther()
    {
        var base_ = MockModel.Create("result");
        var task1 = base_.WithAssistantAsync(
            async (m, ct) => await m.AppendAsync(GrammarFunctions.Gen("a1"), ct));
        var task2 = base_.WithAssistantAsync(
            async (m, ct) => await m.AppendAsync(GrammarFunctions.Gen("a2"), ct));

        var branch1 = await task1;
        var branch2 = await task2;

        Assert.True(branch1.Contains("a1"));
        Assert.False(branch1.Contains("a2"));
        Assert.True(branch2.Contains("a2"));
        Assert.False(branch2.Contains("a1"));

        Assert.Equal(string.Empty, base_.Text);
    }
}
