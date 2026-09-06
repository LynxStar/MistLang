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
internal sealed record PendingExtension(ExtensionBlockDeclarationNode Ast, Scope DeclaringScope, string NamespaceName)
{
    /// <summary>
    /// The unit the block was written in, assigned once the type walk that stashed it has finished.
    /// Not a constructor argument: that walk is given a scope and an AST and has no unit, so
    /// threading one down purely to label the result would put file identity into the type builder.
    /// <para>
    /// Emission needs it. A block is not a <see cref="Model.DeclaredType"/>, so it cannot ride in
    /// <c>CompilationUnit.Types</c>, and without this there is no route from a file to its blocks.
    /// </para>
    /// </summary>
    public Model.CompilationUnit? Unit { get; set; }
}