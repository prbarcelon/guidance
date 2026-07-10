namespace Guidance.Models;

/// <summary>
/// A single captured variable produced during LLM generation.
///
/// Corresponds to the <c>CaptureVar</c> TypedDict in
/// <c>guidance/models/_base/_state.py</c>.
/// </summary>
/// <param name="Value">The captured text.</param>
/// <param name="LogProb">
/// Log-probability of the captured text, when available from the backend.
/// </param>
public sealed record CaptureValue(string Value, double? LogProb = null);
