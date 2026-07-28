using Mist.Generated;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Scaffolding;

/// <summary>
/// Stashed during Phase 1 when an extension block is encountered.
/// Phase 2 uses this to resolve the receiver type and build members.
/// </summary>
/// <param name="Ast">The extension block declaration.</param>
/// <param name="DeclaringScope">
/// The scope the block was written in: the namespace level for its file's top level, or a block
/// namespace's level. The extension's own scope is parented to this.
/// </param>
/// <param name="NamespaceName">The namespace the block belongs to.</param>
internal sealed record PendingExtension(ExtensionBlockDeclarationNode Ast, Scope DeclaringScope, string NamespaceName);