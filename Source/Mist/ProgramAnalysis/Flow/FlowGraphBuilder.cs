using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Flow;

/// <summary>
/// Builds a <see cref="FlowGraph"/> over a body root. Every branch the language has is a branch here
/// -- if, the loops, `&amp;&amp;`, `||`, `!`, `??`, `??=`, an `is` test, a switch's arms and guards, and
/// each pattern's own match-or-not -- so that a rule reads a pattern's binding only on the path where
/// it was bound, and a condition that is a literal `true` or `false` has one exit rather than two, as
/// `while(true)` must for what follows its break to count. A `?.` is not a branch: nothing is bound on
/// either side of it, and the chain it heads is one expression to every rule that reads it. Writing a
/// field of a struct held in a local writes part of the local and reads none of it, so the receiver of
/// such a target is not evaluated; through a class-typed local the same write dereferences the local
/// and reads it.
/// </summary>
internal sealed class FlowGraphBuilder
{
    private readonly List<FlowBlock> blocks = [];
    private readonly Dictionary<object, FlowGraph> nested = new(ReferenceEqualityComparer.Instance);
    private readonly List<FlowLoop> loops = [];
    private readonly Stack<(FlowBlock Exit, int Depth)> breakTargets = new();
    private readonly Stack<Statement> scopes = new();
    private readonly FlowBlock exit;

    private FlowGraphBuilder()
    {
        exit = NewBlock();
    }

    public static FlowGraph Build(object root)
    {
        return Build(root, introducer: null, []);
    }

    private static FlowGraph Build(object root, object? introducer, IReadOnlyList<LocalVariable> introduced)
    {
        var builder = new FlowGraphBuilder();
        var entry = builder.NewBlock();

        foreach(var variable in introduced)
        {
            entry.Steps.Add(new FlowStep(FlowStepKind.Introduce, introducer!, variable));
        }

        var end = root switch
        {
            Statement statement => builder.Statement(statement, entry),
            Expression expression => builder.Expression(expression, entry),
            _ => throw new InvalidOperationException($"ICE: no flow can be built over a {root.GetType().Name} root."),
        };

        end.Successors.Add(builder.exit);

        return new FlowGraph(entry, builder.exit, builder.blocks, builder.loops, builder.nested);
    }

    private FlowBlock Statement(Statement statement, FlowBlock current)
    {
        switch(statement)
        {
            case Block block:
                scopes.Push(block);

                foreach(var inner in block.Statements)
                {
                    current = Statement(inner, current);
                }

                _ = scopes.Pop();
                current.Steps.Add(new FlowStep(FlowStepKind.Leave, block));
                return current;

            case ExpressionStatement expressionStatement:
                return Expression(expressionStatement.Expression, current);

            case LocalDeclarationStatement declaration:
                if(declaration.Initializer is { } initializer)
                {
                    current = Expression(initializer, current);
                }

                current.Steps.Add(new FlowStep(FlowStepKind.Declare, declaration, declaration.Local));
                return current;

            case TupleDeconstructionStatement deconstruction:
                current = Expression(deconstruction.Initializer, current);

                foreach(var target in deconstruction.Targets)
                {
                    current.Steps.Add(new FlowStep(FlowStepKind.Introduce, deconstruction, target));
                }

                return current;

            case ReturnStatement returnStatement:
                if(returnStatement.Value is { } value)
                {
                    current = Expression(value, current);
                }

                current.Steps.Add(Evaluate(returnStatement));
                Leave(current, depth: 0);
                current.Successors.Add(exit);
                return NewBlock();

            case BreakStatement breakStatement:
                current.Steps.Add(Evaluate(breakStatement));
                Leave(current, breakTargets.Count > 0 ? breakTargets.Peek().Depth : 0);
                current.Successors.Add(breakTargets.Count > 0 ? breakTargets.Peek().Exit : exit);
                return NewBlock();

            case IfStatement ifStatement:
                {
                    var (then, otherwise) = Condition(ifStatement.Condition, current);
                    then = Statement(ifStatement.ThenBranch, then);

                    if(ifStatement.ElseBranch is { } elseBranch)
                    {
                        otherwise = Statement(elseBranch, otherwise);
                    }

                    return Join(then, otherwise);
                }

            case WhileStatement whileStatement:
                return Loop(current, header => Condition(whileStatement.Condition, header), whileStatement.Body, iterator: null);

            case ForStatement forStatement:
                {
                    var scoped = forStatement.Initializer is LocalDeclarationStatement;

                    if(scoped)
                    {
                        scopes.Push(forStatement);
                    }

                    if(forStatement.Initializer is { } forInitializer)
                    {
                        current = Statement(forInitializer, current);
                    }

                    var after = Loop(
                        current,
                        header => forStatement.Condition is { } condition ? Condition(condition, header) : (header, NewBlock()),
                        forStatement.Body,
                        forStatement.Iterator);

                    if(scoped)
                    {
                        _ = scopes.Pop();
                        after.Steps.Add(new FlowStep(FlowStepKind.Leave, forStatement));
                    }

                    return after;
                }

            case ForEachStatement forEach:
                current = Expression(forEach.Collection, current);

                return Loop(
                    current,
                    header =>
                    {
                        var body = Successor(header);
                        body.Steps.Add(new FlowStep(FlowStepKind.Introduce, forEach, forEach.Variable));
                        return (body, Successor(header));
                    },
                    forEach.Body,
                    iterator: null);

            case WithBlockStatement withBlock:
                current = Expression(withBlock.Value, current);

                if(withBlock.Binding is { } binding)
                {
                    current.Steps.Add(new FlowStep(FlowStepKind.Introduce, withBlock, binding));
                }

                return Statement(withBlock.Body, current);

            case LocalFunctionStatement function:
                nested[function] = Build(function.Body, function, function.Parameters);
                current.Steps.Add(Evaluate(function));
                return current;

            case UnsupportedStatement unsupported:
                current.Steps.Add(Evaluate(unsupported));
                return current;

            default:
                throw new InvalidOperationException($"ICE: FlowGraphBuilder has no row for {statement.GetType().Name}.");
        }
    }

