using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction.Scaffolding;

/// <summary>
/// Stashed during Phase 1 when an extension block is encountered.
/// Phase 2 uses this to resolve the receiver type and build members.
/// </summary>
internal sealed record PendingExtension(
    ExtensionBlockDeclarationNode Ast,
    Scope FileScope,
    string NamespaceName);