using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Resolves the pattern of an <c>is</c> expression, walking the same or/and/unary/primary chain the
/// grammar spells. A form not yet carried answers null, so the caller parks the whole <c>is</c>
/// unresolved rather than holding a half-bound pattern that emission would have to second-guess.
/// <para>
/// A binding is declared into the scope in effect as the pattern binds, which is the scope the caller
/// chose for it -- see <see cref="StatementBinder"/>, which makes that the guarded block's. Whether a
/// binding may be introduced at all travels down as <c>bindingsPermitted</c>: <c>or</c> and <c>not</c>
/// clear it, because matching under either establishes no value for a name to hold.
/// </para>
/// <para>
/// Grouping is NOT modelled. A parenthesized pattern binds to whatever it wraps, and emission
/// re-derives the parentheses from precedence, as it does for expressions.
/// </para>
/// </summary>
internal static class PatternBinder
{
    internal static Pattern? Bind(
        PatternNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context)
    {
        return Bind(node, scrutineeType, bindingsPermitted: true, ctx, context);
    }

    private static Pattern? Bind(
        PatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context)
    {
        return BindOr(node.OrPattern, scrutineeType, bindingsPermitted, ctx, context);
    }

    private static Pattern? BindOr(
        OrPatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context)
    {
        if(OperatorBinder.IsBare(node.AndPatterns, node.Left, node.Right))
        {
            return BindAnd(node.Left, scrutineeType, bindingsPermitted, ctx, context);
        }

        var operands = new List<Pattern>();

        foreach(var operand in Flatten(node.Left, node.AndPatterns))
        {
            if(BindAnd(operand, scrutineeType, bindingsPermitted: false, ctx, context) is not { } bound)
            {
                return null;
            }

            operands.Add(bound);
        }

        return new DisjunctionPattern(node, scrutineeType, operands);
    }

    private static Pattern? BindAnd(
        AndPatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context)
    {
        if(OperatorBinder.IsBare(node.UnaryPatterns, node.Left, node.Right))
        {
            return BindUnary(node.Left, scrutineeType, bindingsPermitted, ctx, context);
        }

        var operands = new List<Pattern>();
        var narrowed = scrutineeType;

        // Each operand sees what its predecessors proved, not the original scrutinee, so `Dog and var d`
        // binds d as Dog. C# narrows a conjunction's input the same way, and emission hands the pattern
        // straight to it -- typing d against the wider type here would put the model at odds with the C#
        // it produced.
        foreach(var operand in Flatten(node.Left, node.UnaryPatterns))
        {
            if(BindUnary(operand, narrowed, bindingsPermitted, ctx, context) is not { } bound)
            {
                return null;
            }

            operands.Add(bound);

            if(ConversionRelation.IsWithin(bound.NarrowedType, narrowed))
            {
                narrowed = bound.NarrowedType;
            }
        }

        return new ConjunctionPattern(node, narrowed, operands);
    }

    private static Pattern? BindUnary(
        UnaryPatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context)
    {
        return node switch
        {
            DirectPatternUnaryPatternNode direct =>
                BindPrimary(direct.Primary, scrutineeType, bindingsPermitted, ctx, context),
            NotPatternUnaryPatternNode negated => Negate(negated, scrutineeType, ctx, context),
            _ => null,
        };
    }

    private static Pattern? Negate(
        NotPatternUnaryPatternNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context)
    {
        return BindPrimary(node.Operand, scrutineeType, bindingsPermitted: false, ctx, context) is { } operand
            ? new NegatedPattern(node, scrutineeType, operand)
            : null
            ;
    }

    private static Pattern? BindPrimary(
        PrimaryPatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context)
    {
        switch(node)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    var tested = context.Resolver.Resolve(typePattern.Type, ctx.Scope);
                    return new TypeTestPattern(
                        node, tested, Declare(node, typePattern.Name, tested, bindingsPermitted, ctx, context));
                }

            case BareTypePatternPrimaryPatternNode bareType:
                return new TypeTestPattern(
                    node, context.Resolver.Resolve(bareType.Type, ctx.Scope), binding: null);

            case VarPatternPrimaryPatternNode varPattern:
                return new CatchAllPattern(
                    node,
                    scrutineeType,
                    Declare(node, varPattern.Name, scrutineeType, bindingsPermitted, ctx, context));

