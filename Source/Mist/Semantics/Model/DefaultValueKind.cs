namespace Mist.Semantics.Model;

/// <summary>
/// Describes how a field's default value was determined.
/// </summary>
public enum DefaultValueKind
{
    /// <summary>The field's type has a zero value (e.g. 0 for int, false for bool).</summary>
    ZeroInitialized,

    /// <summary>The field has a declared initializer expression.</summary>
    FieldInitializer,

    /// <summary>An explicit default block overrides the field's value.</summary>
    ExplicitOverride,

    /// <summary>An abstract type field is bound to a concrete type.</summary>
    TypeBinding,

    /// <summary>The field's default value is inherited from a base type.</summary>
    InheritedDefault,

    /// <summary>The field's type is a nested class/struct whose default is used recursively.</summary>
    NestedTypeDefault,
}
