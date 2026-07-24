using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Represents a single <c>extension(ReceiverType name) { ... }</c> declaration.
/// Each syntactic extension block has its own scope, receiver parameter, and member
/// declarations. Multiple blocks may target the same receiver type within a namespace.
/// Also used for ambient (assembly-projected) extension blocks where there is no AST.
/// </summary>
internal sealed class ExtensionBlock : IExtensionBlock, IMethodHost
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
    }

    /// <summary>
    /// The resolved receiver type. Null until Phase 2 resolves it.
    /// For ambient extension blocks, set immediately after construction.
    /// </summary>
    public IMistType? ResolvedReceiverType { get; internal set; }

    /// <summary>The receiver parameter name (e.g., "self").</summary>
    public string ReceiverParameterName { get; }

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

    IReadOnlyList<ITypeParameter> IExtensionBlock.TypeParameters => TypeParametersList;

    IReadOnlyList<IMethod> IExtensionBlock.Methods => MethodsList;

    IReadOnlyList<IProperty> IExtensionBlock.Properties => PropertiesList;

    // ═══════════════════════════════════════════════════════════════════════
    // IMethodHost
    // ═══════════════════════════════════════════════════════════════════════

    string IMethodHost.Name => $"extension({ReceiverParameterName})";

    bool IMethodHost.IsAbstract => false;

    bool IMethodHost.IsIntrinsic => false;
}