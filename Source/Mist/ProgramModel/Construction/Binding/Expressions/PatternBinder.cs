using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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
internal static partial class PatternBinder
{
    internal static Pattern? Bind(
        PatternNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context, bool scrutineeFailed = false)
    {
        return Bind(node, scrutineeType, bindingsPermitted: true, ctx, context, scrutineeFailed);
    }

    private static Pattern? Bind(
        PatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context, bool scrutineeFailed)
    {
        return BindOr(node.OrPattern, scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed);
    }

    private static Pattern? BindOr(
        OrPatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context, bool scrutineeFailed)
    {
        if(OperatorBinder.IsBare(node.AndPatterns, node.Left, node.Right))
        {
            return BindAnd(node.Left, scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed);
        }

        var operands = new List<Pattern>();

        foreach(var operand in GrammarRepetition.LeadAndRepeated(node.Left, node.AndPatterns))
        {
            if(BindAnd(operand, scrutineeType, bindingsPermitted: false, ctx, context, scrutineeFailed) is not { } bound)
            {
                return null;
            }

            operands.Add(bound);
        }

        return new DisjunctionPattern(node, scrutineeType, operands) { IsFailed = scrutineeFailed || operands.Any(operand => operand.IsFailed) };
    }

    private static Pattern? BindAnd(
        AndPatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context, bool scrutineeFailed)
    {
        if(OperatorBinder.IsBare(node.UnaryPatterns, node.Left, node.Right))
        {
            return BindUnary(node.Left, scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed);
        }

        return BindConjunction(node, GrammarRepetition.LeadAndRepeated(node.Left, node.UnaryPatterns), scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed);
    }

    private static Pattern? BindConjunction(
        SyntaxNode node, List<UnaryPatternNode> unaryPatterns, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context, bool scrutineeFailed)
    {
        var operands = new List<Pattern>();
        var narrowed = scrutineeType;

        // Each operand sees what its predecessors proved, not the original scrutinee, so `Dog and var d`
        // binds d as Dog. C# narrows a conjunction's input the same way, and emission hands the pattern
        // straight to it -- typing d against the wider type here would put the model at odds with the C#
        // it produced.
        foreach(var operand in unaryPatterns)
        {
            if(BindUnary(operand, narrowed, bindingsPermitted, ctx, context, scrutineeFailed) is not { } bound)
            {
                return null;
            }

            operands.Add(bound);
            scrutineeFailed |= bound.IsFailed;

            if(ConversionRelation.IsWithin(bound.NarrowedType, narrowed))
            {
                narrowed = bound.NarrowedType;
            }
        }

        return new ConjunctionPattern(node, narrowed, operands) { IsFailed = scrutineeFailed || operands.Any(operand => operand.IsFailed) };
    }

    private static Pattern? BindUnary(
        UnaryPatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context, bool scrutineeFailed)
    {
        return node switch
        {
            DirectPatternUnaryPatternNode direct =>
                BindPrimary(direct.Primary, scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed),
            NotPatternUnaryPatternNode negated => Negate(negated, scrutineeType, ctx, context, scrutineeFailed),
            _ => null,
        };
    }

    private static Pattern? Negate(
        NotPatternUnaryPatternNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context, bool scrutineeFailed)
    {
        return BindPrimary(node.Operand, scrutineeType, bindingsPermitted: false, ctx, context, scrutineeFailed) is { } operand
            ? new NegatedPattern(node, scrutineeType, operand) { IsFailed = scrutineeFailed || operand.IsFailed }
            : null
            ;
    }

