namespace Mist.Semantics.Model;

/// <summary>
/// The declared accessibility of a Mist declaration (classic pipeline).
/// </summary>
public enum Accessibility
{
    /// <summary>Visible to all code.</summary>
    Public,

    /// <summary>Visible only within the declaring type.</summary>
    Private,

    /// <summary>Visible to the declaring type and derived types.</summary>
    Protected,

    /// <summary>Visible within the same compilation (assembly).</summary>
    Internal,
}