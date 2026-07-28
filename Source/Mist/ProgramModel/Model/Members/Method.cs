using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// A method declared on a type.
/// </summary>
internal class Method(
    string name,
    IMistType returnType,
    List<Parameter> parameters,
    MemberModifiers modifiers) : IMethod, IHasAttributes
{
    public string Name { get; } = name;

    public IMistType ReturnType { get; } = returnType;

    public List<Parameter> ParametersList { get; } = parameters;

    public MemberModifiers Modifiers { get; } = modifiers;

    /// <summary>
    /// The attributes applied to this method.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    /// <summary>
    /// The type parameters declared on this method. Empty for non-generic methods.
    /// </summary>
    public List<TypeParameter> TypeParametersList { get; } = [];

    /// <summary>
    /// Context bindings from <c>with Type name</c> clauses. Populated in Phase 2.
    /// </summary>
    public List<ContextBinding> ContextBindingsList { get; } = [];

    /// <summary>
    /// The ownership tier required on 'this', declared via a <c>with {tier}</c> clause.
    /// Null if no this-tier clause is present.
    /// </summary>
    public ThisTier? DeclaredThisTier { get; internal set; }

    /// <summary>
    /// The method's own scope. Contains method-level type parameters (if generic)
    /// and will hold locals during expression resolution. Parent is the declaring
    /// type's scope.
    /// </summary>
    public Scope Scope { get; init; } = null!;

    /// <summary>
    /// The method body, or null if the method has no body (abstract or intrinsic).
    /// The statements are filled in during Phase 3 (body binding).
    /// </summary>
    public Block? Body { get; init; }

    /// <summary>
    /// The ownership specifier on the return type, if any.
    /// </summary>
    public OwnershipKind ReturnOwnership { get; init; }

    IReadOnlyList<ITypeParameter> IMethod.TypeParameters => TypeParametersList;
    IReadOnlyList<IContextBinding> IMethod.ContextBindings => ContextBindingsList;
    IReadOnlyList<IParameter> IMethod.Parameters => ParametersList;
    ThisTier? IMethod.DeclaredThisTier => DeclaredThisTier;
    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}