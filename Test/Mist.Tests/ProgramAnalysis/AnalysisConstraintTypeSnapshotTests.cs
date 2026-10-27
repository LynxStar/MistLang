using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class AnalysisConstraintTypeSnapshotTests
{
    [Theory]
    [InlineData("closing", false)]
    [InlineData("closing", true)]
    [InlineData("generic-slot", false)]
    [InlineData("generic-slot", true)]
    [InlineData("associated-slot", false)]
    [InlineData("associated-slot", true)]
    public void ConstraintsRetainKnownTypesAndKeepKeywordDescriptions(string family, bool keyword)
    {
        var fragment = new DeclarationFragments();
        var required = fragment.Abstract("Ranked");
        TypeParameterConstraint constraint = keyword ? new KeywordConstraint(TypeParameterConstraintKind.Struct) : new TypeConstraint(required);
        Diagnostic finding;
        int slot;
        if(family == "closing")
        {
            var owner = fragment.Class("Gate<>");
            var parameter = DeclarationFragments.Parameter(owner, owner.Scope, "T");
            parameter.ConstraintsList.Add(constraint);
            fragment.Built.Model.ConstructorConstraintClosures.RecordType(owner, [fragment.Class("Argument")], HandBuiltModel.Node(30));
            finding = Assert.Single(fragment.Run(new ConstraintAnalyzer()));
            Assert.Equal(ProgramAnalysisDiagnostics.ConstraintNotSatisfied.Number, finding.Number);
            slot = 1;
        }
        else
        {
            var contract = fragment.Abstract("Gate");
            var concrete = fragment.Class("Argument");
            concrete.BaseAbstractTypes.Add(contract);
            if(family == "generic-slot")
            {
                var integer = fragment.Built.Type("Int32");
                var promised = DeclarationFragments.Method(contract, "Take", integer);
                _ = DeclarationFragments.Parameter(promised, contract.Scope, "T");
                var filler = DeclarationFragments.Method(concrete, "Take", integer);
                DeclarationFragments.Parameter(filler, concrete.Scope, "T").ConstraintsList.Add(constraint);
                slot = 4;
            }
            else
            {
                var associated = new TypeSlot("Kind", 0, contract.Scope, contract);
                associated.ConstraintsList.Add(constraint);
                contract.TypeSlotsList.Add(associated);
                _ = fragment.Class("Kind", concrete);
                slot = 1;
            }

            finding = Assert.Single(fragment.Run(new ConformanceAnalyzer()));
            Assert.Equal(family == "generic-slot" ? ProgramAnalysisDiagnostics.SlotConstraintStrengthened.Number
                : ProgramAnalysisDiagnostics.TypeSlotConstraintNotSatisfied.Number, finding.Number);
        }

        Assert.Equal("hand-built.mist", finding.Location.Path);
        if(keyword)
        {
            Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[slot]).Value);
            Assert.Equal(new NameValue("struct"), Assert.IsType<OptionalValue>(finding.Arguments[slot + 1]).Value);
            Assert.Contains("'struct'", finding.Message, StringComparison.Ordinal);
        }
        else
        {
            var snapshot = Assert.IsType<TypeValue>(Assert.IsType<OptionalValue>(finding.Arguments[slot]).Value);
            Assert.Equal(DiagnosticSnapshot.Type(required), snapshot);
            Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[slot + 1]).Value);
            Assert.Contains("'Ranked'", finding.Message, StringComparison.Ordinal);
            required.Assembly = new MistAssembly("Changed", MistAssemblyKind.Reference);
            required.BaseAbstractTypes.Clear();
            Assert.NotEqual(DiagnosticSnapshot.Type(required).Identity, snapshot.Identity);
            Assert.Equal("Ranked", snapshot.Spelling);
            Assert.Contains("'Ranked'", finding.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DependentArgumentsRetainTypesAndKeepLiteralValues(bool literal)
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Gate<>");
        var parameter = DeclarationFragments.Parameter(owner, owner.Scope, "N");
        parameter.IsDependent = true;
        parameter.Domain = fragment.Built.Type("Boolean");
        var integer = fragment.Built.Type("Int32");
        IMistType argument = literal ? new LiftedType(integer, 42L) : fragment.Class("Argument");
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(owner, [argument], HandBuiltModel.Node(30));

        var finding = Assert.Single(fragment.Run(new RefinementAnalyzer()));

        Assert.Equal(ProgramAnalysisDiagnostics.DependentArgumentNotAssignable.Number, finding.Number);
        if(literal)
        {
            Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[0]).Value);
            Assert.Equal(new NameValue("42"), Assert.IsType<OptionalValue>(finding.Arguments[1]).Value);
            Assert.Equal("Dependent type argument '42' of type 'int' is not assignable to dependent parameter 'N' of type 'bool'.", finding.Message);
        }
        else
        {
            var snapshot = Assert.IsType<TypeValue>(Assert.IsType<OptionalValue>(finding.Arguments[0]).Value);
            Assert.Equal(DiagnosticSnapshot.Type(argument), snapshot);
            Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[1]).Value);
            ((MistType)argument).Assembly = new MistAssembly("Changed", MistAssemblyKind.Reference);
            Assert.NotEqual(DiagnosticSnapshot.Type(argument).Identity, snapshot.Identity);
            Assert.Equal("Argument", snapshot.Spelling);
            Assert.Equal("Dependent type argument 'Argument' of type 'Type' is not assignable to dependent parameter 'N' of type 'bool'.", finding.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardedDependentParametersRetainKnownOwners(bool detachedSource)
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Gate<>");
        var target = new TypeParameter("N", 0, owner.Scope, owner)
        { IsDependent = true, Domain = fragment.Built.Type("Boolean") };
        owner.TypeParametersList.Add(target);
        var sourceOwner = detachedSource ? null : fragment.Class("Source");
        var source = new TypeParameter("M", 0, sourceOwner?.Scope ?? new Scope(null), sourceOwner)
        { IsDependent = true, Domain = fragment.Built.Type("Int32") };
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(owner, [source], HandBuiltModel.Node(30));

        var finding = Assert.Single(fragment.Run(new RefinementAnalyzer()));

        Assert.Equal(ProgramAnalysisDiagnostics.DependentForwardNotConvertible.Number, finding.Number);
        var targetSnapshot = Assert.IsType<TypeValue>(Assert.IsType<OptionalValue>(finding.Arguments[6]).Value);
        Assert.Equal(DiagnosticSnapshot.Type(owner), targetSnapshot);
        Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[7]).Value);
        owner.Assembly = new MistAssembly("ChangedTarget", MistAssemblyKind.Reference);
        Assert.NotEqual(DiagnosticSnapshot.Type(owner).Identity, targetSnapshot.Identity);
        if(detachedSource)
        {
            Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[2]).Value);
            Assert.Equal(new NameValue("M"), Assert.IsType<OptionalValue>(finding.Arguments[3]).Value);
        }
        else
        {
            var sourceSnapshot = Assert.IsType<TypeValue>(Assert.IsType<OptionalValue>(finding.Arguments[2]).Value);
            Assert.Equal(DiagnosticSnapshot.Type(sourceOwner!), sourceSnapshot);
            Assert.Null(Assert.IsType<OptionalValue>(finding.Arguments[3]).Value);
            sourceOwner!.Assembly = new MistAssembly("ChangedSource", MistAssemblyKind.Reference);
            Assert.NotEqual(DiagnosticSnapshot.Type(sourceOwner).Identity, sourceSnapshot.Identity);
            Assert.Equal("Source", sourceSnapshot.Spelling);
        }

        var sourceName = detachedSource ? "M" : "Source";
        Assert.Equal($"Dependent type parameter 'M' of type 'int' on '{sourceName}' cannot be forwarded to parameter 'N' of type 'bool' on 'Gate' because 'int' is not implicitly convertible to 'bool'.", finding.Message);
    }

    [Fact]
    public void AConformingTypeArgumentStillMeetsItsConstraint()
    {
        var fragment = new DeclarationFragments();
        var required = fragment.Abstract("Ranked");
        var argument = fragment.Class("Argument");
        argument.BaseAbstractTypes.Add(required);
        var owner = fragment.Class("Gate<>");
        DeclarationFragments.Parameter(owner, owner.Scope, "T").ConstraintsList.Add(new TypeConstraint(required));
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(owner, [argument], HandBuiltModel.Node(30));

        Assert.Empty(fragment.Run(new ConstraintAnalyzer()));
    }
}