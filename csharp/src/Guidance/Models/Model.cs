using Guidance.Grammar;

namespace Guidance.Models;

/// <summary>
/// An immutable model object that wraps an <see cref="IInterpreter"/> and exposes
/// a fluent API for building prompts and generating text.
///
/// <b>Immutability:</b> every operation (<c>Append</c>, <c>WithSystem</c>, …)
/// returns a <em>new</em> <see cref="Model"/> instance; the original is unchanged.
/// This allows multiple independent "branches" from the same state:
/// <code>
/// var base = lm.WithSystem("You are helpful").WithUser("Hello");
/// var branch1 = base.WithAssistant(m => m + Gen("a", maxTokens: 10));
/// var branch2 = base.WithAssistant(m => m + Gen("b", maxTokens: 50));
/// </code>
///
/// Corresponds to <c>Model</c> in <c>guidance/models/_base/_model.py</c>.
/// </summary>
public sealed class Model
{
    // The interpreter is mutable; we deep-copy it on each branch point.
    private readonly IInterpreter _interpreter;

    private Model(IInterpreter interpreter)
    {
        _interpreter = interpreter;
    }

    // -----------------------------------------------------------------------
    // Factory
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="Model"/> from an existing <see cref="IInterpreter"/>.
    /// The interpreter is cloned so the caller's copy is not affected.
    /// </summary>
    public static Model From(IInterpreter interpreter) => new(interpreter.Clone());

    // -----------------------------------------------------------------------
    // State accessors
    // -----------------------------------------------------------------------

    /// <summary>The current text representation of the model state.</summary>
    public string Text => _interpreter.CurrentText;

    /// <summary>All variables captured so far during this conversation.</summary>
    public IReadOnlyDictionary<string, CaptureValue> Captures => _interpreter.Captures;

    /// <summary>
    /// Gets a captured value by name.
    /// </summary>
    /// <exception cref="KeyNotFoundException">If the variable has not been captured yet.</exception>
    public string this[string name]
    {
        get
        {
            if (!_interpreter.Captures.TryGetValue(name, out var capture))
                throw new KeyNotFoundException($"Model does not contain variable '{name}'.");
            return capture.Value;
        }
    }

    /// <summary>Returns <c>true</c> if the model contains a capture with the given name.</summary>
    public bool Contains(string name) => _interpreter.Captures.ContainsKey(name);

    /// <summary>
    /// Returns the captured value for <paramref name="name"/>,
    /// or <paramref name="defaultValue"/> when not present.
    /// </summary>
    public string? Get(string name, string? defaultValue = null)
        => _interpreter.Captures.TryGetValue(name, out var cap) ? cap.Value : defaultValue;

    /// <inheritdoc/>
    public override string ToString() => _interpreter.CurrentText;

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a new Model backed by a deep copy of the current interpreter.
    /// All subsequent mutations operate on the copy, leaving <c>this</c> unchanged.
    /// </summary>
    private Model Copy() => new(_interpreter.Clone());

    /// <summary>
    /// Recursively walks <paramref name="node"/>, dispatching each leaf to the
    /// interpreter.  This method mutates <c>this._interpreter</c> directly and
    /// is only ever called on a freshly cloned Model.
    /// </summary>
    private void ApplyNodeMutating(GrammarNode node)
    {
        switch (node)
        {
            case LiteralNode { IsNull: false } literal:
                _interpreter.AppendLiteral(literal.Value);
                break;

            case LiteralNode:
                // Empty literal — nothing to do.
                break;

            case RuleNode rule:
                _interpreter.ApplyRule(rule);
                break;

            case JoinNode join:
                foreach (var child in join.Children)
                    ApplyNodeMutating(child);
                break;

            default:
                // Wrap bare primitives in a RuleNode so the interpreter can handle them.
                throw new NotSupportedException(
                    $"Top-level grammar node of type '{node.GetType().Name}' cannot be " +
                    $"applied directly.  Wrap it with a RuleNode via GrammarFunctions.");
        }
    }