    private static Pattern? BindPrimary(
        PrimaryPatternNode node, IMistType scrutineeType, bool bindingsPermitted, BindingContext ctx, BuilderContext context, bool scrutineeFailed)
    {
        switch(node)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    var resolution = context.Resolver.ResolveWithFailure(typePattern.Type, ctx.Scope);
                    var tested = CloneConversionBody.Tested(resolution.Type, scrutineeType, ctx.Scope);
                    var failed = scrutineeFailed || resolution.IsFailed;

                    if(tested is NamedTupleType namedTuple)
                    {
                        return BindNamedTupleDeconstruction(
                            typePattern, namedTuple, typePattern.Name, scrutineeType, bindingsPermitted, ctx, context, failed);
                    }

                    var binding = Declare(node, typePattern.Name, tested, bindingsPermitted, ctx, context, failed);
                    return BindTypeTest(node, tested, binding, scrutineeType, ctx, context, failed);
                }

            case BareTypePatternPrimaryPatternNode bareType:
                return BindBareType(node, bareType, scrutineeType, ctx, context, scrutineeFailed);

            case VarPatternPrimaryPatternNode varPattern:
                return new CatchAllPattern(
                    node,
                    scrutineeType,
                    Declare(node, varPattern.Name, scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed))
                { IsFailed = scrutineeFailed };

            case DiscardPatternPrimaryPatternNode:
                return new CatchAllPattern(node, scrutineeType, binding: null) { IsFailed = scrutineeFailed };

            case PropertyPatternPrimaryPatternNode propertyPattern:
                return BindPropertyPattern(propertyPattern, scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed);

            case TuplePositionalPatternPrimaryPatternNode tuplePattern:
                return BindTuplePattern(tuplePattern, scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed);

            case ListPatternPrimaryPatternNode listPattern:
                return BindListPattern(listPattern, scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed);

