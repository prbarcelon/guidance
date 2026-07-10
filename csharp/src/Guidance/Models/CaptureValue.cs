namespace Guidance.Models;

/// <summary>
/// A single captured variable (or list of captures when <c>listAppend = true</c>)
/// produced during LLM generation.
///
/// For scalar captures, <see cref="Values"/> contains exactly one entry and
/// <see cref="Value"/> is a convenience accessor for that entry.
/// For list captures (produced when <c>listAppend = true</c> is passed to
/// <c>Gen</c> or <c>Select</c>), <see cref="Values"/> holds all appended entries
/// and <see cref="Value"/> returns the most recently appended one.
///
/// Corresponds to the <c>CaptureVar</c> TypedDict / list-of-<c>CaptureVar</c>
/// pattern in <c>guidance/models/_base/_state.py</c>.
/// </summary>
public sealed class CaptureValue
{
    private readonly List<string> _values;

    /// <summary>
    /// Creates a scalar capture with a single <paramref name="value"/>.
    /// </summary>
    public CaptureValue(string value, double? logProb = null)
    {
        _values = [value];
        LogProb = logProb;
    }

    // Private constructor used by Append().
    private CaptureValue(List<string> values, double? logProb)
    {
        _values = values;
        LogProb = logProb;
    }

    /// <summary>
    /// The most recently captured value (or the single value for scalar captures).
    /// </summary>
    public string Value => _values[^1];

    /// <summary>
    /// All captured values in order of capture.  Contains exactly one element for
    /// scalar captures; multiple elements when <c>listAppend = true</c> was used.
    /// </summary>
    public IReadOnlyList<string> Values => _values;

    /// <summary>
    /// Log-probability of the most recent captured value, when available from the backend.
    /// </summary>
    public double? LogProb { get; }

    /// <summary>
    /// Returns a new <see cref="CaptureValue"/> with <paramref name="value"/> appended
    /// to the list.  Used internally to implement list-append captures.
    /// </summary>
    internal CaptureValue Append(string value, double? logProb = null)
    {
        var newValues = new List<string>(_values) { value };
        return new CaptureValue(newValues, logProb);
    }
}

