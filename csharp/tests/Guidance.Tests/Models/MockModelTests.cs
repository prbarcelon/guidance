using Guidance.Grammar;
using Guidance.Models;
using Xunit;

namespace Guidance.Tests.Models;

/// <summary>
/// Tests for <see cref="MockInterpreter"/> in isolation and end-to-end through
/// <see cref="MockModel"/>.
///
/// These mirror the spirit of the Python unit tests that use <c>Mock</c> to
/// validate grammar behaviour without a real LLM.
/// </summary>
public class MockModelTests
{
    // -----------------------------------------------------------------------
    // Basic generation
    // -----------------------------------------------------------------------

    [Fact]
    public void Gen_ReturnsMockResponse()
    {
        var lm = MockModel.Create("hello world");
        var result = lm + GrammarFunctions.Gen("output");
        Assert.Equal("hello world", result["output"]);
    }

    [Fact]
    public void Gen_TextIncludesGeneratedValue()
    {
        var lm = MockModel.Create("response");
        var result = lm + GrammarFunctions.Gen("x");
        Assert.Contains("response", result.Text);
    }

    [Fact]
    public void Gen_WithRegex_MatchesPattern()
    {
        // Mock response is "abc123", regex only allows digits.
        var lm = MockModel.Create("abc123");
        var result = lm + GrammarFunctions.Gen("digits", regex: @"\d+");
        // The regex \d+ should match "123" from "abc123"
        Assert.Equal("123", result["digits"]);
    }

    [Fact]
    public void Gen_WithStop_TruncatesAtStopString()
    {
        var lm = MockModel.Create("hello\nworld");
        var result = lm + GrammarFunctions.Gen("line", stop: "\n");
        Assert.Equal("hello", result["line"]);
    }

    [Fact]
    public void Gen_WithMaxTokens_TruncatesOutput()
    {
        // 1 max token ≈ 4 chars — "helloworld" should be trimmed.
        var lm = MockModel.Create("helloworld");
        var result = lm + GrammarFunctions.Gen("x", maxTokens: 1);
        Assert.True(result["x"].Length <= 4);
    }

    // -----------------------------------------------------------------------
    // Select
    // -----------------------------------------------------------------------

    [Fact]
    public void Select_PicksMatchingPrefix()
    {
        var lm = MockModel.Create("Stockholm");
        var result = lm + GrammarFunctions.Select(
            ["Helsinki", "Reykjavik", "Stockholm", "Oslo"],
            name: "capital");
        Assert.Equal("Stockholm", result["capital"]);
    }

    [Fact]
    public void Select_NoMatch_FallsBackToFirstOption()
    {
        var lm = MockModel.Create("NOMATCH");
        var result = lm + GrammarFunctions.Select(["A", "B", "C"], name: "x");
        Assert.Equal("A", result["x"]);
    }

    // -----------------------------------------------------------------------
    // JSON
    // -----------------------------------------------------------------------

    [Fact]
    public void Json_ReturnsMockResponseUnchanged()
    {
        const string jsonResponse = """{"name":"Alice","age":30}""";
        var lm = MockModel.Create(jsonResponse);
        var result = lm + GrammarFunctions.Json("data");
        Assert.Equal(jsonResponse, result["data"]);
    }

    // -----------------------------------------------------------------------
    // Role blocks
    // -----------------------------------------------------------------------

    [Fact]
    public void FullConversation_CorrectCaptureAndText()
    {
        var lm = MockModel.Create(mockResponse: "13");
        var result = lm
            .WithSystem("You are a teenager.")
            .WithUser("How old are you?")
            .WithAssistant(m => m + GrammarFunctions.Gen("age", regex: @"\d+"));

        // Verify capture.
        Assert.Equal("13", result["age"]);

        // Verify role markers in text.
        Assert.Contains("<|system|>You are a teenager.<|/system|>", result.Text);
        Assert.Contains("<|user|>How old are you?<|/user|>", result.Text);
        Assert.Contains("<|assistant|>", result.Text);
    }

    // -----------------------------------------------------------------------
    // Clone / immutability
    // -----------------------------------------------------------------------

    [Fact]
    public void Clone_IsIndependent()
    {
        var interpreter = new MockInterpreter("test");
        var clone = interpreter.Clone();

        // Mutate the original.
        interpreter.StartRole("user");
        interpreter.AppendLiteral("hello");
        interpreter.EndRole("user");

        // The clone should be unaffected.
        Assert.Equal(string.Empty, clone.CurrentText);
        Assert.Null(clone.ActiveRole);
    }

    [Fact]
    public void Model_BranchesDoNotShareState()
    {
        var base_ = MockModel.Create("captured");

        var branch1 = base_
            .WithSystem("sys1")
            .WithUser("q1")
            .WithAssistant(m => m + GrammarFunctions.Gen("a1"));

        var branch2 = base_
            .WithSystem("sys2")
            .WithUser("q2")
            .WithAssistant(m => m + GrammarFunctions.Gen("a2"));

        // Each branch has its own capture.
        Assert.True(branch1.Contains("a1"));
        Assert.False(branch1.Contains("a2"));
        Assert.True(branch2.Contains("a2"));
        Assert.False(branch2.Contains("a1"));

        // Base model is completely unchanged.
        Assert.Equal(string.Empty, base_.Text);
        Assert.Empty(base_.Captures);
    }

    // -----------------------------------------------------------------------
    // Suffix
    // -----------------------------------------------------------------------

    [Fact]
    public void Gen_WithSuffix_AppendsSuffixAfterCapture()
    {
        var lm = MockModel.Create("answer");
        var rule = GrammarFunctions.Gen("x", suffix: " [done]");
        var result = lm + rule;

        Assert.Equal("answer", result["x"]);
        Assert.Contains("answer [done]", result.Text);
    }
}
