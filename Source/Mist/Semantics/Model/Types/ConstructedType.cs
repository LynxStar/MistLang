using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents a generic type with all type parameters substituted by concrete type arguments
/// (e.g., <c>Box&lt;int&gt;</c>, <c>Map&lt;string, int&gt;</c>).
/// </summary>
public sealed class ConstructedType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind { get; }

    /// <summary>
    /// The unparameterized generic type definition (ClassType, StructType, or AbstractType).
    /// </summary>
    public IType GenericDefinition { get; }

    /// <summary>
    /// The concrete type arguments substituted for the type parameters.
    /// </summary>
    public IReadOnlyList<IType> TypeArguments { get; }

    /// <summary>
    /// Dependent value arguments, indexed by position in the type parameter list.
    /// Null entries indicate regular type arguments at that position.
    /// Null if the type has no dependent parameters.
    /// </summary>
    public IReadOnlyList<DependentValueArgument?>? DependentArguments { get; }

    /// <summary>
    /// The member scope of the generic definition (members are resolved with substitution).
    /// </summary>
    // TypeKind audit (unknown-type feature): the default throw remains correct after TypeKind.Unknown
    // is introduced because UnknownType is never the generic definition of a ConstructedType —
    // `unknown` has no type parameters and cannot be constructed (see Requirement 5.4/7).
    public IScope MemberScope => GenericDefinition switch
    {
        ClassType c => c.MemberScope,
        StructType s => s.MemberScope,
        AbstractType a => a.MemberScope,
        _ => throw new InvalidOperationException(
            $"ConstructedType.GenericDefinition must be a ClassType, StructType, or AbstractType, but was {GenericDefinition.GetType().Name}.")
    };

    public ConstructedType(
        IType genericDefinition,
        IReadOnlyList<IType> typeArguments,
        IReadOnlyList<DependentValueArgument?>? dependentArguments = null)
    {
        GenericDefinition = genericDefinition;
        TypeArguments = typeArguments;
        DependentArguments = dependentArguments;
        Kind = genericDefinition.Kind;

        // Build display name: "Box<int>" or "Map<string, int>" or "FixedBuffer<5>"
        // When a position has a dependent value argument, display the value instead of the placeholder type.
        var argParts = new string[typeArguments.Count];
        for (var i = 0; i < typeArguments.Count; i++)
        {
            if (dependentArguments is not null && i < dependentArguments.Count && dependentArguments[i] is { } depArg)
                argParts[i] = depArg.DisplayValue;
            else
                argParts[i] = typeArguments[i].DisplayName;
        }
        DisplayName = $"{genericDefinition.DisplayName}<{string.Join(", ", argParts)}>";
    }
}
