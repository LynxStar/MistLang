namespace Mist.Analysis.Builtin;

/// <summary>
/// Tracks the drop state of a variable during flow-sensitive analysis.
/// </summary>
public enum VariableDropState
{
    /// <summary>Variable is live — not yet dropped, moved, or returned.</summary>
    Live,

    /// <summary>Variable has been explicitly dropped (Drop() called).</summary>
    Dropped,

    /// <summary>Variable has been moved (ownership transferred).</summary>
    Moved,

    /// <summary>Variable has been returned from the method.</summary>
    Returned,
}