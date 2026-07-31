using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Mutable state passed through the body-binding recursion. Carries the current
/// lexical scope and the target type propagated from the surrounding construct.
/// </summary>
internal sealed class BindingContext
{
    /// <summary>
    /// The lexical scope in effect. Always a body scope: binding only ever happens inside a
    /// member body or a block within one, and typing it so means a statement can declare a
    /// local without first proving where it is.
    /// </summary>
    public BodyScope Scope { get; set; }

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

    /// <summary>
    /// The property whose accessor is being bound, or null anywhere else. What <c>field</c> resolves
    /// against, and the reason it is carried here rather than looked up: <c>field</c> is its own
    /// token, so it never reaches a scope as a name to resolve.
    /// </summary>
    public Property? EnclosingProperty { get; set; }

    public BindingContext(BodyScope scope, IMistType? returnType)
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