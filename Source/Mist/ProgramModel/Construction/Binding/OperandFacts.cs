using Mist.Generated;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using StormSyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What the binder knows of a local's value where an operand sits, as the interval set
/// (<see cref="Admitted"/>) the refinement engine already reasons in, supplied BEFORE the expression is
/// typed because proof-sensitive arithmetic fixes the type at binding and Stage 5 cannot retype a
/// finished model. A fact is only sound for a local whose value cannot change after the fact is
/// established, so facts are recorded and read only for a local that is never written in the callable:
/// a once-evaluated operand whose declaration or a guard pins its value. A local that any assignment,
/// loop step, nested write or <c>ref</c> alias could move carries no proof, which keeps a stale fact
/// from surviving a branch join, a backedge or a nested store onto the operand the division sees.
/// </summary>
internal static class OperandFacts
{
    public static Admitted FactFor(BindingContext ctx, Scope scope, LocalVariable local)
    {
        if(IsMutable(ctx, local))
        {
            return Admitted.Everything;
        }

        var store = ctx.ArithmeticFacts;
        var result = Admitted.Everything;

        for(var current = scope; current is not null; current = current.Parent)
        {
            if(current is BodyScope body && store.TryGetValue(body, out var facts) && facts.TryGetValue(local, out var values))
            {
                if(values.IsEverything)
                {
                    return result;
                }

                result = result.And(values);
            }
        }

        return result;
    }

    public static Admitted For(BindingContext ctx, Scope scope, Expression operand)
    {
        var values = Admitted.Everything;

        if(IntegerOperandValue.Exact(ctx, scope, operand) is { } constant
            && constant >= long.MinValue && constant <= long.MaxValue)
        {
            values = values.And(Admitted.Exactly((long)constant));
        }

        if(Requirement.ValuesOf(operand.Type) is { } declared)
        {
            values = values.And(declared);
        }

        if(operand is LocalReferenceExpression local)
        {
            values = values.And(FactFor(ctx, scope, local.Local));
        }

        return values;
    }

    public static void Declared(BindingContext ctx, BodyScope scope, LocalVariable local, Expression? initializer)
    {
        DecimalOperandFacts.Declared(ctx, scope, local, initializer);
        if(!IsMutable(ctx, local) && initializer is not null)
        {
            Facts(ctx, scope)[local] = For(ctx, scope, initializer);
            if(IntegerOperandValue.Exact(ctx, scope, initializer) is { } constant)
            {
                ctx.IntegerConstants[local] = constant;
            }
        }
    }

    public static void Learn(BindingContext ctx, BodyScope scope, Expression condition, bool holds)
    {
        switch(condition)
        {
            case BinaryExpression { OperatorKind: BinaryOperatorKind.LogicalAnd, Left: var left, Right: var right } when holds:
                Learn(ctx, scope, left, true);
                Learn(ctx, scope, right, true);
                return;

            case BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true } } comparison
                when Compare(comparison.OperatorKind) is { } kind:
                var effective = comparison.OperatorKind == BinaryOperatorKind.NotEqual ? !holds : holds;

                if(comparison.Left is LocalReferenceExpression subject && IntegerOperandValue.Exact(ctx, scope, comparison.Right) is { } bound
                    && bound >= long.MinValue && bound <= long.MaxValue)
                {
                    Narrow(ctx, scope, subject.Local, kind, (long)bound, effective);
                }
                else if(comparison.Right is LocalReferenceExpression mirrored && IntegerOperandValue.Exact(ctx, scope, comparison.Left) is { } limit
                    && limit >= long.MinValue && limit <= long.MaxValue)
                {
                    Narrow(ctx, scope, mirrored.Local, Mirrored(kind), (long)limit, effective);
                }

                if(comparison.Left is LocalReferenceExpression real)
                {
                    DecimalOperandFacts.Narrow(ctx, scope, real.Local, kind, comparison.Right, effective);
                }
                else if(comparison.Right is LocalReferenceExpression reverse)
                {
                    DecimalOperandFacts.Narrow(ctx, scope, reverse.Local, Mirrored(kind), comparison.Left, effective);
                }

