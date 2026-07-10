using Guidance.Grammar;

namespace Guidance.Models;

/// <summary>
/// Extends <see cref="IInterpreter"/> with asynchronous counterparts for the
/// operations that may involve network I/O (literal appends and rule execution).
///
/// Implementations that talk to remote APIs (e.g.
/// <see cref="Guidance.Adapters.OpenAI.OpenAIInterpreter"/>) should prefer the
/// async path to avoid blocking threads.
///
/// Corresponds to the async variant of
/// <c>Interpreter[S]</c> in <c>guidance/models/_base/_interpreter.py</c>.
/// </summary>
public interface IAsyncInterpreter : IInterpreter
{
    /// <summary>
    /// Asynchronous counterpart of <see cref="IInterpreter.AppendLiteral"/>.
    /// </summary>
    Task AppendLiteralAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronous counterpart of <see cref="IInterpreter.ApplyRule"/>.
    /// For LLM-backed interpreters this performs the network call asynchronously.
    /// </summary>
    Task ApplyRuleAsync(RuleNode rule, CancellationToken cancellationToken = default);
}
