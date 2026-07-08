using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A type parameter declared on a generic type. This is itself a type — it can
/// appear in base clauses, member signatures, and anywhere a type reference resolves.
/// Lives in its declaring type's scope; does not participate in namespace registration.
/// </summary>
internal sealed class TypeParameter : MistType, ITypeParameter
{
    public TypeParameter(
        string name,
        int ordinal,
        TypeParameterKind kind,
        MistType? valueType,
        Scope declaringScope,
        MistType declaringType)
        : base(name, declaringType.NamespaceName, declaringScope, declaringType)
    {
        Ordinal = ordinal;
        Kind = kind;
        DependentValueType = valueType;
    }

    /// <summary>
    /// The zero-based positional index within the declaring type's parameter list.
    /// </summary>
    public int Ordinal { get; }

    /// <summary>
    /// Whether this is a regular (type-carrying) or dependent (value-carrying) parameter.
    /// </summary>
    public TypeParameterKind Kind { get; }

    /// <summary>
    /// For dependent type parameters, the resolved value type. Null for regular parameters.
    /// </summary>
    public MistType? DependentValueType { get; }

    IMistType? ITypeParameter.ValueType => DependentValueType;
}