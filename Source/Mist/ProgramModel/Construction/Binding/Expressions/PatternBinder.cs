using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
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
                    var binding = Declare(node, typePattern.Name, tested, bindingsPermitted, ctx, context);
                    return BindTypeTest(node, tested, binding, scrutineeType, context);
                }

            case BareTypePatternPrimaryPatternNode bareType:
                return BindBareType(node, bareType, scrutineeType, ctx, context);

            case VarPatternPrimaryPatternNode varPattern:
                return new CatchAllPattern(
                    node,
                    scrutineeType,
                    Declare(node, varPattern.Name, scrutineeType, bindingsPermitted, ctx, context));

            case DiscardPatternPrimaryPatternNode:
                return new CatchAllPattern(node, scrutineeType, binding: null);

            case PropertyPatternPrimaryPatternNode propertyPattern:
                return BindPropertyPattern(propertyPattern, scrutineeType, bindingsPermitted, ctx, context);

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

    /// <summary>
    /// A DOTTED bare name reads as an enum variant before it reads as a type, and that order is forced:
    /// a Mist enum variant is a VALUE, so <c>Direction.North</c> names no type at all, and resolving it
    /// as one reports an unresolved type before the value reading is ever tried. The probe therefore
    /// goes through the scope's own lookup rather than <see cref="ScopeResolver.ResolveNamedType"/>,
    /// which reports what it cannot find.
    /// <para>
    /// What comes out is an ordinary equality comparison, because that is what matching a variant IS --
    /// the same <see cref="ComparisonPattern"/> a constant pattern binds to, differing only in what the
    /// bound expression is. Emission owns the consequence: a variant lowers to a static readonly field,
    /// which C# will not admit as a constant pattern.
    /// </para>
    /// </summary>
    private static Pattern BindBareType(
        PrimaryPatternNode node,
        BareTypePatternPrimaryPatternNode bareType,
        IMistType scrutineeType,
        BindingContext ctx,
        BuilderContext context)
    {
        if(EnumVariant(node, bareType.Type, ctx) is { } variant)
        {
            return new ComparisonPattern(node, scrutineeType, ComparisonKind.Equal, variant);
        }

        return BindTypeTest(
            node,
            context.Resolver.Resolve(bareType.Type, ctx.Scope),
            binding: null,
            scrutineeType,
            context);
    }

    private static Expression? EnumVariant(
        PrimaryPatternNode node, TypeReferenceNode typeRef, BindingContext ctx)
    {
        if(typeRef.SingleTypeRefs.Length > 0
            || typeRef.First is not NamedSingleTypeRefSingleTypeRefNode
            { TypeArgs: null, ArrayRank: null, Optional: null } named
            || named.Name.IDENTIFIERs.IsDefaultOrEmpty)
        {
            return null;
        }

        var qualifiedName = QualifiedNameHelper.Build(named.Name);
        var lastDot = qualifiedName.LastIndexOf('.');

        if(lastDot <= 0)
        {
            return null;
        }

        var variantName = qualifiedName[(lastDot + 1)..];

        if(ctx.Scope.Lookup(new LookupRequest(qualifiedName[..lastDot], MemberVisibility.None, node))
            is not TypeResult { Type: EnumType enumType }
            || !enumType.MembersList.Exists(member => member.Name == variantName))
        {
            return null;
        }

        return new MemberAccessExpression(
            node, new TypeReferenceExpression(node, enumType), variantName, enumType);
    }

    private static Pattern BindTypeTest(
        PrimaryPatternNode node,
        IMistType tested,
        LocalVariable? binding,
        IMistType scrutineeType,
        BuilderContext context)
    {
        if(scrutineeType is not UnionType union || OptionalUnion.IsOptional(union))
        {
            return new TypeTestPattern(node, tested, binding);
        }

        if(MatchConstituent(union, tested) is { } constituent)
        {
            return new TypeTestPattern(node, tested, binding, constituent);
        }

        context.Diagnostics.ReportAtNode(
            ProgramModelDiagnostics.PatternTypeNotAConstituent, node, null, tested.Name, union.Name);

        // ErrorType rather than the tested type, so BindIs answers with an ErrorExpression through the
        // check it already makes instead of needing a second channel for "reported but shaped fine".
        return new TypeTestPattern(node, ErrorType.Instance, binding, constituent: null);
    }

    /// <summary>
    /// A subpattern names a MEMBER, resolved against the scrutinee's own type -- which is the whole of
    /// what makes <c>{ Length: &gt; 3 }</c> mean something on a string -- and the member's type is then
    /// the subpattern's scrutinee, so nesting narrows progressively without this method counting depth.
    /// <para>
    /// Two of the grammar's slots are refused rather than ignored. A leading type name is unreachable
    /// because BareTypePattern claims the identifier first in the ordered choice, and a trailing binding
    /// is unreachable because the parser generator emits no code for an optional group -- so filling
    /// either one means the grammar changed, and parking says so instead of quietly dropping a type test
    /// or a binder that the source did spell.
    /// </para>
    /// </summary>
    private static Pattern? BindPropertyPattern(
        PropertyPatternPrimaryPatternNode node,
        IMistType scrutineeType,
        bool bindingsPermitted,
        BindingContext ctx,
        BuilderContext context)
    {
        if(node.TypeName is not null || node.Name is not null)
        {
            return null;
        }

        var matched = OptionalLifting.NonVoidPortion(scrutineeType, context.ProgramModel.TypeInterner);
        var subpatterns = new List<PropertySubpattern>();
        var narrowed = matched;

        foreach(var subpattern in Subpatterns(node.Subpatterns))
        {
            var memberName = subpattern.Name.Lexeme;
            var (memberType, found) = MemberResolution.ResolveStorage(matched, memberName, subpattern, context);

            if(memberType is null)
            {
                if(!MemberResolution.AnswersMembers(matched))
                {
                    return null;
                }

                ReportSubpatternNotStorage(found, matched, memberName, subpattern, context);
                narrowed = ErrorType.Instance;
                continue;
            }

            if(Bind(subpattern.Pattern, memberType, bindingsPermitted, ctx, context) is not { } bound)
            {
                return null;
            }

            subpatterns.Add(new PropertySubpattern(memberName, bound));
        }

        return new PropertyPattern(node, narrowed, subpatterns);
    }

    private static void ReportSubpatternNotStorage(
        LookupResult found,
        IMistType matched,
        string memberName,
        SyntaxNode node,
        BuilderContext context)
    {
        if(found is MethodGroupResult or TypeResult or EnumMemberResult)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.PatternSubpatternNotStorage, node, null, matched.Name, memberName);
        }
    }

    private static IReadOnlyList<PropertySubpatternNode> Subpatterns(PropertySubpatternListNode? list)
    {
        return list is null ? [] : Flatten(list.PropertySubpattern, list.PropertySubpatterns);
    }

    /// <summary>
    /// The constituent a test narrows through. Identity is tried across ALL constituents before
    /// containment is tried at all: <c>Animal | int</c> narrows to Dog through the Animal accessor, but
    /// an exact constituent must never be answered by a wider one that merely also contains it.
    /// </summary>
    private static IMistType? MatchConstituent(UnionType union, IMistType tested)
    {
        foreach(var constituent in union.Constituents)
        {
            if(ReferenceEquals(constituent, tested))
            {
                return constituent;
            }
        }

        foreach(var constituent in union.Constituents)
        {
            if(ConversionRelation.IsWithin(tested, constituent))
            {
                return constituent;
            }
        }

        return null;
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