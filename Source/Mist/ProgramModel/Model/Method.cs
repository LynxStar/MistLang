using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A method declared on a type.
/// </summary>
internal class Method(
    string name,
    IMistType returnType,
    IReadOnlyList<IParameter> parameters,
    MemberModifiers modifiers) : IMethod
{
    public string Name { get; } = name;

    public IMistType ReturnType { get; } = returnType;

    public IReadOnlyList<IParameter> Parameters { get; } = parameters;

    public MemberModifiers Modifiers { get; } = modifiers;

    /// <summary>
    /// The type parameters declared on this method. Empty for non-generic methods.
    /// </summary>
    public List<TypeParameter> TypeParametersList { get; } = [];

    /// <summary>
    /// The method's own scope. Contains method-level type parameters (if generic)
    /// and will hold locals during expression resolution. Parent is the declaring
    /// type's scope.
    /// </summary>
    public Scope Scope { get; init; } = null!;

    /// <summary>
    /// The method body, or null if the method has no body (abstract or intrinsic).
    /// </summary>
    public Block? Body { get; init; }

    /// <summary>
    /// The ownership specifier on the return type, if any.
    /// </summary>
    public OwnershipKind ReturnOwnership { get; init; }

    IReadOnlyList<ITypeParameter> IMethod.TypeParameters => TypeParametersList;
}