    private FlowBlock Loop(
        FlowBlock current,
        Func<FlowBlock, (FlowBlock Body, FlowBlock Exit)> head,
        Block body,
        Expression? iterator)
    {
        var header = Successor(current);
        var (bodyEntry, loopExit) = head(header);

        breakTargets.Push((loopExit, scopes.Count));
        var bodyEnd = Statement(body, bodyEntry);
        _ = breakTargets.Pop();

        if(iterator is { } step)
        {
            bodyEnd = Expression(step, bodyEnd);
        }

        bodyEnd.Successors.Add(header);
        loops.Add(new FlowLoop(header, bodyEnd, body));
        return loopExit;
    }

    private (FlowBlock WhenTrue, FlowBlock WhenFalse) Condition(Expression condition, FlowBlock current)
    {
        switch(condition)
        {
            case BinaryExpression { OperatorKind: BinaryOperatorKind.LogicalAnd } both:
                {
                    var (leftTrue, leftFalse) = Condition(both.Left, current);
                    var (rightTrue, rightFalse) = Condition(both.Right, leftTrue);
                    return Evaluated(both, rightTrue, Join(leftFalse, rightFalse));
                }

            case BinaryExpression { OperatorKind: BinaryOperatorKind.LogicalOr } either:
                {
                    var (leftTrue, leftFalse) = Condition(either.Left, current);
                    var (rightTrue, rightFalse) = Condition(either.Right, leftFalse);
                    return Evaluated(either, Join(leftTrue, rightTrue), rightFalse);
                }

            case UnaryExpression { OperatorKind: UnaryOperatorKind.LogicalNot } negation:
                {
                    var (whenTrue, whenFalse) = Condition(negation.Operand, current);
                    return Evaluated(negation, whenFalse, whenTrue);
                }

            case IsPatternExpression test:
                {
                    current = Expression(test.Scrutinee, current);
                    var (matched, unmatched) = Pattern(test.Pattern, current);
                    return Assumed(test, Evaluated(test, matched, unmatched));
                }

            case LiteralExpression { Value: bool constant }:
                current.Steps.Add(Evaluate(condition));
                return constant ? (Successor(current), NewBlock()) : (NewBlock(), Successor(current));

            default:
                current = Expression(condition, current);
                return Assumed(condition, (Successor(current), Successor(current)));
        }
    }

