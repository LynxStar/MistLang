using Mist.Generated;
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

    public bool ReturnTypeIsFailed { get; set; }

    /// <summary>
    /// What a valued <c>return</c> in the top-level statements answers to instead of <see cref="ReturnType"/>:
    /// the exit code's int. Inherited by a nested block like everything else, and cleared by the lambda,
    /// local function and async block binders, whose returns answer to the body they start.
    /// </summary>
    public IMistType? ExitCodeType { get; set; }

    /// <summary>
    /// The type the current expression is expected to produce, or null when
    /// there is no target.
    /// </summary>
    public IMistType? TargetType { get; set; }

    public bool TargetTypeIsFailed { get; private set; }

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
    /// The postfix whose value IS an argument to a call that has not chosen its overload yet, which makes
    /// the call the thing that converts a method group it yields. Carried rather than passed, because the
    /// group surfaces at the bottom of the expression recursion and the call that settles it sits at the
    /// top. A node rather than a flag, so that a group nested in the argument -- an operand of `??`, a
    /// switch arm -- is converted where it stands, against its own target.
    /// </summary>
    public PostfixNode? SettledMethodGroup { get; set; }

    /// <summary>
    /// The one access the enclosing assignment writes through -- see
    /// <see cref="Expressions.AssignmentTargets"/> -- and whether it also reads it, as <c>??=</c> does.
    /// Null outside an assignment's target, where every access is a read. A node rather than a flag so
    /// that the receivers an access is reached through, which the same assignment binds, stay reads.
    /// </summary>
    public Storm.Runtime.SyntaxNode? WriteTargetNode { get; private set; }

    public StorageAccess WriteAccess { get; private set; }

    public StorageAccess AccessAt(Storm.Runtime.SyntaxNode node)
    {
        return ReferenceEquals(node, WriteTargetNode) ? WriteAccess : StorageAccess.Read;
    }

    public WriteTargetScope PushWriteTarget(Storm.Runtime.SyntaxNode? node, StorageAccess access)
    {
        return new WriteTargetScope(this, node, access);
    }

    internal struct WriteTargetScope : IDisposable
    {
        private readonly BindingContext ctx;
        private readonly Storm.Runtime.SyntaxNode? previous;
        private readonly StorageAccess previousAccess;

        public WriteTargetScope(BindingContext ctx, Storm.Runtime.SyntaxNode? node, StorageAccess access)
        {
            this.ctx = ctx;
            previous = ctx.WriteTargetNode;
            previousAccess = ctx.WriteAccess;
            ctx.WriteTargetNode = node;
            ctx.WriteAccess = access;
        }

        public void Dispose()
        {
            ctx.WriteTargetNode = previous;
            ctx.WriteAccess = previousAccess;
        }
    }

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
        ReturnTypeIsFailed = ReferenceEquals(ReturnType, enclosing.ReturnType) && enclosing.ReturnTypeIsFailed;
        ExitCodeType = enclosing.ExitCodeType;
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
    public TargetTypeScope PushTargetType(IMistType? type, bool isFailed = false)
    {
        return new TargetTypeScope(this, type, isFailed);
    }

    public MethodGroupSettlementScope PushMethodGroupSettlement(PostfixNode? settled)
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

        private readonly bool previousFailure;

        public TargetTypeScope(BindingContext ctx, IMistType? type, bool isFailed)
        {
            this.ctx = ctx;
            previous = ctx.TargetType;
            previousFailure = ctx.TargetTypeIsFailed;
            ctx.TargetType = type;
            ctx.TargetTypeIsFailed = isFailed;
        }

        public void Dispose()
        {
            ctx.TargetType = previous;
            ctx.TargetTypeIsFailed = previousFailure;
        }
    }

    internal struct MethodGroupSettlementScope : IDisposable
    {
        private readonly BindingContext ctx;
        private readonly PostfixNode? previous;

        public MethodGroupSettlementScope(BindingContext ctx, PostfixNode? settled)
        {
            this.ctx = ctx;
            previous = ctx.SettledMethodGroup;
            ctx.SettledMethodGroup = settled;
        }

        public void Dispose()
        {
            ctx.SettledMethodGroup = previous;
        }
    }
}