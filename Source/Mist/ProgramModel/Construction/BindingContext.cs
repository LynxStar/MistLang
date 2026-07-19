using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Mutable state passed through the body-binding recursion. Carries the current
/// lexical scope and the target type propagated from the surrounding construct.
/// </summary>
internal sealed class BindingContext
{
    /// <summary>
    /// The lexical scope in effect (locals, parameters, types).
    /// </summary>
    public Scope Scope { get; set; }

    /// <summary>
    /// The enclosing type whose members are in scope (for resolving sibling
    /// method calls, field access via implicit this, etc.). Null for top-level statements.
    /// </summary>
    public MemberType? EnclosingType { get; set; }

    /// <summary>
    /// The enclosing member's declared return type, used as the target for
    /// <c>return</c> values. Null for void methods.
    /// </summary>
    public IMistType? ReturnType { get; set; }

    /// <summary>
    /// The type the current expression is expected to produce, or null when
    /// there is no target.
    /// </summary>
    public IMistType? TargetType { get; set; }

    public BindingContext(Scope scope, IMistType? returnType)
    {
        Scope = scope;
        ReturnType = returnType;
    }

    /// <summary>
    /// Sets <see cref="TargetType"/> for the duration of a <c>using</c> block,
    /// restoring the previous value on dispose. Prevents forgetting to restore
    /// and scopes the target type visually to exactly where it applies.
    /// </summary>
    public TargetTypeScope PushTargetType(IMistType? type)
    {
        return new TargetTypeScope(this, type);
    }

    /// <summary>
    /// RAII guard that restores <see cref="TargetType"/> to its
    /// previous value on dispose. Struct to avoid allocation.
    /// </summary>
    internal struct TargetTypeScope : IDisposable
    {
        private readonly BindingContext ctx;
        private readonly IMistType? previous;

        public TargetTypeScope(BindingContext ctx, IMistType? type)
        {
            this.ctx = ctx;
            previous = ctx.TargetType;
            ctx.TargetType = type;
        }

        public void Dispose()
        {
            ctx.TargetType = previous;
        }
    }
}