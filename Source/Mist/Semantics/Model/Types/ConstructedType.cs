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
    /// The unparameterized generic type definition (ClassType, StructType, or InterfaceType).
    /// </summary>
    public IType GenericDefinition { get; }

    /// <summary>
    /// The concrete type arguments substituted for the type parameters.
    /// </summary>
    public IReadOnlyList<IType> TypeArguments { get; }

    /// <summary>
    /// The member scope of the generic definition (members are resolved with substitution).
    /// </summary>
    public IScope MemberScope => GenericDefinition switch
    {
        ClassType c => c.MemberScope,
        StructType s => s.MemberScope,
        InterfaceType i => i.MemberScope,
        _ => throw new InvalidOperationException(
            $"ConstructedType.GenericDefinition must be a ClassType, StructType, or InterfaceType, but was {GenericDefinition.GetType().Name}.")
    };

    public ConstructedType(IType genericDefinition, IReadOnlyList<IType> typeArguments)
    {
        GenericDefinition = genericDefinition;
        TypeArguments = typeArguments;
        Kind = genericDefinition.Kind;

        // Build display name: "Box<int>" or "Map<string, int>"
        var argNames = string.Join(", ", typeArguments.Select(a => a.DisplayName));
        DisplayName = $"{genericDefinition.DisplayName}<{argNames}>";
    }
}