    private static (FlowBlock, FlowBlock) Assumed(Expression condition, (FlowBlock WhenTrue, FlowBlock WhenFalse) exits)
    {
        exits.WhenTrue.Steps.Add(new FlowStep(FlowStepKind.Assume, condition));
        exits.WhenFalse.Steps.Add(new FlowStep(FlowStepKind.Assume, condition, Holds: false));
        return exits;
    }

    private static (FlowBlock, FlowBlock) Evaluated(Expression node, FlowBlock whenTrue, FlowBlock whenFalse)
    {
        whenTrue.Steps.Add(Evaluate(node));
        whenFalse.Steps.Add(Evaluate(node));
        return (whenTrue, whenFalse);
    }

    private (FlowBlock Matched, FlowBlock Unmatched) Pattern(Pattern pattern, FlowBlock current)
    {
        switch(pattern)
        {
            case CatchAllPattern:
                return Bound(pattern, current, NewBlock());

            case TypeTestPattern test:
                {
                    var (matched, unmatched) = Split(current);

                    if(test.Refinement is { } refinement)
                    {
                        var (refined, unrefined) = Pattern(refinement, matched);
                        matched = refined;
                        unmatched = Join(unmatched, unrefined);
                    }

                    return Bound(pattern, matched, unmatched);
                }

            case ComparisonPattern comparison:
                return Split(Expression(comparison.Bound, current));

            case RegexPattern:
                return Split(current);

            case NegatedPattern negated:
                {
                    var (matched, unmatched) = Pattern(negated.Operand, current);
                    return (unmatched, matched);
                }

            case ConjunctionPattern conjunction:
                return All(conjunction.Operands, current);

            case DisjunctionPattern disjunction:
                {
                    var (matched, unmatched) = Pattern(disjunction.Operands[0], current);

                    foreach(var operand in disjunction.Operands.Skip(1))
                    {
                        var (also, neither) = Pattern(operand, unmatched);
                        matched = Join(matched, also);
                        unmatched = neither;
                    }

                    return (matched, unmatched);
                }

            case PropertyPattern property:
                {
                    var (matched, unmatched) = property.TypeTest is { } typeTest ? Pattern(typeTest, current) : Split(current);
                    var (all, some) = All(property.Subpatterns.Select(subpattern => subpattern.Pattern), matched);
                    return Bound(pattern, all, Join(unmatched, some));
                }

            case TuplePattern tuple:
                {
                    var (matched, unmatched) = tuple.TestedType is null ? (current, NewBlock()) : Split(current);
                    var (all, some) = All(tuple.Elements, matched);
                    return Bound(pattern, all, Join(unmatched, some));
                }

            case ListPattern list:
                {
                    var (matched, unmatched) = Split(current);
                    var (all, some) = All(list.Elements.Select(element => element.Subpattern).OfType<Pattern>(), matched);
                    return (all, Join(unmatched, some));
                }

            default:
                throw new InvalidOperationException($"ICE: FlowGraphBuilder has no row for {pattern.GetType().Name}.");
        }
    }

    private (FlowBlock Matched, FlowBlock Unmatched) All(IEnumerable<Pattern> patterns, FlowBlock current)
    {
        var matched = current;
        var unmatched = new List<FlowBlock>();

        foreach(var pattern in patterns)
        {
            var (next, failed) = Pattern(pattern, matched);
            matched = next;
            unmatched.Add(failed);
        }

        return (matched, Join(unmatched));
    }

    private static (FlowBlock, FlowBlock) Bound(Pattern pattern, FlowBlock matched, FlowBlock unmatched)
    {
        if(pattern.Binding is { } binding)
        {
            matched.Steps.Add(new FlowStep(FlowStepKind.Bind, pattern, binding));
        }

        return (matched, unmatched);
    }

