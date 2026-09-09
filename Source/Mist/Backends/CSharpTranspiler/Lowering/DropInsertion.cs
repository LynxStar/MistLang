using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Which locals a block has to drop on the way out, and which it must not. A droppable binding that
/// still owns its value at the end of the scope is dropped there; one whose value has been MOVED
/// elsewhere is not, the new owner having taken the obligation with it.
/// <para>
/// A MOVE IS ANY USE THAT HANDS THE VALUE ON: passing it as an argument, returning it, or calling its
/// own Drop. Reading a member off it is not one -- <c>f.GetFd()</c> borrows and gives back -- which is
/// why this looks at the POSITION a reference occupies rather than counting references.
/// </para>
/// </summary>
internal static class DropInsertion
{
    /// <summary>
    /// What a value calls on the way out, or null for one with nothing to release. A CLR type reaches
    /// it through IDisposable, which the projector already recognises, and Mist's own through a
    /// declared Drop -- the same obligation under the two names their worlds use.
    /// </summary>
    public static string? ExitAction(IMistType type)
    {
        if(Definition(type) is ConcreteType { IsDisposableDrop: true })
        {
            return "Dispose";
        }

        return Declares(type) || SynthesizesDrop(type) ? "Drop" : null;
    }

    /// <summary>
    /// The full release of a value: its own drop, and then the drop of whatever that returned, for as
    /// long as the results keep being droppable. A drop may HAND BACK what it was holding -- a box's
    /// gives up its contents -- and the obligation travels with the value, so stopping at the first
    /// call would leak exactly what the box was protecting.
    /// <para>
    /// Depth-limited because a type whose drop returns itself would otherwise chain forever. Nothing
    /// declares one, and a limit is cheaper than proving none can.
    /// </para>
    /// </summary>
    public static string DropChain(string receiver, IMistType type, int depth = 0)
    {
        if(ExitAction(type) is not { } action)
        {
            return receiver;
        }

        var call = $"{receiver}.{action}()";
        var returned = DropReturns(type);

        return depth < ChainLimit && returned is not null && ExitAction(returned) is not null
            ? DropChain($"({call})", returned, depth + 1)
            : call
            ;
    }

    private const int ChainLimit = 8;

    /// <summary>
    /// What this type's declared drop hands back, with the type arguments of a constructed generic
    /// applied -- a box of tokens gives up a token, not the T its declaration names.
    /// </summary>
    private static IMistType? DropReturns(IMistType type)
    {
        if(Definition(type) is not MemberType member
            || member.MethodsList.Find(
                method => method.Name == "Drop" && method.ParametersList.Count == 0) is not { } drop)
        {
            return null;
        }

        return type is ConstructedType constructed
            ? Substituted(drop.ReturnType, member, constructed)
            : drop.ReturnType
            ;
    }

    private static IMistType Substituted(
        IMistType declared, MemberType definition, ConstructedType constructed)
    {
        var index = definition.TypeParametersList.FindIndex(
            parameter => ReferenceEquals(parameter, declared));

        return index >= 0 && index < constructed.TypeArguments.Count
            ? constructed.TypeArguments[index]
            : declared
            ;
    }

    /// <summary>
    /// Whether a container gets a drop it never declared, built from the fields that have one. A type
    /// under an obligation cannot honour it without releasing what it holds, so a linear container of
    /// linear handles must drop them; the author writing no Drop is not declining, there being nothing
    /// to write that the fields do not already say.
    /// </summary>
    public static bool SynthesizesDrop(IMistType type)
    {
        return !Declares(type)
            && DropsAtScopeExit(type)
            && Definition(type) is ConcreteType concrete
            && DroppableFields(concrete).Count > 0
            ;
    }

    /// <summary>
    /// The instance fields a synthesized drop releases, in DECLARATION order -- the reverse of the
    /// order locals drop in, because these are parts of one value rather than a sequence of bindings
    /// and the declaration is the only order they have.
    /// </summary>
    public static List<Field> DroppableFields(ConcreteType type)
    {
        return type.FieldsList
            .Where(field => !field.Modifiers.IsStatic
                && (ExitAction(field.Type) is not null || IsDroppableParameter(field.Type)))
            .ToList();
    }

