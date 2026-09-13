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

    public bool InLambda { get; set; }

    /// <summary>
    /// The member whose body is being bound, or null outside one. Carried so an await can be recorded
    /// against the body that contains it, which is the only place that knows.
    /// </summary>
    public Method? EnclosingMethod { get; set; }

    /// <summary>
    /// Whether the body being bound is an ASYNC context, which changes what a call to a task-returning
    /// member MEANS: in a sync context it yields the result and the await is inserted, in an async one
    /// it yields the task and the author writes the await. The same call site therefore has two types
    /// depending only on where it sits, which is why this is carried rather than derived at emission.
    /// </summary>
    public bool IsAsyncContext { get; set; }

    /// <summary>
    /// The property whose accessor is being bound, or null anywhere else. What <c>field</c> resolves
    /// against, and the reason it is carried here rather than looked up: <c>field</c> is its own
    /// token, so it never reaches a scope as a name to resolve.
    /// </summary>
    public Property? EnclosingProperty { get; set; }

    /// <summary>
    /// Whether a method group reaching the end of an expression is an ARGUMENT to a call that has not
    /// chosen its overload yet, which makes the call the thing that converts it. Carried rather than
    /// passed, because the group surfaces at the bottom of the expression recursion and the call that
    /// settles it sits at the top.
    /// </summary>
    public bool CallSettlesMethodGroups { get; set; }

    public BindingContext(BodyScope scope, IMistType? returnType)
    {
        Scope = scope;
        ReturnType = returnType;
    }

    /// <summary>
    /// A nested block's context: a scope of its own, everything else inherited. Copying happens HERE
    /// rather than at each call site so that a field added to this class is inherited by construction
    /// -- the callers used to list the fields themselves, which made every addition a silent omission
    /// waiting at whichever site was not updated.
    /// </summary>
    public BindingContext(BodyScope scope, IMistType? returnType, BindingContext enclosing)
        : this(scope, returnType ?? enclosing.ReturnType)
    {
        EnclosingProperty = enclosing.EnclosingProperty;
        EnclosingMethod = enclosing.EnclosingMethod;
        IsAsyncContext = enclosing.IsAsyncContext;
        InLambda = enclosing.InLambda;
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

    public MethodGroupSettlementScope PushMethodGroupSettlement(bool settled)
    {
        return new MethodGroupSettlementScope(this, settled);
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

    internal struct MethodGroupSettlementScope : IDisposable
    {
        private readonly BindingContext ctx;
        private readonly bool previous;

        public MethodGroupSettlementScope(BindingContext ctx, bool settled)
        {
            this.ctx = ctx;
            previous = ctx.CallSettlesMethodGroups;
            ctx.CallSettlesMethodGroups = settled;
        }

        public void Dispose()
        {
            ctx.CallSettlesMethodGroups = previous;
        }
    }
}