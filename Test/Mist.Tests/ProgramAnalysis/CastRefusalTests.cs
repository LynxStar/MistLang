// Tier 1 decisions and Tier 2 report sites hold total conversion refusals without compiling programs.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class CastRefusalTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("cast-ambiguous-between-both-ends", "MIST_PA_AMBIGUOUS_CAST")]
    [InlineData("cast-ambiguous-between-signed-and-unsigned-parameters", "MIST_PA_AMBIGUOUS_CAST")]
    [InlineData("cast-ambiguous-between-two-parameters", "MIST_PA_AMBIGUOUS_CAST")]
    [InlineData("cast-ambiguous-between-two-widening-results", "MIST_PA_AMBIGUOUS_CAST")]
    [InlineData("cast-argument-narrowing", "MIST_PA_CAST_ARGUMENT_MISMATCH")]
    [InlineData("cast-argument-through-two-declared-implicits", "MIST_PA_CAST_ARGUMENT_MISMATCH")]
    [InlineData("cast-constant-fitting-no-operator", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("clone-conversion-through-a-renamed-field", "MIST_PA_CLONE_CONVERSION_DOES_NOT_HOLD")]
    [InlineData("constant-narrowing", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("constant-outside-a-refined-base", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("copy-conversion-through-a-self-typed-field", "MIST_PA_COPY_CONVERSION_DOES_NOT_HOLD")]
    [InlineData("down-to-a-type-parameter", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("downcast", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("enum-from-a-narrowing-numeric", "MIST_PA_ENUM_CAST_ARGUMENT_MISMATCH")]
    [InlineData("enum-from-a-wider-integral-variable", "MIST_PA_ENUM_CAST_ARGUMENT_MISMATCH")]
    [InlineData("enum-from-an-enum-over-a-wider-base", "MIST_PA_ENUM_CAST_ARGUMENT_MISMATCH")]
    [InlineData("integral-narrowing-of-a-variable", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("mutable-class-newtype-from-a-source-with-no-copy", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY")]
    [InlineData("negative-constant-to-an-unsigned-type", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("numeric-narrowing", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("operator-declared-only-on-a-derived-type", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("union-to-one-constituent", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("unknown-to-a-concrete-type", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("unknown-to-a-type-parameter", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("unrelated-types", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    public void RefusedConversionKeepsItsDiagnostic(string caseName, string expected)
    {
        var input = Case(caseName);
        var finding = CastAnalyzer.Judge(input.Cast, input.Model.Interner, input.Refusals);
        Assert.True(finding?.Number.Value == expected, $"{caseName}: {finding?.Number.Value ?? "silence"}");
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("newtype-as-its-source")]
    public void NewtypeAsItsSourceKeepsItsBindingDiagnostic(string caseName)
    {
        var source = HandBuiltModel.Class("Money");
        source.Modifiers = new MemberModifiers { IsReadonly = true };
        var clone = HandBuiltModel.Class("Cash");
        clone.CloneSource = source;
        clone.CloneSourceType = source;
        Assert.True(OperatorBinder.AsFinding(clone, source)?.Number.Value == "MIST_PM_AS_NO_RELATIONSHIP", caseName);
        Assert.Null(OperatorBinder.AsFinding(source, source));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("downcast", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("cast-argument-narrowing", "MIST_PA_CAST_ARGUMENT_MISMATCH")]
    [InlineData("enum-from-a-wider-integral-variable", "MIST_PA_ENUM_CAST_ARGUMENT_MISMATCH")]
    [InlineData("cast-ambiguous-between-both-ends", "MIST_PA_AMBIGUOUS_CAST")]
    [InlineData("copy-conversion-through-a-self-typed-field", "MIST_PA_COPY_CONVERSION_DOES_NOT_HOLD")]
    public void ConversionReportsAtTheWrittenCast(string caseName, string expected)
    {
        var input = Case(caseName);
        var finding = Assert.Single(input.Model.Analyze(new CastAnalyzer(), input.Cast));
        Assert.Equal(expected, finding.Number.Value);
        Assert.Equal(41, finding.Position().Start.Column);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Int32", "Int64", null)]
    [InlineData("Int32", "Byte", 2L)]
    [InlineData("String", "String", null)]
    [InlineData("error", "Int32", null)]
    public void AdmittedConversionStaysSilent(string from, string to, long? constant)
    {
        var fixture = new AssignabilityTypes();
        Expression operand = constant is { } value ? HandBuiltModel.Literal(fixture[from], value) : new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", fixture[from]));
        var cast = new CastExpression(HandBuiltModel.Node(), operand, fixture[to], [], fixture[to]);
        Assert.Null(CastAnalyzer.Judge(cast, fixture.Model.Interner, CloneConversionRefusals.None));
    }

    private static (HandBuiltModel Model, CastExpression Cast, IReadOnlyDictionary<CastOperator, ConversionRefusal> Refusals) Case(string name)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var wide = model.Type("Int64");
        var number = model.Type("Double");
        var text = model.Type("String");
        var small = model.Type("Byte");
        IMistType source = integer;
        IMistType target = small;
        long? constant = null;
        var refusals = CloneConversionRefusals.None;
        IReadOnlyList<ConversionFunction> selected = [];

        switch(name)
        {
            case "constant-narrowing": constant = 300; break;
            case "negative-constant-to-an-unsigned-type": constant = -1; break;
            case "constant-outside-a-refined-base": target = HandBuiltModel.Refined(small, "Small"); constant = 300; break;
            case "integral-narrowing-of-a-variable": break;
            case "numeric-narrowing": source = number; target = integer; break;
            case "unrelated-types": target = text; break;
            case "unknown-to-a-concrete-type": source = UnknownType.Instance; target = integer; break;
            case "unknown-to-a-type-parameter": source = UnknownType.Instance; target = new TypeParameter("T", 0, new Scope(null), null); break;
            case "down-to-a-type-parameter":
                source = HandBuiltModel.Class("Animal");
                var parameter = new TypeParameter("T", 0, new Scope(null), null);
                parameter.ConstraintsList.Add(new TypeConstraint(source));
                target = parameter;
                break;
            case "downcast": source = HandBuiltModel.Class("Animal"); target = HandBuiltModel.Class("Dog", (MistType)source); break;
            case "union-to-one-constituent": source = model.Interner.GetOrCreateUnionType([integer, text]); target = integer; break;
            case "enum-from-a-narrowing-numeric": source = number; target = HandBuiltModel.Enum("Priority", integer); break;
            case "enum-from-a-wider-integral-variable": target = HandBuiltModel.Enum("Color", small); break;
            case "enum-from-an-enum-over-a-wider-base": source = HandBuiltModel.Enum("Wide", wide); target = HandBuiltModel.Enum("Priority", integer); break;
            case "cast-ambiguous-between-both-ends":
                var link = HandBuiltModel.Class("Link");
                var anchor = HandBuiltModel.Class("Anchor");
                var fromLink = HandBuiltModel.Conversion(link, link, anchor, isImplicit: false);
                var fromAnchor = HandBuiltModel.Conversion(anchor, link, anchor, isImplicit: false);
                selected = [new ConversionFunction(fromLink, link, anchor), new ConversionFunction(fromAnchor, link, anchor)];
                source = link; target = anchor;
                break;
            case "cast-ambiguous-between-signed-and-unsigned-parameters":
                var gauge = HandBuiltModel.Class("Gauge");
                var unsigned = HandBuiltModel.Conversion(gauge, model.Type("UInt32"), gauge, isImplicit: false);
                var signed = HandBuiltModel.Conversion(gauge, model.Type("Int16"), gauge, isImplicit: false);
                selected = [new ConversionFunction(unsigned, model.Type("UInt32"), gauge), new ConversionFunction(signed, model.Type("Int16"), gauge)];
                source = small; target = gauge;
                break;
            case "cast-ambiguous-between-two-widening-results":
                var tally = HandBuiltModel.Class("Tally");
                var narrowResult = HandBuiltModel.Conversion(tally, tally, integer);
                var wideResult = HandBuiltModel.Conversion(tally, tally, wide);
                selected = [new ConversionFunction(narrowResult, tally, integer), new ConversionFunction(wideResult, tally, wide)];
                source = tally; target = number;
                break;
            case "cast-ambiguous-between-two-parameters":
                var sounding = HandBuiltModel.Class("Sounding");
                var fathom = HandBuiltModel.Class("Fathom");
                var meters = HandBuiltModel.Class("Meters");
                var yards = HandBuiltModel.Class("Yards");
                _ = HandBuiltModel.Conversion(sounding, sounding, meters);
                _ = HandBuiltModel.Conversion(sounding, sounding, yards);
                var metersOperator = HandBuiltModel.Conversion(fathom, meters, fathom, isImplicit: false);
                var yardsOperator = HandBuiltModel.Conversion(fathom, yards, fathom, isImplicit: false);
                selected = [new ConversionFunction(metersOperator, meters, fathom), new ConversionFunction(yardsOperator, yards, fathom)];
                source = sounding; target = fathom;
                break;
            case "cast-argument-narrowing":
                var feet = HandBuiltModel.Class("Feet");
                var feetOperator = HandBuiltModel.Conversion(feet, integer, feet, isImplicit: false);
                selected = [new ConversionFunction(feetOperator, integer, feet)];
                source = wide; target = feet;
                break;
            case "cast-argument-through-two-declared-implicits":
                var destination = HandBuiltModel.Class("Feet");
                var middle = HandBuiltModel.Class("Meters");
                var reading = HandBuiltModel.Class("Reading");
                var sensor = HandBuiltModel.Class("Sensor");
                var destinationOperator = HandBuiltModel.Conversion(destination, middle, destination, isImplicit: false);
                selected = [new ConversionFunction(destinationOperator, middle, destination)];
                _ = HandBuiltModel.Conversion(reading, reading, middle);
                _ = HandBuiltModel.Conversion(sensor, sensor, reading);
                source = sensor; target = destination;
                break;
            case "cast-constant-fitting-no-operator":
                var bin = HandBuiltModel.Class("Bin");
                _ = HandBuiltModel.Conversion(bin, text, bin, isImplicit: false);
                _ = HandBuiltModel.Conversion(bin, small, bin, isImplicit: false);
                target = bin; constant = 300;
                break;
            case "operator-declared-only-on-a-derived-type":
                var measured = HandBuiltModel.Class("Measurement");
                var weight = HandBuiltModel.Class("Weight", measured);
                var kelvin = HandBuiltModel.Class("Kelvin");
                _ = HandBuiltModel.Conversion(weight, weight, kelvin, isImplicit: false);
                source = measured; target = kelvin;
                break;
            case "mutable-class-newtype-from-a-source-with-no-copy":
                var entity = HandBuiltModel.Class("Entity");
                var actor = HandBuiltModel.Class("Actor");
                actor.CloneSource = entity; actor.CloneSourceType = entity;
                source = entity; target = actor;
                break;
            case "copy-conversion-through-a-self-typed-field":
                var node = HandBuiltModel.Class("Node");
                node.Modifiers = new MemberModifiers { IsReadonly = true };
                var chain = HandBuiltModel.Class("Chain");
                chain.CloneSource = node; chain.CloneSourceType = node;
                chain.UnheldCopy = new UnheldCopy("Next", chain, node);
                source = node; target = chain;
                break;
            case "clone-conversion-through-a-renamed-field":
                var lion = HandBuiltModel.Class("Lion");
                var puma = HandBuiltModel.Class("Puma");
                var clone = new Method("Clone", lion, [], default) { DeclaringType = lion };
                var conversion = new CastOperator(puma, new Parameter("value", lion), false, default) { DeclaringType = puma, ClonesThrough = clone };
                selected = [new ConversionFunction(conversion, lion, puma)];
                var failure = DiagnosticFixtures.Occurrence(ProgramAnalysisDiagnostics.NoImplicitConversion.Number.Value, DiagnosticSeverity.Error, default, "Mate does not hold", default, "hand-built.mist", null);
                refusals = new Dictionary<CastOperator, ConversionRefusal> { [conversion] = new(failure, "Mate") };
                source = lion; target = puma;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(name));
        }

        Expression operand = constant is { } value ? HandBuiltModel.Literal(source, value) : new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", source));
        var result = target is EnumType ? model.Interner.GetOrCreateUnionType([target, model.Type("Void")]) : target;
        var cast = new CastExpression(HandBuiltModel.Node(40), operand, target, selected, result);
        return (model, cast, refusals);
    }
}