    /// <summary>
    /// Whether a field's type is a type PARAMETER known droppable by its constraint. The constraint is
    /// a discipline rather than a nominal type, so C# has no interface to call the drop through and the
    /// dispatch is dynamic -- see <see cref="FieldDrop"/>.
    /// </summary>
    public static bool IsDroppableParameter(IMistType type)
    {
        return type is TypeParameter parameter
            && parameter.ConstraintsList.Exists(constraint => constraint is KeywordConstraint
            {
                Kind: TypeParameterConstraintKind.Linear
                    or TypeParameterConstraintKind.Affine
                    or TypeParameterConstraintKind.Relevant,
            });
    }

    /// <summary>
    /// Releasing one field of the enclosing value. A generic one goes through <c>dynamic</c>, which is
    /// the cost of a discipline constraint carrying no member: nothing in the emitted C# says a T has a
    /// Drop, so nothing but the runtime can find it.
    /// </summary>
    public static string FieldDrop(Field field)
    {
        return IsDroppableParameter(field.Type)
            ? $"((dynamic)this.{field.Name}).Drop();"
            : $"{DropChain($"this.{field.Name}", field.Type)};"
            ;
    }

    /// <summary>
    /// The fields a DECLARED drop still owes, which is every droppable one its body does not hand on.
    /// A container's obligation does not end because the author wrote a Drop of their own -- what they
    /// wrote is what happens IN ADDITION to releasing what it holds. A field returned out is moved, so
    /// `Drop()` returning its own value releases nothing.
    /// </summary>
    public static List<Field> FieldsOwedBy(Method drop, ConcreteType type)
    {
        return drop is { Name: "Drop", ParametersList.Count: 0 } && drop.Body is { } body
            ? DroppableFields(type)
                .Where(field => !body.Statements.Any(statement => Moves(statement, Field(field.Name))))
                .ToList()
            : []
            ;
    }

    /// <summary>
    /// Whether a call to <c>Drop</c> on this type reaches something. An INTRINSIC declaration does not:
    /// it says the target already supplies the member, and no CLR task has a Drop -- Standard declares
    /// one so a task can carry a discipline, which is a claim about Mist's type system and not about
    /// what can be called at runtime.
    /// </summary>
    private static bool Declares(IMistType type)
    {
        return Definition(type) is MemberType member
            && member is not ConcreteType { IsIntrinsic: true }
            && member.MethodsList.Exists(
                method => method.Name == "Drop" && method.ParametersList.Count == 0)
            ;
    }

    /// <summary>
    /// Whether this declaration's local is still owned at the end of <paramref name="remainder"/>, the
    /// statements that follow it in the block. Only those can move it: a statement before the
    /// declaration cannot mention the local at all.
    /// </summary>
    public static bool NeedsDrop(
        LocalDeclarationStatement declaration, IReadOnlyList<Statement> remainder)
    {
        return declaration.Initializer is not null
            && DropsAtScopeExit(declaration.Local.Type)
            && ExitAction(declaration.Local.Type) is not null
            && !remainder.Any(statement => Moves(statement, Same(declaration.Local)));
    }

    /// <summary>
    /// Whether falling out of scope obliges a drop, which only the two disciplines that REQUIRE one do.
    /// Linear must drop exactly once and relevant at least once, so a scope holding either has to.
    /// Affine may drop at most once and unrestricted any number of times, and neither is an obligation
    /// -- affine explicitly allows forgetting, so inserting a drop there would invent a call the author
    /// declined to make.
    /// </summary>
    /// <summary>
    /// The parameters a body drops on its way out: an owned droppable value the caller handed over and
    /// the body did not pass on. A by-reference parameter is a loan and is never one of these.
    /// </summary>
    public static List<Parameter> DroppedParameters(Method method, Block body)
    {
        return method.ParametersList
            .Where(parameter => !parameter.Modifier.IsRef
                && DropsAtScopeExit(parameter.Type)
                && ExitAction(parameter.Type) is not null
                && !MovesParameter(body, parameter))
            .ToList();
    }

    /// <summary>
    /// A parameter is referenced through a local of the same identity as the body's declared locals,
    /// so the move walk is reused by matching on NAME -- the body's references resolve to the
    /// parameter's own isTarget, which the emitter does not hold.
    /// </summary>
    private static bool MovesParameter(Block body, Parameter parameter)
    {
        return body.Statements.Any(statement => Moves(statement, Named(parameter.Name)));
    }

    private static Func<Expression, bool> Same(LocalVariable local)
    {
        return candidate =>
            candidate is LocalReferenceExpression reference && ReferenceEquals(reference.Local, local);
    }

    /// <summary>
    /// A field of the enclosing value, matched by name for the reason a parameter is: the emitter holds
    /// the declaration and the body holds references bound to it, and no identity spans the two.
    /// </summary>
    private static Func<Expression, bool> Field(string name)
    {
        return candidate =>
            candidate is FieldReferenceExpression reference
            && string.Equals(reference.Field.Name, name, StringComparison.Ordinal);
    }

