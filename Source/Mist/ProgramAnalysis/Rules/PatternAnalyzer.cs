using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The pattern questions that are assignability's question asked of a test rather than a value: a type
/// test no value of the input could pass, a relational or constant bound the
/// input cannot be compared with or that is not a constant, and an arm an earlier arm already covers.
/// Order 8 of the analysis register. Each pattern is judged against the type flowing INTO it -- the
/// scrutinee's, a conjunct's predecessor's narrowing, a member's, a tuple element's -- and an input
/// that is indeterminate, <c>unknown</c> above all, admits any test, since that is what a test of it
/// is for. A test of an unsealed class against an abstract is possible whichever side is which,
/// because a derivation may implement it, which is C#'s rule and classic's verdict.
/// <para>
/// A type test, an <c>as</c> or a cast whose target names a type parameter the operand's static type
/// does not is judged once more at every closing of that parameter (purity Requirement 16.5): the
/// erased runtime test would read a plain function value as a pure one, so no closing may hand the
/// parameter a type holding a pure function type -- through a forwarding closing of an enclosing
/// generic's parameter included.
/// </para>
/// </summary>
internal sealed class PatternAnalyzer : IProgramAnalyzer
{
    public string Id => "pattern-checking";

    public void Analyze(AnalysisContext context)
    {
        var tests = new Dictionary<TypeParameter, TypeTest>(ReferenceEqualityComparer.Instance);

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case IsPatternExpression test:
                    Judge(context, test.Pattern, test.Scrutinee.Type, test.Scrutinee, tests);
                    break;

                case SwitchExpression switchExpression:
                    foreach(var arm in switchExpression.Arms)
                    {
                        Judge(context, arm.Pattern, switchExpression.Scrutinee.Type, switchExpression.Scrutinee, tests);
                    }

                    JudgeUnreachable(context, switchExpression);
                    break;

                case AsExpression { Kind: AsConversionKind.Narrowing or AsConversionKind.UnionExtraction } converted:
                    Note(tests, converted.Operand.Type, converted.TargetType);
                    break;

                case CastExpression cast when CastAnalyzer.Tests(context, cast):
                    Note(tests, cast.Operand.Type, cast.TargetType);
                    break;
            }
        }

        JudgeClosings(context, tests);
    }

    private static void Judge(AnalysisContext context, Pattern root, IMistType input, Expression about, Dictionary<TypeParameter, TypeTest> tests)
    {
        foreach(var (pattern, flowing) in Flatten(root, input))
        {
            switch(pattern)
            {
                case TypeTestPattern test:
                    Note(tests, flowing, test.NarrowedType);
                    JudgeTypeTest(context, test, flowing, about);
                    break;

                case ComparisonPattern comparison:
                    JudgeComparison(context, comparison, flowing, about);
                    break;
            }
        }
    }

    private static IEnumerable<(Pattern Pattern, IMistType Input)> Flatten(Pattern pattern, IMistType input)
    {
        yield return (pattern, input);

        var nested = pattern switch
        {
            TypeTestPattern { Refinement: { } refinement } test when Comparability.Misordered(refinement) is null
                => Flatten(refinement, test.NarrowedType),
            ConjunctionPattern conjunction => Conjuncts(conjunction, input),
            DisjunctionPattern disjunction => disjunction.Operands.SelectMany(operand => Flatten(operand, input)),
            NegatedPattern negated => Flatten(negated.Operand, input),
            PropertyPattern property
                => (property.TypeTest is { } typeTest ? Flatten(typeTest, input) : [])
                    .Concat(property.Subpatterns.SelectMany(subpattern => Flatten(subpattern.Pattern, subpattern.MemberType))),
            TuplePattern tuple when Elements(input) is { } elements && elements.Count == tuple.Elements.Count
                => tuple.Elements.SelectMany((element, i) => Flatten(element, elements[i])),
            _ => [],
        };

        foreach(var entry in nested)
        {
            yield return entry;
        }
    }

    private static IEnumerable<(Pattern Pattern, IMistType Input)> Conjuncts(ConjunctionPattern conjunction, IMistType input)
    {
        var narrowed = input;

        foreach(var operand in conjunction.Operands)
        {
            foreach(var entry in Flatten(operand, narrowed))
            {
                yield return entry;
            }

            narrowed = operand.NarrowedType;
        }
    }

    private static void Note(Dictionary<TypeParameter, TypeTest> tests, IMistType from, IMistType to)
    {
        foreach(var parameter in CastAnalyzer.HiddenParameters(from, to))
        {
            _ = tests.TryAdd(parameter, new TypeTest(parameter, from, to));
        }
    }

    private static void JudgeClosings(AnalysisContext context, Dictionary<TypeParameter, TypeTest> tests)
    {
        var registry = context.Model.ConstructorConstraintClosures;
        var forwarding = registry.Closings.Concat(registry.OpenClosings).ToList();

        for(var grew = tests.Count > 0; grew;)
        {
            grew = false;

            foreach(var closing in forwarding)
            {
                if(tests.TryGetValue(closing.Parameter, out var test))
                {
                    grew |= CastAnalyzer.TypeParameters(closing.Argument).Aggregate(false, (added, forwarded) => tests.TryAdd(forwarded, test) || added);
                }
            }
        }

        var reported = new HashSet<(object, TypeParameter)>();

        foreach(var closing in registry.Closings)
        {
            if(!tests.TryGetValue(closing.Parameter, out var test)
                || CastAnalyzer.PureFunctionIn(closing.Argument) is not { } promised
                || !reported.Add((closing.Node, closing.Parameter)))
            {
                continue;
            }

            context.Report(
                ProgramAnalysisDiagnostics.PureFunctionTypeUntestableClosing, closing.Node, [],
                closing.Parameter.Name, GenericName(closing.Parameter, context), TypeDisplay.Mist(closing.Argument),
                GenericName(test.Parameter, context), TypeDisplay.Mist(test.From), TypeDisplay.Mist(test.To), TypeDisplay.Mist(promised));
        }
    }

    private static string GenericName(TypeParameter parameter, AnalysisContext context)
    {
        if(parameter.DeclaringType is MemberType type)
        {
            var method = type.MethodsList.Find(candidate => candidate.TypeParametersList.Exists(own => ReferenceEquals(own, parameter)));

            return method is null ? TypeDisplay.Mist(type) : $"{TypeDisplay.Mist(type)}.{method.Name}";
        }

        var extension = context.Extensions
            .SelectMany(block => block.MethodsList)
            .FirstOrDefault(candidate => candidate.TypeParametersList.Exists(own => ReferenceEquals(own, parameter)));

        return extension?.Name ?? parameter.Name;
    }

    private static void JudgeTypeTest(AnalysisContext context, TypeTestPattern test, IMistType input, Expression about)
    {
        var tested = test.Constituent ?? test.NarrowedType;

        if(CastAnalyzer.HidesPurity(input, test.NarrowedType))
        {
            context.Report(
                ProgramAnalysisDiagnostics.PureFunctionTypeUntestable, test.Node, [about],
                TypeDisplay.Mist(input), TypeDisplay.Mist(test.NarrowedType));

            return;
        }

        if(tested is UnknownType && !IsIndeterminate(input))
        {
            context.Report(ProgramAnalysisDiagnostics.PatternNarrowsToUnknown, test.Node, [about]);
            return;
        }

        var held = OptionalLifting.NonVoidPortion(input, context.Model.TypeInterner);

        if(ConversionRelation.CopiesAClone(held, tested) || ConversionRelation.CopiesAClone(tested, held))
        {
            context.Report(
                ProgramAnalysisDiagnostics.PatternNeverMatches, test.Node, [about],
                TypeDisplay.Mist(held), TypeDisplay.Mist(tested));

            return;
        }

        if(OptionalUnion.IsVoid(tested) || input is UnionType || tested is UnionType || IsIndeterminate(input) || IsIndeterminate(tested))
        {
            return;
        }

        if(!Possible(input, tested, context))
        {
            context.Report(
                ProgramAnalysisDiagnostics.PatternNeverMatches, test.Node, [about],
                TypeDisplay.Mist(input), TypeDisplay.Mist(tested));
        }
    }

    private static void JudgeComparison(AnalysisContext context, ComparisonPattern comparison, IMistType input, Expression about)
    {
        if(IsIndeterminate(input) || input is UnionType || comparison.Bound.Type is ErrorType)
        {
            return;
        }

        if(!IsConstant(comparison.Bound))
        {
            context.Report(ProgramAnalysisDiagnostics.PatternBoundNotConstant, comparison.Node, [about, comparison.Bound]);
            return;
        }

        if(comparison.Kind == ComparisonKind.Equal)
        {
            var kind = ConversionClassifier.Classify(comparison.Bound.Type, input, context.Model.TypeInterner);
            var reverse = ConversionClassifier.Classify(input, comparison.Bound.Type, context.Model.TypeInterner);

            if(kind is ConversionKind.None && reverse is ConversionKind.None && !Fits(comparison.Bound, input))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.PatternConstantMismatch, comparison.Node, [about, comparison.Bound],
                    TypeDisplay.Mist(comparison.Bound.Type), TypeDisplay.Mist(input));
            }

            return;
        }

        if(!Comparability.Supports(input))
        {
            context.Report(
                ProgramAnalysisDiagnostics.PatternNotComparable, comparison.Node, [about],
                TypeDisplay.Mist(input));
        }
        else if(!Comparability.Supports(comparison.Bound.Type))
        {
            context.Report(
                ProgramAnalysisDiagnostics.PatternConstantMismatch, comparison.Node, [about, comparison.Bound],
                TypeDisplay.Mist(comparison.Bound.Type), TypeDisplay.Mist(input));
        }
    }

    private static void JudgeUnreachable(AnalysisContext context, SwitchExpression switchExpression)
    {
        var covering = new List<Pattern>();

        foreach(var arm in switchExpression.Arms)
        {
            if(covering.Any(earlier => Covers(earlier, arm.Pattern, context)))
            {
                context.Report(ProgramAnalysisDiagnostics.SwitchArmUnreachable, arm.Node, [switchExpression.Scrutinee]);
            }

            if(arm.Guard is null)
            {
                covering.Add(arm.Pattern);
            }
        }
    }

    private static bool Covers(Pattern earlier, Pattern later, AnalysisContext context)
    {
        return earlier switch
        {
            CatchAllPattern => true,
            TypeTestPattern { Refinement: null } wide when later is TypeTestPattern narrow
                => ConversionClassifier.Classify(narrow.Constituent ?? narrow.NarrowedType, wide.Constituent ?? wide.NarrowedType, context.Model.TypeInterner)
                    is ConversionKind.Identity or ConversionKind.Implicit,
            ComparisonPattern { Kind: ComparisonKind.Equal, Bound: LiteralExpression { Value: { } first } }
                when later is ComparisonPattern { Kind: ComparisonKind.Equal, Bound: LiteralExpression { Value: { } second } }
                => Equals(first, second),
            _ => false,
        };
    }

    private static bool Possible(IMistType input, IMistType tested, AnalysisContext context)
    {
        var interner = context.Model.TypeInterner;

        if(ConversionClassifier.Classify(input, tested, interner) is ConversionKind.Identity or ConversionKind.Implicit or ConversionKind.Unclassified
            || ConversionClassifier.Classify(tested, input, interner) is ConversionKind.Identity or ConversionKind.Implicit or ConversionKind.Unclassified)
        {
            return true;
        }

        return (Definition(input) is AbstractType && IsOpenClass(tested))
            || (Definition(tested) is AbstractType && IsOpenClass(input));
    }

    private static bool IsOpenClass(IMistType type)
    {
        return Definition(type) is ClassType { Modifiers.IsSealed: false };
    }

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }

    private static bool IsIndeterminate(IMistType type)
    {
        return type is TypeParameter or UnknownType or UnknownValueType or ErrorType or NotProjectedType or LiftedType or SpreadType
            || (type is ITupleType tuple && tuple.ElementTypes.Any(element => element is SpreadType));
    }

    private static bool IsConstant(Expression bound)
    {
        return bound switch
        {
            LiteralExpression => true,
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: LiteralExpression } => true,
            MemberAccessExpression { Type: EnumType } => true,
            _ => false,
        };
    }

    private static bool Fits(Expression bound, IMistType input)
    {
        return bound.Type.FullyQualifiedName == WellKnownTypes.Int32.FullyQualifiedName && Comparability.Supports(input);
    }

    private static IReadOnlyList<IMistType>? Elements(IMistType type)
    {
        return type switch
        {
            INamedTupleType named => named.Underlying.ElementTypes,
            ITupleType tuple => tuple.ElementTypes,
            _ => null,
        };
    }

    private readonly record struct TypeTest(TypeParameter Parameter, IMistType From, IMistType To);
}