            case DiscardPatternPrimaryPatternNode:
                return new CatchAllPattern(node, scrutineeType, binding: null);

            case ParenthesizedPatternPrimaryPatternNode paren:
                return Bind(paren.Inner, scrutineeType, bindingsPermitted, ctx, context);

            case ConstantPatternPrimaryPatternNode constant:
                return BindConstant(constant, scrutineeType, context);

            case LessThanRelationalPatternPrimaryPatternNode lessThan:
                return BindComparison(
                    node, lessThan.Constant, ComparisonKind.LessThan, scrutineeType, ctx, context);

            case GreaterThanRelationalPatternPrimaryPatternNode greaterThan:
                return BindComparison(
                    node, greaterThan.Constant, ComparisonKind.GreaterThan, scrutineeType, ctx, context);

            case LessThanOrEqualRelationalPatternPrimaryPatternNode lessOrEqual:
                return BindComparison(
                    node, lessOrEqual.Constant, ComparisonKind.LessThanOrEqual, scrutineeType, ctx, context);

            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode greaterOrEqual:
                return BindComparison(
                    node, greaterOrEqual.Constant, ComparisonKind.GreaterThanOrEqual, scrutineeType, ctx, context);

            default:
                return null;
        }
    }

    private static Pattern? BindConstant(
        ConstantPatternPrimaryPatternNode node, IMistType scrutineeType, BuilderContext context)
    {
        var bound = node.Literal switch
        {
            IntegerConstantConstantLiteralNode integer =>
                LiteralBinder.BindIntegerLiteral(node, integer.Value.Lexeme, context),
            FloatConstantConstantLiteralNode floatConstant =>
                LiteralBinder.BindFloatLiteral(node, floatConstant.Value.Lexeme, context),
            StringConstantConstantLiteralNode stringConstant =>
                LiteralBinder.BindStringLiteral(node, stringConstant.Value.Lexeme, context),
            CharConstantConstantLiteralNode charConstant =>
                LiteralBinder.BindCharLiteral(node, charConstant.Value.Lexeme, context),
            TrueConstantConstantLiteralNode => LiteralBinder.BindBoolLiteral(node, true, context),
            FalseConstantConstantLiteralNode => LiteralBinder.BindBoolLiteral(node, false, context),
            _ => null,
        };

        return bound is null
            ? null
            : new ComparisonPattern(node, scrutineeType, ComparisonKind.Equal, bound)
            ;
    }

    private static Pattern BindComparison(
        SyntaxNode node,
        VoidCoalescingNode constant,
        ComparisonKind kind,
        IMistType scrutineeType,
        BindingContext ctx,
        BuilderContext context)
    {
        Expression bound;
        using(ctx.PushTargetType(scrutineeType))
        {
            bound = OperatorBinder.BindVoidCoalescing(constant, ctx, context);
        }

        return new ComparisonPattern(node, scrutineeType, kind, bound);
    }

    /// <summary>
    /// Left followed by every repeated operand. The generated Right slot holds the LAST repetition
    /// element and that element is ALSO in the array, so a fold reading Left and Right silently drops
    /// everything between them -- which is how the classic emitter loses the <c>b</c> of
    /// <c>a or b or c</c>.
    /// </summary>
    private static List<T> Flatten<T>(T left, ImmutableArray<T> repeated)
        where T : class
    {
        var operands = new List<T>(1 + repeated.Length) { left };
        operands.AddRange(repeated);

        return operands;
    }

    /// <summary>
    /// Declares a pattern's binding, or answers null where the pattern keeps nothing. The grammar puts
    /// the discard in the SAME slot as a name (<c>name:(IDENTIFIER | DISCARD)</c>), so <c>int _</c>
    /// arrives as a type pattern carrying a discard token, never as a discard pattern, and the token's
    /// kind is the only thing separating the two readings.
    /// </summary>
    private static LocalVariable? Declare(
        SyntaxNode node,
        Token name,
        IMistType type,
        bool bindingsPermitted,
        BindingContext ctx,
        BuilderContext context)
    {
        if(name.Kind == (int)TokenKind.DISCARD)
        {
            return null;
        }

        if(!bindingsPermitted)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.PatternBindingNotEstablished, node, null, name.Lexeme);

            return null;
        }

        var local = new LocalVariable(name.Lexeme, type);
        ctx.Scope.DeclareLocal(local, context.Diagnostics);

        return local;
    }
}