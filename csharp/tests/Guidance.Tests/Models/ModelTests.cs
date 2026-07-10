using Guidance.Grammar;
using Guidance.Models;
using Xunit;

namespace Guidance.Tests.Models;

/// <summary>
/// Unit tests for the immutable <see cref="Model"/> class:
/// copy-on-write semantics, operator overloads, captures, role helpers.
///
/// All tests use <see cref="MockInterpreter"/> so no network is required.
/// </summary>
public class ModelTests
{
    // -----------------------------------------------------------------------
    // Factory / basic construction
    // -----------------------------------------------------------------------

    [Fact]
    public void From_SetsEmptyTextAndCaptures()
    {
        var lm = MockModel.Create();
        Assert.Equal(string.Empty, lm.Text);
        Assert.Empty(lm.Captures);
    }

    // -----------------------------------------------------------------------
    // Append (operator +)
    // -----------------------------------------------------------------------

    [Fact]
    public void AppendString_DoesNotMutateOriginal()
    {
        var original = MockModel.Create();
        var updated = original + "hello";

        Assert.Equal(string.Empty, original.Text);
        Assert.Equal("hello", updated.Text);
    }

    [Fact]
    public void AppendString_ReturnsNewInstance()
    {
        var original = MockModel.Create();
        var updated = original + "hello";
        Assert.NotSame(original, updated);
    }

    [Fact]
    public void AppendEmptyString_ReturnsSameInstance()
    {
        var lm = MockModel.Create();
        var result = lm + string.Empty;
        Assert.Same(lm, result);
    }

    [Fact]
    public void AppendLiteralNode_AppendsText()
    {
        var lm = MockModel.Create();
        var result = lm + new LiteralNode("world");
        Assert.Equal("world", result.Text);
    }

    [Fact]
    public void AppendChained_BuildsText()
    {
        var lm = MockModel.Create();
        var result = lm + "foo" + "bar" + "baz";
        Assert.Equal("foobarbaz", result.Text);
    }

    [Fact]
    public void AppendJoinNode_AppendsAllChildren()
    {
        var lm = MockModel.Create();
        var joined = new LiteralNode("a") + new LiteralNode("b");
        var result = lm + joined;
        Assert.Equal("ab", result.Text);
    }

    // -----------------------------------------------------------------------
    // Branching (immutable copy-on-write)
    // -----------------------------------------------------------------------

    [Fact]
    public void Branching_TwoBranchesDoNotAffectEachOther()
    {
        var base_ = MockModel.Create() + "base ";
        var branch1 = base_ + "one";
        var branch2 = base_ + "two";

        Assert.Equal("base one", branch1.Text);
        Assert.Equal("base two", branch2.Text);
        Assert.Equal("base ", base_.Text);
    }

    // -----------------------------------------------------------------------
    // Captures
    // -----------------------------------------------------------------------

    [Fact]
    public void Capture_IsAccessibleViaIndexer()
    {
        var lm = MockModel.Create(mockResponse: "Paris");
        var result = lm + GrammarFunctions.Gen("city", maxTokens: 5);
        Assert.Equal("Paris", result["city"]);
    }

    [Fact]
    public void Capture_Missing_ThrowsKeyNotFoundException()
    {
        var lm = MockModel.Create();
        Assert.Throws<KeyNotFoundException>(() => _ = lm["missing"]);
    }

    [Fact]
    public void Contains_ReturnsTrueWhenCaptureExists()
    {
        var lm = MockModel.Create(mockResponse: "yes");
        var result = lm + GrammarFunctions.Gen("flag");
        Assert.True(result.Contains("flag"));
    }

    [Fact]
    public void Contains_ReturnsFalseWhenCaptureAbsent()
    {
        var lm = MockModel.Create();
        Assert.False(lm.Contains("nope"));
    }

    [Fact]
    public void Get_ReturnsDefaultWhenCaptureAbsent()
    {
        var lm = MockModel.Create();
        Assert.Equal("default", lm.Get("missing", "default"));
    }

    [Fact]
    public void Get_ReturnsValueWhenCapturePresent()
    {
        var lm = MockModel.Create(mockResponse: "hello");
        var result = lm + GrammarFunctions.Gen("x");
        Assert.Equal("hello", result.Get("x"));
    }

    // -----------------------------------------------------------------------
    // Role helpers
    // -----------------------------------------------------------------------

    [Fact]
    public void WithSystem_AddsOpenAndCloseMarkers()
    {
        var lm = MockModel.Create();
        var result = lm.WithSystem("You are helpful.");
        Assert.Contains("<|system|>", result.Text);
        Assert.Contains("You are helpful.", result.Text);
        Assert.Contains("<|/system|>", result.Text);
    }

    [Fact]
    public void WithUser_AddsUserMarkers()
    {
        var lm = MockModel.Create();
        var result = lm.WithUser("Hello!");
        Assert.Contains("<|user|>", result.Text);
        Assert.Contains("Hello!", result.Text);
        Assert.Contains("<|/user|>", result.Text);
    }

    [Fact]
    public void WithAssistant_AddsAssistantMarkers()
    {
        var lm = MockModel.Create();
        var result = lm.WithAssistant("Hi there.");
        Assert.Contains("<|assistant|>", result.Text);
        Assert.Contains("Hi there.", result.Text);
        Assert.Contains("<|/assistant|>", result.Text);
    }

    [Fact]
    public void WithRole_ChainedRoles_BuildsFullConversation()
    {
        var lm = MockModel.Create(mockResponse: "42");
        var result = lm
            .WithSystem("You are a maths tutor.")
            .WithUser("What is 6 × 7?")
            .WithAssistant(m => m + GrammarFunctions.Gen("answer", maxTokens: 5));

        Assert.Contains("<|system|>", result.Text);
        Assert.Contains("<|user|>", result.Text);
        Assert.Contains("<|assistant|>", result.Text);
        Assert.Equal("42", result["answer"]);
    }

    [Fact]
    public void WithRole_DoesNotMutateCallerModel()
    {
        var original = MockModel.Create();
        _ = original.WithSystem("test");
        Assert.Equal(string.Empty, original.Text);
    }

    [Fact]
    public void WithRole_NestedRole_Throws()
    {
        var lm = MockModel.Create();
        Assert.Throws<InvalidOperationException>(() =>
            lm.WithSystem(m => m.WithUser("nested")));
    }

    // -----------------------------------------------------------------------
    // Select
    // -----------------------------------------------------------------------

    [Fact]
    public void Select_ReturnsFirstMatchingOption()
    {
        // Mock response starts with "B" → should choose "B".
        var lm = MockModel.Create(mockResponse: "B");
        var result = lm + GrammarFunctions.Select(["A", "B", "C"], name: "choice");
        Assert.Equal("B", result["choice"]);
    }

    [Fact]
    public void Select_NoMatch_ReturnsFirstOption()
    {
        // Mock response is something that doesn't match any option → first option.
        var lm = MockModel.Create(mockResponse: "zzz");
        var result = lm + GrammarFunctions.Select(["A", "B", "C"], name: "choice");
        Assert.Equal("A", result["choice"]);
    }

    // -----------------------------------------------------------------------
    // ToString
    // -----------------------------------------------------------------------

    [Fact]
    public void ToString_EqualsText()
    {
        var lm = MockModel.Create() + "hello";
        Assert.Equal(lm.Text, lm.ToString());
    }
}
