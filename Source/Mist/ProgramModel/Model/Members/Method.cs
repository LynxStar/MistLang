using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Members;

internal class Method(
    string name,
    IMistType returnType,
    List<Parameter> parameters,
    MemberModifiers modifiers) : IMethod, IHasTypeParameters, IHasAttributes, IOverload
{
    public string Name { get; } = name;

    public IMistType ReturnType { get; } = returnType;

    public List<Parameter> ParametersList { get; } = parameters;

    public MemberModifiers Modifiers { get; } = modifiers;

    public SequenceLengthOperation SequenceLengthOperation { get; init; }

    public bool IsTopLevelProgram { get; init; }

    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    public List<TypeParameter> TypeParametersList { get; } = [];

    public List<ContextBinding> ContextBindingsList { get; } = [];

    public ThisTier? DeclaredThisTier { get; internal set; }

    /// <summary>
    /// Holds the method's type parameters; its parent is the declaring type's scope.
    /// </summary>
    public Scope Scope { get; init; } = null!;

    /// <summary>
    /// Null for an abstract or intrinsic method.
    /// </summary>
    public Block? Body { get; init; }

    /// <summary>
    /// A projected interface member whose CLR declaration carries its own body, so a conformer may leave it
    /// to the native default. Kept apart from <see cref="Body"/>, which only Mist source fills and nothing
    /// may fabricate for a member the compiler never reads the source of.
    /// </summary>
    public bool HasNativeBody { get; init; }

    public NativeSlotKind SlotKind { get; init; }

    public OwnershipKind ReturnOwnership { get; init; }

    /// <summary>
    /// The type that declares this member, and where it was written -- what a rule needs to judge the
    /// member as a declaration, since neither the scope chain nor the body says. Both null on a
    /// projected or synthesized member, which has no Mist source to report against.
    /// </summary>
    public MemberType? DeclaringType { get; init; }

    public Storm.Runtime.SyntaxNode? DeclarationNode { get; init; }

    /// <summary>
    /// What the receiver closed a generic extension block's type parameters with, on the copy
    /// <see cref="ExtensionSpecialization"/> makes of one of its methods. That copy substitutes its
    /// parameters and return but shares its type parameters with the declaration, so their
    /// constraints still name the block's and are closed through this instead.
    /// </summary>
    public TypeSubstitution BlockClosing { get; init; } = TypeSubstitution.Empty;

    public ExtensionBlock? ClosedBlock { get; init; }

    public ExtensionBlock? DeclaringBlock { get; init; }

    // A closed signature keeps its body on the canonical declaration, whose parameter and receiver
    // identities remain coherent and whose inferred promises hold for every admitted construction.
    public Method? SourceDeclaration { get; init; }

    IReadOnlyList<ITypeParameter> IGenericDeclaration.TypeParameters => TypeParametersList;
    IReadOnlyList<IContextBinding> IMethod.ContextBindings => ContextBindingsList;
    IReadOnlyList<IParameter> IMethod.Parameters => ParametersList;
    ThisTier? IMethod.DeclaredThisTier => DeclaredThisTier;
    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}