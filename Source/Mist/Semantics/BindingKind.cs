namespace Mist.Semantics;

/// <summary>
/// Tracks whether a variable is an owned binding or unrestricted binding.
/// Used to determine which access rules apply.
/// </summary>
public enum BindingKind
{
    /// <summary>Variable declared with 'owned' specifier.</summary>
    Owned,

    /// <summary>Variable without ownership annotation.</summary>
    Unrestricted,
}