    private FlowBlock Expression(Expression expression, FlowBlock current, bool isStorageReceiver = false)
    {
        switch(expression)
        {
            case MemberAccessExpression access when LocalStoragePath.Of(access) is not null || access.Type is ErrorType:
                current = Expression(access.Receiver, current, isStorageReceiver: true);
                break;

            case BinaryExpression { OperatorKind: BinaryOperatorKind.LogicalAnd or BinaryOperatorKind.LogicalOr }
                or UnaryExpression { OperatorKind: UnaryOperatorKind.LogicalNot }
                or IsPatternExpression:
                {
                    var (whenTrue, whenFalse) = Condition(expression, current);
                    return Join(whenTrue, whenFalse);
                }

            case AssignmentExpression assignment:
                return Assignment(assignment, current);

            case CoalesceExpression coalesce:
                {
                    current = Expression(coalesce.Left, current);
                    var (present, absent) = Split(current);
                    current = Join(present, Expression(coalesce.Right, absent));
                    break;
                }

            case SwitchExpression switchExpression:
                {
                    current = Expression(switchExpression.Scrutinee, current);
                    var exits = new List<FlowBlock>();

                    foreach(var arm in switchExpression.Arms)
                    {
                        var (matched, unmatched) = Pattern(arm.Pattern, current);

                        if(arm.Guard is { } guard)
                        {
                            var (held, failed) = Condition(guard, matched);
                            matched = held;
                            unmatched = Join(unmatched, failed);
                        }

                        exits.Add(Expression(arm.Result, matched));
                        current = unmatched;
                    }

                    current = Join(exits);
                    break;
                }

            case LambdaExpression lambda:
                if(((object?)lambda.Body ?? lambda.Result) is { } lambdaBody)
                {
                    nested[lambda] = Build(lambdaBody, lambda, lambda.Parameters);
                }

                break;

            case AsyncBlockExpression asyncBlock:
                nested[asyncBlock] = Build(asyncBlock.Body, asyncBlock, []);
                break;

            default:
                foreach(var child in BodyWalker.ChildrenOf(expression))
                {
                    current = Child(child, current);
                }

                break;
        }

        current.Steps.Add(new FlowStep(FlowStepKind.Evaluate, expression, IsStorageReceiver: isStorageReceiver));
        return current;
    }

    private FlowBlock Child(object child, FlowBlock current)
    {
        return child switch
        {
            Expression expression => Expression(expression, current),
            InterpolatedStringHole hole => Expression(hole.Expression, current),
            InterpolatedStringText => current,
            ObjectInitializerAssignment assignment => Expression(assignment.Value, current),
            _ => throw new InvalidOperationException($"ICE: FlowGraphBuilder has no row for a {child.GetType().Name} beneath an expression."),
        };
    }

    private FlowBlock Assignment(AssignmentExpression assignment, FlowBlock current)
    {
        current = assignment.Target switch
        {
            MemberAccessExpression member when LocalStoragePath.Of(member) is not null || member.Type is ErrorType => current,
            MemberAccessExpression member => Expression(member.Receiver, current),
            IndexExpression index => Expression(index.Index, Expression(index.Receiver, current)),
            ExtensionPropertyAccessExpression extension => Expression(extension.Receiver, current),
            _ => current,
        };

        if(!assignment.IsCoalescing)
        {
            current = Expression(assignment.Value, current);
            current.Steps.Add(Evaluate(assignment));
            return current;
        }

        current.Steps.Add(Evaluate(assignment.Target));
        var (present, absent) = Split(current);
        absent = Expression(assignment.Value, absent);
        absent.Steps.Add(Evaluate(assignment));
        return Join(present, absent);
    }

    private void Leave(FlowBlock current, int depth)
    {
        foreach(var scope in scopes.Take(scopes.Count - depth))
        {
            current.Steps.Add(new FlowStep(FlowStepKind.Leave, scope));
        }
    }

    private static FlowStep Evaluate(object node)
    {
        return new FlowStep(FlowStepKind.Evaluate, node);
    }

    private FlowBlock NewBlock()
    {
        var block = new FlowBlock(blocks.Count);
        blocks.Add(block);
        return block;
    }

    private FlowBlock Successor(FlowBlock block)
    {
        var next = NewBlock();
        block.Successors.Add(next);
        return next;
    }

    private (FlowBlock, FlowBlock) Split(FlowBlock block)
    {
        return (Successor(block), Successor(block));
    }

    private FlowBlock Join(FlowBlock left, FlowBlock right)
    {
        return Join([left, right]);
    }

    private FlowBlock Join(IReadOnlyList<FlowBlock> predecessors)
    {
        var joined = NewBlock();

        foreach(var predecessor in predecessors)
        {
            predecessor.Successors.Add(joined);
        }

        return joined;
    }
}