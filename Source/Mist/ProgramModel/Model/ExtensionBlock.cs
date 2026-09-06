using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Represents a single <c>extension(ReceiverType name) { ... }</c> declaration.
/// Each syntactic extension block has its own scope, receiver parameter, and member
/// declarations. Multiple blocks may target the same receiver type within a namespace.
/// Also used for ambient (assembly-projected) extension blocks where there is no AST.
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
    /// The resolved receiver type. Null until Phase 2 resolves it.
    /// For ambient extension blocks, set immediately after construction.
    /// </summary>
    public IMistType? ResolvedReceiverType { get; internal set; }

    /// <summary>The receiver parameter name (e.g., "self").</summary>
    public string ReceiverParameterName { get; }

    /// <summary>
    /// The receiver as something a body can actually NAME, rather than as the string above. Null
    /// until Phase 2b, and null for an ambient block, which has no body to name it from.
    /// <para>
    /// A local rather than a parameter because that is what the scope chain already answers with,
    /// and because the emitted signature does not exist yet -- the receiver becomes a
    /// <c>this</c>-annotated first parameter only at emission, where the carrier is written.
    /// </para>
    /// </summary>
    public LocalVariable? Receiver { get; internal set; }

    /// <summary>
    /// The name of the static class this block emits as, assigned ONCE when the block is built.
    /// <para>
    /// Not derived at emission: a static extension member is called by carrier name from any file,
    /// so a name computed from the EMITTING unit is unreachable from the call site that needs it.
    /// </para>
    /// </summary>
    public string CarrierName { get; internal set; } = "";

    /// <summary>The namespace this extension block belongs to.</summary>
    public string NamespaceName { get; }

    /// <summary>The extension block's own scope.</summary>
    public Scope Scope { get; }

    /// <summary>
    /// The AST node stashed during Phase 1 for Phase 2 member resolution.
    /// Null for ambient extension blocks projected from assemblies.
    /// </summary>
    internal ExtensionBlockDeclarationNode? DeclarationNode { get; }

    /// <summary>Type parameters declared on this extension block.</summary>
    public List<TypeParameter> TypeParametersList { get; } = [];

    /// <summary>Methods declared in this extension block.</summary>
    public List<Method> MethodsList { get; } = [];

    /// <summary>Properties declared in this extension block.</summary>
    public List<Property> PropertiesList { get; } = [];

    // ═══════════════════════════════════════════════════════════════════════
    // IExtensionBlock
    // ═══════════════════════════════════════════════════════════════════════

    IMistType IExtensionBlock.ReceiverType => ResolvedReceiverType!;

    IReadOnlyList<ITypeParameter> IGenericDeclaration.TypeParameters => TypeParametersList;

    IReadOnlyList<IMethod> IExtensionBlock.Methods => MethodsList;

    IReadOnlyList<IProperty> IExtensionBlock.Properties => PropertiesList;

    // ═══════════════════════════════════════════════════════════════════════
    // IMethodHost
    // ═══════════════════════════════════════════════════════════════════════

    string IMethodHost.Name => $"extension({ReceiverParameterName})";

    bool IMethodHost.IsAbstract => false;

    bool IMethodHost.IsIntrinsic => false;
}