            case ParenthesizedPatternPrimaryPatternNode paren:
                return Bind(paren.Inner, scrutineeType, bindingsPermitted, ctx, context, scrutineeFailed);

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
        BuilderContext context, bool scrutineeFailed)
    {
        if(EnumVariant(node, bareType.Type, ctx, context) is { } variant)
        {
            return new ComparisonPattern(node, scrutineeType, ComparisonKind.Equal, variant);
        }

        var resolution = context.Resolver.ResolveWithFailure(bareType.Type, ctx.Scope);

        return BindTypeTest(
            node,
            CloneConversionBody.Tested(resolution.Type, scrutineeType, ctx.Scope),
            binding: null,
            scrutineeType,
            ctx,
            context,
            scrutineeFailed || resolution.IsFailed);
    }

    /// <summary>
    /// A refined tested type's predicate, bound against the type it refines. Null for a type
    /// carrying no refinement, and null when the predicate does not bind -- which the emitter must
    /// treat as a refusal rather than as an absent one, since erasing an unbound predicate would
    /// let the test succeed for every value.
    /// </summary>
    internal static Pattern? BindRefinement(IMistType tested, BindingContext ctx, BuilderContext context)
    {
        if(tested is not RefinedType refined)
        {
            return null;
        }

        if(refined.Predicate is { } bound)
        {
            return bound;
        }

        return refined.PredicateNode switch
        {
            SimpleRefinementPredicateRefinementPredicateNode simple => BindAnd(simple.Predicate, refined.BaseType, bindingsPermitted: false, ctx, context, false),
            AndPatternNode adhoc => BindAnd(adhoc, refined.BaseType, bindingsPermitted: false, ctx, context, false),
            CompoundRefinementPredicateRefinementPredicateNode compound => BindConjunction(
                compound, GrammarRepetition.LeadAndRepeated(compound.First, compound.UnaryPatterns), refined.BaseType, bindingsPermitted: false, ctx, context, false),
            _ => null,
        };
    }

    internal static Pattern? BindPredicate(AndPatternNode predicate, IMistType baseType, BindingContext ctx, BuilderContext context)
    {
        return BindAnd(predicate, baseType, bindingsPermitted: false, ctx, context, false);
    }

    private static Expression? EnumVariant(
        PrimaryPatternNode node, TypeReferenceNode typeRef, BindingContext ctx, BuilderContext context)
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
            || enumType.MembersList.Find(member => member.Name == variantName) is not { } variant)
        {
            return null;
        }

        context.ProgramModel.TypeNamings.Record(enumType, ctx.Scope, node);

        return new MemberAccessExpression(
            node, new TypeReferenceExpression(node, enumType), variantName, enumType, member: new EnumMemberResult(variant, enumType));
    }

    private static TypeTestPattern BindTypeTest(
        PrimaryPatternNode node,
        IMistType tested,
        LocalVariable? binding,
        IMistType scrutineeType,
        BindingContext ctx,
        BuilderContext context,
        bool isFailed = false)
    {
        var refinement = ComposeRefinement(tested, node, refined => BindRefinement(refined, ctx, context));

        if(scrutineeType is not UnionType union || OptionalUnion.IsOptional(union))
        {
            return new TypeTestPattern(node, tested, binding, constituent: null, refinement)
            {
                TestsAbsence = OptionalUnion.IsOptional(scrutineeType) && OptionalUnion.IsVoid(tested),
                IsFailed = isFailed,
            };
        }

        if(MatchConstituent(union, tested) is { } constituent)
        {
            return new TypeTestPattern(node, tested, binding, constituent, refinement) { IsFailed = isFailed };
        }

        context.Diagnostics.ReportAtNode(
            ProgramModelDiagnostics.PatternTypeNotAConstituent, node, null, tested.Name, union.Name);

        return new TypeTestPattern(node, ErrorType.Instance, binding, constituent: null) { IsFailed = true };
    }

    private static Pattern? BindTuplePattern(
        TuplePositionalPatternPrimaryPatternNode node,
        IMistType scrutineeType,
        bool bindingsPermitted,
        BindingContext ctx,
        BuilderContext context, bool scrutineeFailed)
    {
        var elementNodes = GrammarRepetition.TwoLeadsAndRepeated(node.First, node.Rest, node.Patterns);
        var matched = OptionalLifting.NonVoidPortion(scrutineeType, context.ProgramModel.TypeInterner);
        var scrutineeElements = (matched as ITupleType)?.ElementTypes;

        if(scrutineeElements is { } held && held.Count != elementNodes.Count)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.TuplePatternArityMismatch, node, null,
                matched.Name, held.Count, elementNodes.Count);

            return new TuplePattern(node, [], ErrorType.Instance, testedType: null, binding: null) { IsFailed = true };
        }

        if(scrutineeElements is null && matched is not (UnknownType or ErrorType))
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.TuplePatternScrutineeNotATuple, node, null, matched.Name);

            return new TuplePattern(node, [], ErrorType.Instance, testedType: null, binding: null) { IsFailed = true };
        }

        var elements = new List<Pattern>(elementNodes.Count);

        for(var i = 0; i < elementNodes.Count; i++)
        {
            var elementScrutinee = scrutineeElements is null
                ? matched is ErrorType ? ErrorType.Instance : UnknownType.Instance
                : scrutineeElements[i];

            if(Bind(elementNodes[i], elementScrutinee, bindingsPermitted, ctx, context, scrutineeFailed) is not { } bound)
            {
                return null;
            }

            elements.Add(bound);
        }

        if(scrutineeElements is not null)
        {
            return new TuplePattern(node, elements, matched, testedType: null, binding: null) { IsFailed = scrutineeFailed || elements.Any(element => element.IsFailed) };
        }

        if(matched is ErrorType)
        {
            return new TuplePattern(node, elements, ErrorType.Instance, testedType: null, binding: null) { IsFailed = scrutineeFailed || elements.Any(element => element.IsFailed) };
        }

        return TupleFromElementPatterns(elements, context) is { } tested
            ? new TuplePattern(node, elements, tested, tested, binding: null) { IsFailed = scrutineeFailed || elements.Any(element => element.IsFailed) }
            : null
            ;
    }

    /// <summary>
    /// <c>[p, .. q, r]</c>, whose two element kinds scrutinee different types -- see
    /// <see cref="ListPattern"/>. The minimum length matching requires is NOT computed here: C# derives
    /// it from the pattern's own shape, so stating it again would be a second authority on the same
    /// fact.
    /// <para>
    /// An <c>unknown</c> scrutinee is REFUSED where a tuple pattern would type-test it, and the
    /// asymmetry is C#'s rather than a gap here. A tuple pattern can name the ValueTuple ahead of
    /// itself, but <c>[...]</c> is the only spelling C# has for a list pattern and it requires a
    /// countable scrutinee at compile time -- so there is no type to name first, and the test would
    /// have to become <c>is int[] and [...]</c>, a second emission shape.
    /// </para>
    /// </summary>
    private static Pattern? BindListPattern(
        ListPatternPrimaryPatternNode node,
        IMistType scrutineeType,
        bool bindingsPermitted,
        BindingContext ctx,
        BuilderContext context, bool scrutineeFailed)
    {
        var matched = OptionalLifting.NonVoidPortion(scrutineeType, context.ProgramModel.TypeInterner);
        var elementType = Indexability.ElementType(matched, Indexability.Position(node, context), context);

        if(elementType is ErrorType && matched is not ErrorType)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ListPatternScrutineeNotIndexable, node, null, matched.Name);

            return new ListPattern(node, [], ErrorType.Instance) { IsFailed = true };
        }

        CountedIndexer? reads = null;

        if(Indexability.ArrayElement(matched) is null
            && Indexability.Select(Indexability.Indexers(matched), matched, Indexability.Position(node, context), context)
                is { Selected: { Modifiers.IsIntrinsic: false } selected } choice)
        {
            var position = choice.Substitution.Apply(selected.ParametersList[0].Type, context.ProgramModel.TypeInterner);

            if(Indexability.Count(matched) is not { } count)
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.ReceiverNotCountable, node, null, TypeDisplay.Mist(matched));
            }
            else if(!Indexability.IsIndex(position))
            {
                reads = new CountedIndexer(selected, position, count);
            }
        }

        var elements = new List<ListPatternElement>();
        var sliceSeen = false;

        foreach(var elementNode in Elements(node.Elements))
        {
            if(elementNode is SliceElementListPatternElementNode slice)
            {
                if(sliceSeen)
                {
                    context.Diagnostics.ReportAtNode(
                        ProgramModelDiagnostics.ListPatternMultipleSlices, node, null);

                    return new ListPattern(node, [], ErrorType.Instance) { IsFailed = true };
                }

                sliceSeen = true;

                if(slice.Subpattern is null)
                {
                    elements.Add(new ListPatternElement(Subpattern: null, IsSlice: true, ScrutineeType: null));
                    continue;
                }

                var sliceType = matched is ErrorType ? ErrorType.Instance : Indexability.SliceType(matched, context);

                if(sliceType is ErrorType && matched is not ErrorType)
                {
                    context.Diagnostics.ReportAtNode(
                        ProgramModelDiagnostics.ReceiverNotSliceable, node, null, matched.Name);

                    return new ListPattern(node, [], ErrorType.Instance) { IsFailed = true };
                }

                if(Bind(slice.Subpattern, sliceType, bindingsPermitted, ctx, context, scrutineeFailed) is not { } boundSlice)
                {
                    return null;
                }

                elements.Add(new ListPatternElement(boundSlice, IsSlice: true, sliceType));
                continue;
            }

            if(elementNode is not PatternElementListPatternElementNode positional
                || Bind(positional.Pattern, elementType, bindingsPermitted, ctx, context, scrutineeFailed) is not { } bound)
            {
                return null;
            }

            elements.Add(new ListPatternElement(bound, IsSlice: false, elementType));
        }

        return new ListPattern(node, elements, matched) { Reads = reads, IsFailed = scrutineeFailed || elements.Any(element => element.Subpattern?.IsFailed is true) };
    }

    /// <summary>
    /// The tuple type a pattern proves its scrutinee holds, read off what each element pattern narrows
    /// to. Null where any element narrows to nothing nameable, which is every element testing a VALUE
    /// rather than a type: <c>u is (0, 0)</c> on an <c>unknown</c> says the elements equal zero and not
    /// which tuple holds them. DO NOT substitute <c>unknown</c> for the unnameable element to get an
    /// answer -- the test would emit, compile, and never match, the runtime value being a tuple of ints
    /// and not of objects.
    /// </summary>
    private static IMistType? TupleFromElementPatterns(List<Pattern> elements, BuilderContext context)
    {
        foreach(var element in elements)
        {
            if(element.NarrowedType is UnknownType or ErrorType)
            {
                return null;
            }
        }

        return context.ProgramModel.TypeInterner.GetOrCreateTupleType(
            elements.ConvertAll(element => element.NarrowedType));
    }

    /// <summary>
    /// <c>(int x, int y) whole</c>, which the grammar hands over as a TYPE pattern over a named tuple
    /// rather than as a tuple pattern: <c>(int x, int y)</c> is a valid type reference and the type
    /// alternative is tried first, so the element names arrive as names ON THE TYPE. They mean locals,
    /// as they do in C#, and lifting them here is what makes the three bindings the source spells
    /// reachable at all.
    /// <para>
    /// The whole-value binding is typed by the UNDERLYING tuple, so <c>whole.x</c> deliberately does not
    /// resolve -- the names became locals and are not also members of what <c>whole</c> holds.
    /// </para>
    /// </summary>
    private static Pattern BindNamedTupleDeconstruction(
        TypePatternPrimaryPatternNode node,
        NamedTupleType namedTuple,
        Token name,
        IMistType scrutineeType,
        bool bindingsPermitted,
        BindingContext ctx,
        BuilderContext context, bool scrutineeFailed)
    {
        var elementTypes = namedTuple.Underlying.ElementTypes;
        var elements = new List<Pattern>(elementTypes.Count);
        var elementSites = node.Type.First is TupleSingleTypeRefSingleTypeRefNode tuple
            ? new List<TupleTypeElementNode> { tuple.First, tuple.Rest }.Concat(tuple.TupleTypeElements).ToList()
            : null;

        for(var i = 0; i < elementTypes.Count; i++)
        {
            var elementName = i < namedTuple.ElementNames.Count ? namedTuple.ElementNames[i] : null;
            var elementSite = elementSites is not null && i < elementSites.Count ? elementSites[i] : null;

            var elementBinding = elementName is null
                ? null
                : DeclareNamed(
                    elementSite ?? (SyntaxNode)node, elementName, elementTypes[i], bindingsPermitted, ctx, context, scrutineeFailed,
                    elementSite?.Name?.Span)
                ;

            elements.Add(new CatchAllPattern(node, elementTypes[i], elementBinding));
        }

        var matched = OptionalLifting.NonVoidPortion(scrutineeType, context.ProgramModel.TypeInterner);

        return new TuplePattern(
            node,
            elements,
            namedTuple.Underlying,
            matched is ITupleType ? null : namedTuple.Underlying,
            Declare(node, name, namedTuple.Underlying, bindingsPermitted, ctx, context, scrutineeFailed))
        { IsFailed = scrutineeFailed };
    }

    private static IReadOnlyList<ListPatternElementNode> Elements(ListPatternElementListNode? list)
    {
        return list is null ? [] : GrammarRepetition.LeadAndRepeated(list.ListPatternElement, list.ListPatternElements);
    }

    /// <summary>
    /// The constituent a test narrows through. Identity is tried across ALL constituents before
    /// containment is tried at all: <c>Animal | int</c> narrows to Dog through the Animal accessor, but
    /// an exact constituent must never be answered by a wider one that merely also contains it.
    /// </summary>
    internal static IMistType? MatchConstituent(UnionType union, IMistType tested)
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
        // Not a comparison like every other constant: a regex says how a value must LOOK, and
        // nothing it could be compared against exists.
        if(node.Literal is RegexConstantConstantLiteralNode regex)
        {
            var lexeme = regex.Value.Lexeme;
            var raw = lexeme.Length >= 2 ? lexeme[1..^1] : lexeme;
            return new RegexPattern(node, scrutineeType, LiteralBinder.Quoted(raw));
        }

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

}