    /// <summary>
    /// Async version of <see cref="ApplyNodeMutating"/> — uses
    /// <see cref="IAsyncInterpreter"/> when available, otherwise falls back to
    /// the synchronous path.
    /// </summary>
    private async Task ApplyNodeMutatingAsync(
        GrammarNode node,
        CancellationToken cancellationToken)
    {
        var asyncInterpreter = _interpreter as IAsyncInterpreter;

        switch (node)
        {
            case LiteralNode { IsNull: false } literal:
                if (asyncInterpreter is not null)
                    await asyncInterpreter.AppendLiteralAsync(literal.Value, cancellationToken);
                else
                    _interpreter.AppendLiteral(literal.Value);
                break;

            case LiteralNode:
                break;

            case RuleNode rule:
                if (asyncInterpreter is not null)
                    await asyncInterpreter.ApplyRuleAsync(rule, cancellationToken);
                else
                    _interpreter.ApplyRule(rule);
                break;

            case JoinNode join:
                foreach (var child in join.Children)
                    await ApplyNodeMutatingAsync(child, cancellationToken);
                break;

            default:
                throw new NotSupportedException(
                    $"Top-level grammar node of type '{node.GetType().Name}' cannot be " +
                    $"applied directly.  Wrap it with a RuleNode via GrammarFunctions.");
        }
    }

    // -----------------------------------------------------------------------
    // Public append operations (synchronous)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns a new model that has <paramref name="node"/> applied to the state.
    /// </summary>
    public Model Append(GrammarNode node)
    {
        var m = Copy();
        m.ApplyNodeMutating(node);
        return m;
    }

    /// <summary>Returns a new model with the string literal appended.</summary>
    public Model Append(string text)
        => string.IsNullOrEmpty(text) ? this : Append(new LiteralNode(text));

    /// <summary>Appends a grammar node to the model.</summary>
    public static Model operator +(Model model, GrammarNode grammar) => model.Append(grammar);

    /// <summary>Appends a string literal to the model.</summary>
    public static Model operator +(Model model, string text) => model.Append(text);

    // -----------------------------------------------------------------------
    // Public append operations (asynchronous)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Asynchronously returns a new model that has <paramref name="node"/> applied
    /// to the state.  Uses <see cref="IAsyncInterpreter"/> when available.
    /// </summary>
    public async Task<Model> AppendAsync(
        GrammarNode node,
        CancellationToken cancellationToken = default)
    {
        var m = Copy();
        await m.ApplyNodeMutatingAsync(node, cancellationToken);
        return m;
    }

