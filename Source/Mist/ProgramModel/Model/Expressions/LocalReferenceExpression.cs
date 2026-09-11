using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A reference to a local variable or parameter. Its type is the local's type, unless a `??=` earlier
/// in the scope narrowed the local to the non-void constituent of its optional type, in which case
/// it is that constituent and <see cref="IsNarrowed"/> says so.
/// </summary>
internal sealed class LocalReferenceExpression(SyntaxNode node, LocalVariable local, IMistType? narrowed = null) : Expression(node)
{
    /// <summary>
    /// The local variable or parameter being referenced.
    /// </summary>
    public LocalVariable Local { get; } = local;

    public override IMistType Type { get; } = narrowed ?? local.Type;

    public bool IsNarrowed => !ReferenceEquals(Type, Local.Type);
}