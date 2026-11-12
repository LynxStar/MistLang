using Storm.Runtime;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A statement kind the body binder does not resolve; the emitter reports it at its node
/// (<c>BackendDiagnostics.UnsupportedStatement</c>).
/// <para>
/// Typed as <see cref="SyntaxNode"/> rather than <c>StatementNode</c> because a <c>for</c> initializer
/// is a <c>ForInitializerNode</c>, which is not a statement node.
/// </para>
/// </summary>
internal sealed class UnsupportedStatement(SyntaxNode node) : Statement
{
    public SyntaxNode Node { get; } = node;
}