    /// <summary>
    /// Asynchronously returns a new model with the string literal appended.
    /// </summary>
    public Task<Model> AppendAsync(
        string text,
        CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(text)
            ? Task.FromResult(this)
            : AppendAsync(new LiteralNode(text), cancellationToken);

    // -----------------------------------------------------------------------
    // Role / block helpers (synchronous)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Runs <paramref name="configure"/> inside a named role block.
    ///
    /// This is the C# equivalent of the Python <c>with system():</c> /
    /// <c>with user():</c> / <c>with assistant():</c> context managers.
    ///
    /// The role is opened on a fresh copy of the current state, the
    /// <paramref name="configure"/> callback builds content within it, then
    /// the role is closed on the final state before returning.
    /// </summary>
    /// <param name="role">Role name — typically <c>"system"</c>, <c>"user"</c>,
    /// or <c>"assistant"</c>.</param>
    /// <param name="configure">
    /// A function that receives the model (with the role already open) and
    /// returns the model after adding content.
    /// </param>
    public Model WithRole(string role, Func<Model, Model> configure)
    {
        // 1. Clone and open the role.
        var start = Copy();
        start._interpreter.StartRole(role);

        // 2. Let the caller add content.  Each append inside configure will
        //    create further copies that inherit the "role open" state.
        var result = configure(start);

        // 3. Close the role on a final copy so that `result` remains immutable.
        var final = result.Copy();
        final._interpreter.EndRole(role);
        return final;
    }

    /// <summary>Adds a system message with static text content.</summary>
    public Model WithSystem(string text) => WithRole("system", m => m + text);

    /// <summary>Adds a system message built by <paramref name="configure"/>.</summary>
    public Model WithSystem(Func<Model, Model> configure) => WithRole("system", configure);

    /// <summary>Adds a user message with static text content.</summary>
    public Model WithUser(string text) => WithRole("user", m => m + text);

    /// <summary>Adds a user message built by <paramref name="configure"/>.</summary>
    public Model WithUser(Func<Model, Model> configure) => WithRole("user", configure);

    /// <summary>
    /// Adds an assistant message, potentially triggering LLM generation when
    /// the <paramref name="configure"/> callback applies grammar nodes.
    /// </summary>
    public Model WithAssistant(string text) => WithRole("assistant", m => m + text);

    /// <summary>
    /// Adds an assistant message built by <paramref name="configure"/>, potentially
    /// triggering LLM generation.
    /// </summary>
    public Model WithAssistant(Func<Model, Model> configure) => WithRole("assistant", configure);

    // -----------------------------------------------------------------------
    // Role / block helpers (asynchronous)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Asynchronously runs <paramref name="configure"/> inside a named role block.
    /// The callback receives the model (with the role already open) and must return
    /// a <see cref="Task{Model}"/> that completes after all content has been appended.
    /// </summary>
    public async Task<Model> WithRoleAsync(
        string role,
        Func<Model, CancellationToken, Task<Model>> configure,
        CancellationToken cancellationToken = default)
    {
        var start = Copy();
        start._interpreter.StartRole(role);

        var result = await configure(start, cancellationToken);

        var final = result.Copy();
        final._interpreter.EndRole(role);
        return final;
    }

    /// <summary>
    /// Asynchronously adds a system message built by <paramref name="configure"/>.
    /// </summary>
    public Task<Model> WithSystemAsync(
        Func<Model, CancellationToken, Task<Model>> configure,
        CancellationToken cancellationToken = default)
        => WithRoleAsync("system", configure, cancellationToken);

    /// <summary>Asynchronously adds a system message with static text content.</summary>
    public Task<Model> WithSystemAsync(
        string text,
        CancellationToken cancellationToken = default)
        => WithRoleAsync("system",
            (m, ct) => m.AppendAsync(text, ct),
            cancellationToken);

    /// <summary>
    /// Asynchronously adds a user message built by <paramref name="configure"/>.
    /// </summary>
    public Task<Model> WithUserAsync(
        Func<Model, CancellationToken, Task<Model>> configure,
        CancellationToken cancellationToken = default)
        => WithRoleAsync("user", configure, cancellationToken);

    /// <summary>Asynchronously adds a user message with static text content.</summary>
    public Task<Model> WithUserAsync(
        string text,
        CancellationToken cancellationToken = default)
        => WithRoleAsync("user",
            (m, ct) => m.AppendAsync(text, ct),
            cancellationToken);

    /// <summary>
    /// Asynchronously adds an assistant message built by <paramref name="configure"/>,
    /// potentially triggering LLM generation.
    /// </summary>
    public Task<Model> WithAssistantAsync(
        Func<Model, CancellationToken, Task<Model>> configure,
        CancellationToken cancellationToken = default)
        => WithRoleAsync("assistant", configure, cancellationToken);

    /// <summary>Asynchronously adds a static assistant message.</summary>
    public Task<Model> WithAssistantAsync(
        string text,
        CancellationToken cancellationToken = default)
        => WithRoleAsync("assistant",
            (m, ct) => m.AppendAsync(text, ct),
            cancellationToken);
}

/// <summary>
/// An immutable model object that wraps an <see cref="IInterpreter"/> and exposes
/// a fluent API for building prompts and generating text.
///
/// <b>Immutability:</b> every operation (<c>Append</c>, <c>WithSystem</c>, …)
/// returns a <em>new</em> <see cref="Model"/> instance; the original is unchanged.
/// This allows multiple independent "branches" from the same state:
/// <code>
/// var base = lm.WithSystem("You are helpful").WithUser("Hello");
/// var branch1 = base.WithAssistant(m => m + Gen("a", maxTokens: 10));
/// var branch2 = base.WithAssistant(m => m + Gen("b", maxTokens: 50));
/// </code>
///
/// Corresponds to <c>Model</c> in <c>guidance/models/_base/_model.py</c>.
