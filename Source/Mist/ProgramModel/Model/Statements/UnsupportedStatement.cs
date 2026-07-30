using Storm.Runtime;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A statement kind the body binder does not yet resolve. Retains the AST node so a later pass can
/// resolve it, and so the emitter can report where it gave up -- see
/// <c>BackendDiagnostics.UnsupportedStatement</c>.
/// <para>
/// Typed as <see cref="SyntaxNode"/> rather than <c>StatementNode</c> because not every position
/// that yields an unsupported statement holds one: a <c>for</c> initializer is a
/// <c>ForInitializerNode</c>, which is not a statement node. The node is only used for provenance,
/// and diagnostic reporting takes the base type, so nothing needs the narrower one.
/// </para>
/// </summary>
internal sealed class UnsupportedStatement(SyntaxNode node) : Statement
{
    public SyntaxNode Node { get; } = node;
}