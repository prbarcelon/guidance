using Guidance.Grammar;

namespace Guidance.Models;

/// <summary>
/// An interpreter runs grammar nodes against a specific LLM backend, updating
/// an internal mutable state (current text, captures, active role).
///
/// Implementations include <see cref="MockInterpreter"/> (for offline testing)
/// and <see cref="Guidance.Adapters.OpenAI.OpenAIInterpreter"/>.
///
/// Corresponds to <c>Interpreter[S]</c> in
/// <c>guidance/models/_base/_interpreter.py</c>.
/// </summary>
public interface IInterpreter
{
    // -----------------------------------------------------------------------
    // State accessors (read-only from the Model's perspective)
    // -----------------------------------------------------------------------

    /// <summary>
    /// The current text representation of the model state (the "prompt so far").
    /// </summary>
    string CurrentText { get; }

    /// <summary>All variables captured so far during this conversation.</summary>
    IReadOnlyDictionary<string, CaptureValue> Captures { get; }

    /// <summary>
    /// The name of the currently open role block (e.g. <c>"system"</c>,
    /// <c>"user"</c>, <c>"assistant"</c>), or <c>null</c> when no role is active.
    /// </summary>
    string? ActiveRole { get; }

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a deep copy of this interpreter, preserving all accumulated state.
    /// Used by <see cref="Model"/> to implement copy-on-write semantics.
    /// </summary>
    IInterpreter Clone();

    // -----------------------------------------------------------------------
    // Node execution
    // -----------------------------------------------------------------------

    /// <summary>
    /// Appends an exact string to the current role's content.
    /// For chat-style backends this adds to the pending message; for completion
    /// backends it extends the prompt.
    /// </summary>
    void AppendLiteral(string text);

    /// <summary>
    /// Applies a <see cref="RuleNode"/> (e.g. from <c>Gen</c>, <c>Select</c>,
    /// <c>Json</c>) to the current state.
    ///
    /// For LLM-backed interpreters this may trigger a network call.
    /// Captured output (if any) is stored in <see cref="Captures"/> and the
    /// generated text is appended to <see cref="CurrentText"/>.
    /// </summary>
    void ApplyRule(RuleNode rule);

    // -----------------------------------------------------------------------
    // Role management
    // -----------------------------------------------------------------------

    /// <summary>
    /// Opens a role block.  Implementations must reject nested role opens
    /// (i.e. calling this while <see cref="ActiveRole"/> is non-<c>null</c>).
    /// </summary>
    /// <param name="role">Role name (e.g. <c>"system"</c>, <c>"user"</c>, <c>"assistant"</c>).</param>
    void StartRole(string role);

    /// <summary>
    /// Closes the currently open role block.
    /// </summary>
    /// <param name="role">
    /// Must match the value passed to the most recent <see cref="StartRole"/> call.
    /// </param>
    void EndRole(string role);
}