    /// <summary>
    /// A parameter's references resolve to a local the emitter does not hold -- BodyBinder declares one
    /// per parameter -- so a parameter is matched by NAME. A nested block shadowing the name would be
    /// read as a use of the parameter, which errs toward eliding a drop rather than inventing one.
    /// </summary>
    private static Func<Expression, bool> Named(string name)
    {
        return candidate =>
            candidate is LocalReferenceExpression reference
            && string.Equals(reference.Local.Name, name, StringComparison.Ordinal);
    }

    private static bool DropsAtScopeExit(IMistType type)
    {
        return Definition(type) is ConcreteType concrete
            && concrete.Discipline is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Relevant
            ;
    }

    /// <summary>
    /// The declaration behind a type. A CONSTRUCTED generic carries none of its definition's modifiers,
    /// so asking a `LinearBox of int` about its discipline answers nothing unless it is unwrapped first.
    /// </summary>
    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }

    private static bool Moves(Statement statement, Func<Expression, bool> isTarget)
    {
        return statement switch
        {
            ReturnStatement { Value: { } returned } =>
                IsReference(returned, isTarget) || Moves(returned, isTarget),
            ExpressionStatement expression => Moves(expression.Expression, isTarget),
            LocalDeclarationStatement { Initializer: { } initializer } => Moves(initializer, isTarget),
            Block block => block.Statements.Any(nested => Moves(nested, isTarget)),
            IfStatement branch => Moves(branch.Condition, isTarget)
                || Moves(branch.ThenBranch, isTarget)
                || (branch.ElseBranch is { } otherwise && Moves(otherwise, isTarget)),
            WhileStatement loop => Moves(loop.Condition, isTarget) || Moves(loop.Body, isTarget),
            ForEachStatement loop => Moves(loop.Collection, isTarget) || Moves(loop.Body, isTarget),
            WithBlockStatement scope => Moves(scope.Value, isTarget) || Moves(scope.Body, isTarget),
            _ => false,
        };
    }

    /// <summary>
    /// A reference in a moving POSITION anywhere in the expression. The receiver of a call is walked
    /// but not itself a move, except where the call is the value's own Drop -- an explicit drop
    /// consumes the binding exactly as handing it away does.
    /// </summary>
    private static bool Moves(Expression expression, Func<Expression, bool> isTarget)
    {
        return expression switch
        {
            InvocationExpression invocation => MovesThroughArguments(invocation, isTarget)
                || IsExplicitDrop(invocation, isTarget)
                || invocation.Arguments.Any(argument => Moves(argument, isTarget))
                || Moves(invocation.Receiver, isTarget),
            AwaitExpression await => Moves(await.Operand, isTarget),
            AsyncExpression lifted => Moves(lifted.Operand, isTarget),
            MemberAccessExpression access => Moves(access.Receiver, isTarget),
            NewExpression creation =>
                creation.Arguments.Any(argument => IsReference(argument, isTarget))
                || creation.Arguments.Any(argument => Moves(argument, isTarget)),
            BinaryExpression binary => Moves(binary.Left, isTarget) || Moves(binary.Right, isTarget),
            InterpolatedStringExpression interpolated => interpolated.Parts
                .OfType<InterpolatedStringHole>()
                .Any(hole => Moves(hole.Expression, isTarget)),
            _ => false,
        };
    }

    /// <summary>
    /// Whether an argument hands the value over. A BY-REFERENCE parameter does not: lending is a loan
    /// and the caller still owns what it lent, so the drop stays with it.
    /// </summary>
    private static bool MovesThroughArguments(InvocationExpression invocation, Func<Expression, bool> isTarget)
    {
        var parameters = invocation.Method?.ParametersList;

        for(var i = 0; i < invocation.Arguments.Count; i++)
        {
            if(!IsReference(invocation.Arguments[i], isTarget))
            {
                continue;
            }

            if(parameters is null || i >= parameters.Count || !parameters[i].Modifier.IsRef)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExplicitDrop(InvocationExpression invocation, Func<Expression, bool> isTarget)
    {
        return invocation.Method is { Name: "Drop" }
            && invocation.Receiver is MemberAccessExpression access
            && IsReference(access.Receiver, isTarget)
            ;
    }

    private static bool IsReference(Expression expression, Func<Expression, bool> isTarget)
    {
        return isTarget(expression);
    }
}