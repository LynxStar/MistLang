using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class AnalysisTypeFactSnapshotTests
{
    [Fact]
    public void ExposedReturnTypeRetainsItsIdentityAndMistSpelling()
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Exposes");
        var hidden = fragment.Class("Hidden", owner, Accessibility.Private);
        _ = DeclarationFragments.Method(owner, "Make", hidden);
        var expected = DiagnosticSnapshot.Type(hidden);

        var finding = Assert.Single(fragment.Run(new AccessibilityAnalyzer()));

        Assert.Equal(ProgramAnalysisDiagnostics.InconsistentAccessibility.Number, finding.Number);
        Assert.Equal(expected, OptionalType(finding, 1));
        Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[2]).Value);
        Assert.Equal("Inconsistent accessibility: return type 'Hidden' is less accessible than method 'Make'.", finding.Message);
        Assert.Equal("hand-built.mist", finding.Location.Path);
        hidden.Assembly = new MistAssembly("Changed", MistAssemblyKind.Reference);
        Assert.NotEqual(expected.Identity, DiagnosticSnapshot.Type(hidden).Identity);
        Assert.Equal(expected, OptionalType(finding, 1));
        Assert.Contains("'Hidden'", finding.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PredicateExposureRetainsTheStorageSymbol(bool property)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var owner = fragment.Class("Gauge");
        var modifiers = DeclarationFragments.Public with { Accessibility = Accessibility.Private, IsReadonly = true };
        LookupResult member;
        if(property)
        {
            var storage = new Property("level", integer, modifiers) { DeclaringType = owner, Getter = new PropertyAccessor { IsAuto = true } };
            owner.PropertiesList.Add(storage);
            member = new PropertyResult(storage, owner, []);
        }
        else
        {
            var storage = new Field("level", integer, OwnershipKind.Unrestricted, modifiers) { DeclaringType = owner };
            owner.FieldsList.Add(storage);
            member = new FieldResult(storage, owner, []);
        }

        var refined = HandBuiltModel.Refined(owner, "Low");
        var bound = new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.LessThan, HandBuiltModel.Literal(integer, 3L));
        refined.Predicate = new PropertyPattern(HandBuiltModel.Node(), owner,
            [new PropertySubpattern(HandBuiltModel.Node(30), "level", bound, integer, member)]);
        _ = fragment.Alias("Low", refined, owner);

        var finding = Assert.Single(fragment.Run(new AccessibilityAnalyzer()));

        Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[1]).Value);
        var symbol = Assert.IsType<SymbolValue>(Assert.IsType<OptionalValue>(finding.Arguments[2]).Value);
        Assert.Contains(owner.FullyQualifiedName, symbol.Identity);
        Assert.Contains("level", symbol.Identity);
        Assert.Equal("Gauge.level", symbol.Spelling);
        var kind = property ? "property" : "field";
        Assert.Equal($"Inconsistent accessibility: {kind} 'Gauge.level' is less accessible than type alias 'Low'.", finding.Message);
        var identity = symbol.Identity;
        owner.Assembly = new MistAssembly("Changed", MistAssemblyKind.Reference);
        owner.FieldsList.Clear();
        owner.PropertiesList.Clear();
        Assert.Equal(identity, symbol.Identity);
        Assert.Equal("Gauge.level", symbol.Spelling);
    }

    [Fact]
    public void EquallyAccessibleReturnTypeIsAdmitted()
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Exposes");
        _ = DeclarationFragments.Method(owner, "Make", fragment.Class("Visible"));

        Assert.Empty(fragment.Run(new AccessibilityAnalyzer()));
    }

    [Fact]
    public void UnreachableReturnTypeLeavesTheRefusalToItsNaming()
    {
        var fragment = new DeclarationFragments();
        var outer = fragment.Class("Outer");
        var hidden = fragment.Class("Hidden", outer, Accessibility.Private);
        var owner = fragment.Class("Exposes");
        _ = DeclarationFragments.Method(owner, "Make", hidden);

        Assert.Empty(fragment.Run(new AccessibilityAnalyzer()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnprovenReturnRetainsEveryKnownType(bool property)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var promised = Positive(integer);
        var contract = fragment.Abstract("Counts");
        var concrete = fragment.Class("Counter");
        concrete.BaseAbstractTypes.Add(contract);
        if(property)
        {
            contract.PropertiesList.Add(new Property("Count", promised, DeclarationFragments.Public)
            { DeclaringType = contract, Getter = new PropertyAccessor { IsAuto = true } });
            concrete.PropertiesList.Add(new Property("Count", integer, DeclarationFragments.Public)
            { DeclaringType = concrete, DeclarationNode = HandBuiltModel.Node(20), Getter = new PropertyAccessor { IsAuto = true } });
        }
        else
        {
            _ = DeclarationFragments.Method(contract, "Count", promised);
            _ = DeclarationFragments.Method(concrete, "Count", integer);
        }

        var finding = Assert.Single(Contracts(fragment, concrete));
        var expected = new[] { DiagnosticSnapshot.Type(integer), DiagnosticSnapshot.Type(promised), DiagnosticSnapshot.Type(contract) };

        Assert.Equal(ProgramAnalysisDiagnostics.RefinementReturnUnproven.Number, finding.Number);
        Assert.Equal(expected, finding.Arguments.Skip(1).Cast<TypeValue>());
        Assert.Equal("Member 'Count' produces 'int', which is not proven to satisfy the refinement contract 'Positive' required by 'Counts'.", finding.Message);
        integer.Assembly = new MistAssembly("ChangedInteger", MistAssemblyKind.Reference);
        promised.Assembly = new MistAssembly("ChangedRefinement", MistAssemblyKind.Reference);
        contract.Assembly = new MistAssembly("ChangedContract", MistAssemblyKind.Reference);
        promised.Predicate = null;
        Assert.Equal(expected, finding.Arguments.Skip(1).Cast<TypeValue>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingReturnContractIsAdmitted(bool property)
    {
        var fragment = new DeclarationFragments();
        var promised = Positive(fragment.Built.Type("Int32"));
        var contract = fragment.Abstract("Counts");
        var concrete = fragment.Class("Counter");
        concrete.BaseAbstractTypes.Add(contract);
        if(property)
        {
            contract.PropertiesList.Add(new Property("Count", promised, DeclarationFragments.Public)
            { DeclaringType = contract, Getter = new PropertyAccessor { IsAuto = true } });
            concrete.PropertiesList.Add(new Property("Count", promised, DeclarationFragments.Public)
            { DeclaringType = concrete, Getter = new PropertyAccessor { IsAuto = true } });
        }
        else
        {
            _ = DeclarationFragments.Method(contract, "Count", promised);
            _ = DeclarationFragments.Method(concrete, "Count", promised);
        }

        Assert.Empty(Contracts(fragment, concrete));
    }

    [Theory]
    [InlineData("reference", "Positive", "int")]
    [InlineData("setter", "int", "Positive")]
    [InlineData("integral", "anything", ">= 1")]
    [InlineData("real", "[-1.7976931348623157E+308..1.7976931348623157E+308] or NaN or +Infinity or -Infinity", "[1..1.7976931348623157E+308] or +Infinity")]
    [InlineData("opaque", "string", "Letters")]
    public void StrengthenedContractsRetainTypesWithTheirOriginalDescriptions(string shape, string known, string required)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        IMistType promised = integer;
        IMistType demanded = Positive(integer);
        if(shape == "reference")
        {
            (promised, demanded) = (demanded, promised);
        }
        else if(shape == "real")
        {
            promised = fragment.Built.Type("Double");
            demanded = Positive(promised, 1.0);
        }
        else if(shape == "opaque")
        {
            promised = fragment.Built.Type("String");
            demanded = new RefinedType(promised, HandBuiltModel.Node(), "Letters")
            { Predicate = new RegexPattern(HandBuiltModel.Node(), promised, "[a-z]+") };
        }

        var contract = fragment.Abstract("Takes");
        var concrete = fragment.Class("Taker");
        concrete.BaseAbstractTypes.Add(contract);
        if(shape == "setter")
        {
            contract.PropertiesList.Add(new Property("Take", promised, DeclarationFragments.Public)
            { DeclaringType = contract, Setter = new PropertyAccessor { IsAuto = true } });
            concrete.PropertiesList.Add(new Property("Take", demanded, DeclarationFragments.Public)
            { DeclaringType = concrete, DeclarationNode = HandBuiltModel.Node(20), Setter = new PropertyAccessor { IsAuto = true } });
        }
        else
        {
            var modifier = new ParameterModifier { IsRef = shape == "reference" };
            var slot = DeclarationFragments.Method(contract, "Take", integer);
            slot.ParametersList.Add(new Parameter("value", promised, modifier));
            var filler = DeclarationFragments.Method(concrete, "Take", integer);
            filler.ParametersList.Add(new Parameter("value", demanded, modifier));
        }

        var finding = Assert.Single(Contracts(fragment, concrete));
        Assert.Equal(ProgramAnalysisDiagnostics.ConstraintStrengthened.Number, finding.Number);
        Assert.Equal($"'Take' constrains parameter 'value' more than the member it implements: '{known}' does not imply '{required}'.", finding.Message);

        if(shape is "integral" or "real")
        {
            Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[2]).Value);
            Assert.Equal(new NameValue(known), Assert.IsType<OptionalValue>(finding.Arguments[3]).Value);
            Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[4]).Value);
            Assert.Equal(new NameValue(required), Assert.IsType<OptionalValue>(finding.Arguments[5]).Value);
            return;
        }

        var promisedSnapshot = OptionalType(finding, 2);
        var demandedSnapshot = OptionalType(finding, 4);
        Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[3]).Value);
        Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[5]).Value);
        Assert.Equal(DiagnosticSnapshot.Type(promised, known), promisedSnapshot);
        Assert.Equal(DiagnosticSnapshot.Type(demanded, required), demandedSnapshot);
        ((MistType)promised).Assembly = new MistAssembly("ChangedPromised", MistAssemblyKind.Reference);
        ((MistType)demanded).Assembly = new MistAssembly("ChangedDemanded", MistAssemblyKind.Reference);
        Assert.Equal(known, promisedSnapshot.Spelling);
        Assert.Equal(required, demandedSnapshot.Spelling);
        Assert.NotEqual(DiagnosticSnapshot.Type(promised).Identity, promisedSnapshot.Identity);
        Assert.NotEqual(DiagnosticSnapshot.Type(demanded).Identity, demandedSnapshot.Identity);
    }

    private static RefinedType Positive(IMistType integer, object? minimum = null)
    {
        var predicate = new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.GreaterThanOrEqual,
            HandBuiltModel.Literal(integer, minimum ?? 1L));
        return new RefinedType(integer, HandBuiltModel.Node(), "Positive") { Predicate = predicate };
    }

    private static IReadOnlyList<Diagnostic> Contracts(DeclarationFragments fragment, ConcreteType concrete)
    {
        var bag = new DiagnosticBag();
        var context = fragment.Built.Context(new RefinementAnalyzer(), bag, types: fragment.Types);
        RefinementContracts.Judge(context, concrete, new LiftedReturns());
        return bag.ToImmutableArray();
    }

    private static TypeValue OptionalType(Diagnostic finding, int index)
    {
        return Assert.IsType<TypeValue>(Assert.IsType<OptionalValue>(finding.Arguments[index]).Value);
    }
}