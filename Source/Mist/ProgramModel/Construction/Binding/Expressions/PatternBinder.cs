using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Patterns;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Resolves the pattern of an <c>is</c> expression, walking the same or/and/unary/primary chain the
/// grammar spells. Type tests and the three catch-all forms are carried; every other form answers
/// null, so the caller parks the whole <c>is</c> unresolved rather than holding a half-bound pattern
/// that emission would have to second-guess.
/// <para>
/// A binding is declared into the scope in effect as the pattern binds, which is the scope the caller
/// chose for it -- see <see cref="StatementBinder"/>, which makes that the branch's.
/// </para>
/// </summary>
internal static class PatternBinder
{
    internal static Pattern? Bind(
        PatternNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context)
    {
        return BindOr(node.OrPattern, scrutineeType, ctx, context);
    }

    private static Pattern? BindOr(
        OrPatternNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context)
    {
        if(!OperatorBinder.IsBare(node.AndPatterns, node.Left, node.Right))
        {
            return null;
        }

        return BindAnd(node.Left, scrutineeType, ctx, context);
    }

    private static Pattern? BindAnd(
        AndPatternNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context)
    {
        if(!OperatorBinder.IsBare(node.UnaryPatterns, node.Left, node.Right))
        {
            return null;
        }

        return BindUnary(node.Left, scrutineeType, ctx, context);
    }

    private static Pattern? BindUnary(
        UnaryPatternNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context)
    {
        return node is DirectPatternUnaryPatternNode direct
            ? BindPrimary(direct.Primary, scrutineeType, ctx, context)
            : null
            ;
    }

    private static Pattern? BindPrimary(
        PrimaryPatternNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context)
    {
        switch(node)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    var tested = context.Resolver.Resolve(typePattern.Type, ctx.Scope);
                    return new TypeTestPattern(node, tested, Declare(typePattern.Name, tested, ctx, context));
                }

            case BareTypePatternPrimaryPatternNode bareType:
                return new TypeTestPattern(
                    node, context.Resolver.Resolve(bareType.Type, ctx.Scope), binding: null);

            case VarPatternPrimaryPatternNode varPattern:
                return new CatchAllPattern(
                    node, scrutineeType, Declare(varPattern.Name, scrutineeType, ctx, context));

            case DiscardPatternPrimaryPatternNode:
                return new CatchAllPattern(node, scrutineeType, binding: null);

            default:
                return null;
        }
    }

    /// <summary>
    /// Declares a pattern's binding, or answers null where the pattern keeps nothing. The grammar puts
    /// the discard in the SAME slot as a name (<c>name:(IDENTIFIER | DISCARD)</c>), so <c>int _</c>
    /// arrives as a type pattern carrying a discard token, never as a discard pattern, and the token's
    /// kind is the only thing separating the two readings.
    /// </summary>
    private static LocalVariable? Declare(
        Token name, IMistType type, BindingContext ctx, BuilderContext context)
    {
        if(name.Kind == (int)TokenKind.DISCARD)
        {
            return null;
        }

        var local = new LocalVariable(name.Lexeme, type);
        ctx.Scope.DeclareLocal(local, context.Diagnostics);
        return local;
    }
}