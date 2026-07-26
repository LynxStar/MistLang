using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An expression that resolved to a namespace (e.g., <c>Simpliest</c> in
/// <c>Simpliest.Program.RunDemo()</c>). Only ever valid as the receiver of a further
/// qualification, which the postfix binder folds away; a namespace reference should
/// never survive into a finished expression tree.
/// <para>
/// A namespace is not a value, so there is no honest type to report and
/// <see cref="ErrorType"/> is used as the sentinel. <c>UnknownType</c> is deliberately
/// avoided because the emitter renders it as <c>object</c>.
/// </para>
/// </summary>
internal sealed class NamespaceReferenceExpression(SyntaxNode node, string namespaceName) : Expression(node)
{
    /// <summary>
    /// The fully qualified namespace name resolved so far, so the next qualification
    /// step can probe <c>NamespaceName.Member</c> directly.
    /// </summary>
    public string NamespaceName { get; } = namespaceName;

    public override IMistType Type { get; } = ErrorType.Instance;
}