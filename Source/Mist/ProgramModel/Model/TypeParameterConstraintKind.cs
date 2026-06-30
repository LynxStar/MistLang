namespace Mist.ProgramModel.Model;

/// <summary>
/// Constraint kinds for type parameters.
/// </summary>
public enum TypeParameterConstraintKind
{
    /// <summary>Must derive from or be a specific type.</summary>
    BaseType,

    /// <summary>Must implement a specific abstract type.</summary>
    AbstractType,

    /// <summary>Must be a value type (struct).</summary>
    Struct,

    /// <summary>Must be a reference type (class).</summary>
    Class,

    /// <summary>Must have a parameterless constructor / be defaultable.</summary>
    Defaultable,
}