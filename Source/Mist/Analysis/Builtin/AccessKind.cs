namespace Mist.Analysis.Builtin;

/// <summary>
/// Classifies the kind of access being attempted on an owned variable.
/// Used to look up permissions in the truth table that maps
/// (VariableOwnershipState, AccessKind) pairs to permit/deny decisions.
/// </summary>
public enum AccessKind
{
    /// <summary>Reading the value of the variable.</summary>
    Read,

    /// <summary>Writing (mutating) the value of the variable.</summary>
    Write,

    /// <summary>Lending the variable as a readonly ref (live view).</summary>
    LiveView,

    /// <summary>Lending the variable as a frozen ref.</summary>
    Freeze,

    /// <summary>Lending the variable as a borrowed ref (exclusive mutable access).</summary>
    Borrow,

    /// <summary>Moving the value to another binding, making the source dead.</summary>
    Move,
}