                return;

            default:
                return;
        }
    }

    public static IReadOnlySet<string> AssignedNames(StormSyntaxNode? root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if(root is not null)
        {
            Collect(root, names);
        }

        return names;
    }

    private static void Collect(StormSyntaxNode node, HashSet<string> names)
    {
        if(node is ExpressionNode expression
            && expression.AsAssignmentOrNull() is { Group0.Operator.Lexeme: "=" or "??=" } write
            && LeadingIdentifier(write.Target) is { } name)
        {
            _ = names.Add(name);
        }

        var exposed = node switch
        {
            RefArgumentArgumentNode argument => (StormSyntaxNode)argument.Expression,
            TypedVariableDeclarationVariableDeclarationStatementNode { OwnershipSpecifier: RefSpecifierOwnershipSpecifierNode or ReadonlyRefSpecifierOwnershipSpecifierNode } alias => alias.Initializer,
            InferredVariableDeclarationVariableDeclarationStatementNode { OwnershipSpecifier: RefSpecifierOwnershipSpecifierNode or ReadonlyRefSpecifierOwnershipSpecifierNode } alias => alias.Initializer,
            ReturnStatementNode { RefKeyword: not null, Value: { } returned } => returned,
            _ => null,
        };

        if(exposed is not null)
        {
            CollectIdentifiers(exposed, names);
        }

        foreach(var child in node.Children())
        {
            Collect(child, names);
        }
    }

    private static void CollectIdentifiers(StormSyntaxNode node, HashSet<string> names)
    {
        if(node is IdentifierExpressionPrimaryNode identifier)
        {
            _ = names.Add(identifier.Name.Lexeme);
        }

        foreach(var child in node.Children())
        {
            CollectIdentifiers(child, names);
        }
    }

    private static string? LeadingIdentifier(StormSyntaxNode target)
    {
        if(target is IdentifierExpressionPrimaryNode identifier)
        {
            return identifier.Name.Lexeme;
        }

        foreach(var child in target.Children())
        {
            if(LeadingIdentifier(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    internal static bool IsMutable(BindingContext ctx, LocalVariable local)
    {
        return local.IsRef || ctx.AssignedLocals.Contains(local.Name);
    }

    private static void Narrow(BindingContext ctx, BodyScope scope, LocalVariable local, ComparisonKind kind, long constant, bool holds)
    {
        if(IsMutable(ctx, local))
        {
            return;
        }

        var interval = Requirement.Interval(kind, constant);
        Record(ctx, scope, local, holds ? interval : interval.Not());
    }

    private static void Record(BindingContext ctx, BodyScope scope, LocalVariable local, Admitted values)
    {
        var facts = Facts(ctx, scope);
        facts[local] = facts.TryGetValue(local, out var existing) && !existing.IsEverything ? existing.And(values) : values;
    }

    private static Dictionary<LocalVariable, Admitted> Facts(BindingContext ctx, BodyScope scope)
    {
        if(!ctx.ArithmeticFacts.TryGetValue(scope, out var facts))
        {
            facts = new Dictionary<LocalVariable, Admitted>(ReferenceEqualityComparer.Instance);
            ctx.ArithmeticFacts[scope] = facts;
        }

        return facts;
    }

    private static ComparisonKind? Compare(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual => ComparisonKind.Equal,
            BinaryOperatorKind.LessThan => ComparisonKind.LessThan,
            BinaryOperatorKind.GreaterThan => ComparisonKind.GreaterThan,
            BinaryOperatorKind.LessThanOrEqual => ComparisonKind.LessThanOrEqual,
            BinaryOperatorKind.GreaterThanOrEqual => ComparisonKind.GreaterThanOrEqual,
            _ => null,
        };
    }

    private static ComparisonKind Mirrored(ComparisonKind kind)
    {
        return kind switch
        {
            ComparisonKind.LessThan => ComparisonKind.GreaterThan,
            ComparisonKind.GreaterThan => ComparisonKind.LessThan,
            ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThanOrEqual,
            ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThanOrEqual,
            _ => kind,
        };
    }
}