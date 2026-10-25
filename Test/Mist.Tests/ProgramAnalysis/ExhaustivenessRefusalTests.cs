// Tier 1: finite missing cases, product witnesses and unbounded constant-only switches are judged directly.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ExhaustivenessRefusalTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bool-missing-a-value", "false")]
    [InlineData("enum-missing-a-variant", "Blue")]
    [InlineData("int-constants-only", "0, 3")]
    [InlineData("negated-bool-leaves-its-value", "true")]
    [InlineData("nested-tuple-missing-a-cell", "((false, false), false)")]
    [InlineData("only-a-guarded-catch-all", "_")]
    [InlineData("only-constants-over-an-unbounded-type", "_")]
    [InlineData("optional-bool-covered-only-by-values", "void")]
    [InlineData("optional-string-covered-only-by-constants", "void")]
    [InlineData("tuple-cell-reached-only-through-a-guard", "(false, false)")]
    [InlineData("tuple-of-bools-missing-a-cell", "(false, false)")]
    [InlineData("tuple-of-enums-missing-a-cell", "(Blue, Blue)")]
    [InlineData("tuple-of-ints-with-a-gap", "(0, _)")]
    [InlineData("tuple-with-only-guarded-arms", "(false, _), (true, _)")]
    [InlineData("union-missing-a-constituent", "string")]
    [InlineData("union-missing-a-refined-constituent", "Big")]
    public void MissingCaseKeepsItsDiagnosticAndNamesAWitness(string caseName, string witnesses)
    {
        var expression = Switch(caseName);
        var finding = ExhaustivenessAnalyzer.Finding(expression);
        Assert.Equal("MIST0148", finding?.Number.Value);
        Assert.Equal(Mist.Common.Diagnostics.DiagnosticSeverity.Warning, finding?.DefaultSeverity);
        Assert.Equal(witnesses, string.Join(", ", ExhaustivenessAnalyzer.Missing(expression)!));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bool-missing-a-value")]
    [InlineData("enum-missing-a-variant")]
    [InlineData("int-constants-only")]
    [InlineData("nested-tuple-missing-a-cell")]
    [InlineData("only-a-guarded-catch-all")]
    [InlineData("only-constants-over-an-unbounded-type")]
    [InlineData("optional-bool-covered-only-by-values")]
    [InlineData("tuple-of-bools-missing-a-cell")]
    [InlineData("tuple-of-ints-with-a-gap")]
    [InlineData("union-missing-a-constituent")]
    public void UnguardedCatchAllCompletesEachSwitch(string caseName)
    {
        var input = Switch(caseName);
        var catchAll = new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), input.Scrutinee.Type, null), null, input.Scrutinee);
        var complete = new SwitchExpression(input.Node, input.Scrutinee, [.. input.Arms, catchAll], input.Type);
        Assert.Null(ExhaustivenessAnalyzer.Finding(complete));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("nested-tuple-fully-covered")]
    [InlineData("optional-bool-with-a-void-arm")]
    [InlineData("optional-bool-with-void-in-a-disjunction")]
    [InlineData("relational-partition-of-an-int")]
    [InlineData("tuple-cell-covered-by-a-later-unguarded-arm")]
    [InlineData("tuple-of-all-catch-alls")]
    [InlineData("tuple-of-an-enum-and-a-bool")]
    [InlineData("tuple-of-bindings")]
    [InlineData("tuple-of-bools-fully-enumerated")]
    [InlineData("tuple-partition-of-relational-arms")]
    [InlineData("tuple-with-a-relational-enum-element")]
    [InlineData("tuple-with-an-unreadable-element")]
    [InlineData("union-constituent-partially-covered-by-a-constant")]
    [InlineData("union-of-refined-constituents-each-tested")]
    [InlineData("optional-refinement-and-its-absence")]
    public void ACoveringOrUnreadableArmSetIsNotReported(string caseName)
    {
        Assert.Null(ExhaustivenessAnalyzer.Finding(Switch(caseName)));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("bool-missing-a-value", "false")]
    [InlineData("optional-bool-covered-only-by-values", "void")]
    [InlineData("tuple-of-bools-missing-a-cell", "(false, false)")]
    public void ReportsTheSwitchSiteAndNamesTheMissingValue(string caseName, string witness)
    {
        var input = Switch(caseName);
        var diagnostics = new HandBuiltModel().Analyze(new ExhaustivenessAnalyzer(), input);
        var finding = Assert.Single(diagnostics);
        Assert.Equal("MIST0148", finding.Number.Value);
        Assert.Equal(31, finding.Position().Start.Column);
        Assert.Contains(witness, finding.Message, StringComparison.Ordinal);
    }

    private static SwitchExpression Switch(string caseName)
    {
        var model = new HandBuiltModel();
        var boolean = model.Type("Boolean");
        var integer = model.Type("Int32");
        var text = model.Type("String");
        var nothing = model.Type("Void");
        var color = HandBuiltModel.Enum("Color", integer);
        color.MembersList.AddRange([new EnumMember("Red", false, null), new EnumMember("Green", false, null), new EnumMember("Blue", false, null)]);
        var union = model.Interner.GetOrCreateUnionType([integer, text]);
        var small = HandBuiltRefinements.RefinePattern(integer, new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.LessThan, HandBuiltModel.Literal(integer, 10L)), "Small");
        var big = HandBuiltRefinements.RefinePattern(integer, new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.GreaterThan, HandBuiltModel.Literal(integer, 100L)), "Big");
        var refinedUnion = model.Interner.GetOrCreateUnionType([small, big]);
        var optionalSmall = model.Interner.GetOrCreateUnionType([small, nothing]);
        var optionalBool = model.Interner.GetOrCreateUnionType([boolean, nothing]);
        var optionalText = model.Interner.GetOrCreateUnionType([text, nothing]);
        var bools = model.Interner.GetOrCreateTupleType([boolean, boolean]);
        var colors = model.Interner.GetOrCreateTupleType([color, color]);
        var ints = model.Interner.GetOrCreateTupleType([integer, integer]);
        var mixed = model.Interner.GetOrCreateTupleType([color, boolean]);
        var nested = model.Interner.GetOrCreateTupleType([bools, boolean]);
        var guard = HandBuiltModel.Literal(boolean, true);
        IMistType input;
        IReadOnlyList<SwitchArm> arms;

        SwitchArm arm(Pattern pattern, Expression? guard = null)
        {
            return new SwitchArm(HandBuiltModel.Node(), pattern, guard, HandBuiltModel.Literal(text, "result"));
        }

        ComparisonPattern constant(IMistType type, object value)
        {
            return new ComparisonPattern(HandBuiltModel.Node(), type, ComparisonKind.Equal, HandBuiltModel.Literal(type, value));
        }

        ComparisonPattern compare(ComparisonKind kind, long value)
        {
            return new ComparisonPattern(HandBuiltModel.Node(), integer, kind, HandBuiltModel.Literal(integer, value));
        }

        ComparisonPattern variant(string name, ComparisonKind kind = ComparisonKind.Equal)
        {
            return new ComparisonPattern(HandBuiltModel.Node(), color, kind, new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(color), name, color));
        }

        CatchAllPattern any(IMistType type, string? binding = null)
        {
            return new CatchAllPattern(HandBuiltModel.Node(), type, binding is null ? null : new LocalVariable(binding, type));
        }

        TuplePattern tuple(IMistType type, params Pattern[] elements)
        {
            return new TuplePattern(HandBuiltModel.Node(), elements, type, null, null);
        }

        TypeTestPattern absent()
        {
            return new TypeTestPattern(HandBuiltModel.Node(), nothing, null) { TestsAbsence = true };
        }

        switch(caseName)
        {
            case "bool-missing-a-value":
                input = boolean;
                arms = [arm(constant(boolean, true))];
                break;
            case "negated-bool-leaves-its-value":
                input = boolean;
                arms = [arm(new NegatedPattern(HandBuiltModel.Node(), boolean, constant(boolean, true)))];
                break;
            case "enum-missing-a-variant":
                input = color;
                arms = [arm(variant("Red")), arm(variant("Green"))];
                break;
            case "only-a-guarded-catch-all":
                input = integer;
                arms = [arm(any(integer), guard)];
                break;
            case "int-constants-only":
                input = integer;
                arms = [arm(constant(integer, 1L)), arm(constant(integer, 2L))];
                break;
            case "relational-partition-of-an-int":
                input = integer;
                arms = [arm(compare(ComparisonKind.LessThan, 0)), arm(constant(integer, 0L)), arm(compare(ComparisonKind.GreaterThan, 0))];
                break;
            case "only-constants-over-an-unbounded-type":
                input = text;
                arms = [arm(constant(text, "a")), arm(constant(text, "b"))];
                break;
            case "union-missing-a-constituent":
                input = union;
                arms = [arm(new TypeTestPattern(HandBuiltModel.Node(), integer, null, integer))];
                break;
            case "union-missing-a-refined-constituent":
                input = refinedUnion;
                arms = [arm(new TypeTestPattern(HandBuiltModel.Node(), small, null, small, small.Predicate))];
                break;
            case "union-of-refined-constituents-each-tested":
                input = refinedUnion;
                arms = [arm(new TypeTestPattern(HandBuiltModel.Node(), small, null, small, small.Predicate)), arm(new TypeTestPattern(HandBuiltModel.Node(), big, null, big, big.Predicate))];
                break;
            case "optional-refinement-and-its-absence":
                input = optionalSmall;
                arms = [arm(new TypeTestPattern(HandBuiltModel.Node(), small, null, refinement: small.Predicate)), arm(absent())];
                break;
            case "union-constituent-partially-covered-by-a-constant":
                input = union;
                arms = [arm(constant(union, 3L)), arm(new TypeTestPattern(HandBuiltModel.Node(), text, null, text))];
                break;
            case "optional-bool-covered-only-by-values":
                input = optionalBool;
                arms = [arm(constant(optionalBool, true)), arm(constant(optionalBool, false))];
                break;
            case "optional-bool-with-a-void-arm":
                input = optionalBool;
                arms = [arm(constant(optionalBool, true)), arm(constant(optionalBool, false)), arm(absent())];
                break;
            case "optional-bool-with-void-in-a-disjunction":
                input = optionalBool;
                arms = [arm(new DisjunctionPattern(HandBuiltModel.Node(), optionalBool, [constant(optionalBool, true), absent()])), arm(constant(optionalBool, false))];
                break;
            case "optional-string-covered-only-by-constants":
                input = optionalText;
                arms = [arm(constant(optionalText, "a")), arm(constant(optionalText, "b"))];
                break;
            case "tuple-of-bools-missing-a-cell":
                input = bools;
                arms = [arm(tuple(bools, constant(boolean, true), any(boolean))), arm(tuple(bools, constant(boolean, false), constant(boolean, true)))];
                break;
            case "tuple-of-bools-fully-enumerated":
                input = bools;
                arms =
                [
                    arm(tuple(bools, constant(boolean, true), any(boolean))),
                    arm(tuple(bools, constant(boolean, false), constant(boolean, true))),
                    arm(tuple(bools, constant(boolean, false), constant(boolean, false))),
                ];
                break;
            case "tuple-cell-reached-only-through-a-guard":
                input = bools;
                arms =
                [
                    arm(tuple(bools, constant(boolean, true), any(boolean))),
                    arm(tuple(bools, constant(boolean, false), constant(boolean, true))),
                    arm(tuple(bools, constant(boolean, false), constant(boolean, false)), guard),
                ];
                break;
            case "tuple-cell-covered-by-a-later-unguarded-arm":
                input = bools;
                arms =
                [
                    arm(tuple(bools, constant(boolean, true), any(boolean))),
                    arm(tuple(bools, constant(boolean, false), any(boolean, "q")), guard),
                    arm(tuple(bools, constant(boolean, false), constant(boolean, false))),
                    arm(tuple(bools, constant(boolean, false), constant(boolean, true))),
                ];
                break;
            case "tuple-with-only-guarded-arms":
                input = bools;
                arms = [arm(tuple(bools, any(boolean, "a"), any(boolean, "b")), guard)];
                break;
            case "tuple-of-all-catch-alls":
                input = bools;
                arms = [arm(tuple(bools, any(boolean), any(boolean)))];
                break;
            case "tuple-of-bindings":
                input = bools;
                arms = [arm(tuple(bools, any(boolean, "a"), any(boolean, "b")))];
                break;
            case "tuple-with-an-unreadable-element":
                input = bools;
                arms = [arm(tuple(bools, constant(boolean, true), any(boolean))), arm(tuple(bools, new PropertyPattern(HandBuiltModel.Node(), boolean, []), any(boolean)))];
                break;
            case "tuple-of-enums-missing-a-cell":
                input = colors;
                arms =
                [
                    arm(tuple(colors, variant("Red"), any(color))),
                    arm(tuple(colors, variant("Green"), any(color))),
                    arm(tuple(colors, variant("Blue"), variant("Red"))),
                    arm(tuple(colors, variant("Blue"), variant("Green"))),
                ];
                break;
            case "tuple-with-a-relational-enum-element":
                input = colors;
                arms = [arm(tuple(colors, variant("Red", ComparisonKind.GreaterThan), any(color)))];
                break;
            case "tuple-of-an-enum-and-a-bool":
                input = mixed;
                arms =
                [
                    arm(tuple(mixed, variant("Red"), any(boolean))),
                    arm(tuple(mixed, variant("Green"), any(boolean))),
                    arm(tuple(mixed, variant("Blue"), constant(boolean, true))),
                    arm(tuple(mixed, variant("Blue"), constant(boolean, false))),
                ];
                break;
            case "tuple-of-ints-with-a-gap":
                input = ints;
                arms = [arm(tuple(ints, compare(ComparisonKind.LessThan, 0), any(integer))), arm(tuple(ints, compare(ComparisonKind.GreaterThan, 0), any(integer)))];
                break;
            case "tuple-partition-of-relational-arms":
                input = ints;
                arms =
                [
                    arm(tuple(ints, constant(integer, 0L), constant(integer, 0L))),
                    arm(tuple(ints, compare(ComparisonKind.GreaterThan, 0), compare(ComparisonKind.GreaterThan, 0))),
                    arm(tuple(ints, compare(ComparisonKind.LessThan, 0), compare(ComparisonKind.GreaterThan, 0))),
                    arm(tuple(ints, compare(ComparisonKind.LessThan, 0), compare(ComparisonKind.LessThan, 0))),
                    arm(tuple(ints, compare(ComparisonKind.GreaterThan, 0), compare(ComparisonKind.LessThan, 0))),
                    arm(tuple(ints, constant(integer, 0L), any(integer))),
                    arm(tuple(ints, any(integer), constant(integer, 0L))),
                ];
                break;
            case "nested-tuple-missing-a-cell":
                input = nested;
                arms =
                [
                    arm(tuple(nested, tuple(bools, constant(boolean, true), any(boolean)), any(boolean))),
                    arm(tuple(nested, tuple(bools, constant(boolean, false), constant(boolean, true)), any(boolean))),
                    arm(tuple(nested, tuple(bools, constant(boolean, false), constant(boolean, false)), constant(boolean, true))),
                ];
                break;
            case "nested-tuple-fully-covered":
                input = nested;
                arms =
                [
                    arm(tuple(nested, tuple(bools, constant(boolean, true), any(boolean)), any(boolean))),
                    arm(tuple(nested, tuple(bools, constant(boolean, false), constant(boolean, true)), any(boolean))),
                    arm(tuple(nested, tuple(bools, constant(boolean, false), constant(boolean, false)), any(boolean))),
                ];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(caseName));
        }

        return new SwitchExpression(HandBuiltModel.Node(30), HandBuiltModel.Literal(input), arms, text);
    }
}