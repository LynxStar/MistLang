using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// One syntactic <c>extension</c> block; several may target the same receiver type within a
/// namespace. Also models an ambient (assembly-projected) block, which has no AST.
/// </summary>
internal sealed class ExtensionBlock : IExtensionBlock, IHasTypeParameters, IMethodHost
{
    public ExtensionBlock(
        string receiverParameterName,
        string namespaceName,
        Scope scope,
        ExtensionBlockDeclarationNode? declarationNode)
    {
        ReceiverParameterName = receiverParameterName;
        NamespaceName = namespaceName;
        Scope = scope;
        DeclarationNode = declarationNode;

        // The scope answers for this block's type parameters and methods, so it needs the block.
        // Wiring it from here means the two cannot disagree and no builder can forget.
        if(scope is ExtensionScope extensionScope)
        {
            extensionScope.AttachBlock(this);
        }
    }

    /// <summary>
    /// Null until Phase 2, except for an ambient block, which sets it at construction.
    /// </summary>
    public IMistType? ResolvedReceiverType { get; internal set; }

    public string ReceiverParameterName { get; }

    /// <summary>
    /// Null until Phase 2b, and always for an ambient block, which has no body to name it from. A
    /// local rather than a parameter because that is what the scope chain answers with; the receiver
    /// becomes a <c>this</c>-annotated first parameter only at emission.
    /// </summary>
    public LocalVariable? Receiver { get; internal set; }

    /// <summary>
    /// Assigned once when the block is built, not derived at emission: a static extension member is
    /// called by carrier name from any file, so a name computed from the emitting unit is unreachable
    /// from the call site.
    /// </summary>
    public string CarrierName { get; internal set; } = "";

    public string NamespaceName { get; }

    public Scope Scope { get; }

    /// <summary>
    /// Kept from Phase 1 for Phase 2 member resolution. Null for an ambient block.
    /// </summary>
    internal ExtensionBlockDeclarationNode? DeclarationNode { get; }

    public List<TypeParameter> TypeParametersList { get; } = [];

    public List<Method> MethodsList { get; } = [];

    public List<Property> PropertiesList { get; } = [];

    IMistType IExtensionBlock.ReceiverType => ResolvedReceiverType!;

    IReadOnlyList<ITypeParameter> IGenericDeclaration.TypeParameters => TypeParametersList;

    IReadOnlyList<IMethod> IExtensionBlock.Methods => MethodsList;

    IReadOnlyList<IProperty> IExtensionBlock.Properties => PropertiesList;

    string IMethodHost.Name => $"extension({ReceiverParameterName})";

    bool IMethodHost.IsAbstract => false;

    bool IMethodHost.IsIntrinsic => false;
}