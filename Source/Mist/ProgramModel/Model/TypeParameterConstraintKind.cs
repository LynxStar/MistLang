namespace Mist.ProgramModel.Model;

/// <summary>
/// Enumerates the kinds of constraints that can be placed on a type parameter.
/// </summary>
public enum TypeParameterConstraintKind
{
    /// <summary>Reference type constraint (<c>class</c>).</summary>
    Class,

    /// <summary>Value type constraint (<c>struct</c>).</summary>
    Struct,

    /// <summary>Readonly constraint (<c>readonly</c>).</summary>
    Readonly,

    /// <summary>Mutable constraint (<c>mutable</c>).</summary>
    Mutable,

    /// <summary>Owned constraint (<c>owned</c>).</summary>
    Owned,

    /// <summary>Linear substructural constraint (<c>linear</c>).</summary>
    Linear,

    /// <summary>Affine substructural constraint (<c>affine</c>).</summary>
    Affine,

    /// <summary>Relevant substructural constraint (<c>relevant</c>).</summary>
    Relevant,

    /// <summary>Constructor constraint (<c>new(...)</c>).</summary>
    Constructor,

    /// <summary>Default value constraint (<c>default</c>).</summary>
    Default,

    /// <summary>Type constraint — must conform to a specified type.</summary>
    Type,
}