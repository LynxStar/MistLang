using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ConstraintClauseTargetTests
{
    [Theory]
    [InlineData("U")]
    [InlineData("x")]
    public void AGenericClauseOverNoDeclaredTypeParameterIsRefusedAndTheDeclaredClauseStands(string undeclared)
    {
        var f = new BindingFragments();
        var declared = new TypeParameter("T", 0, f.Scope, null);
        var value = new Parameter("x", f.Built.Type("Int32"));
        var stray = Generic(undeclared, 12, new ClassConstraintConstraintNode());
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        resolver.Resolve([stray, Generic("T", 40, new StructConstraintConstraintNode())], [declared], [value], f.Scope);

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal("MIST0338", finding.Number.Value);
        Assert.Equal(new SlotValue[] { new NameValue(undeclared) }, finding.Arguments);
        Assert.Equal(DiagnosticHelper.ResolveLocation(stray).Location, finding.Location);
        Assert.Equal(TypeParameterConstraintKind.Struct, Assert.Single(declared.ConstraintsList).Kind);
        Assert.Same(f.Built.Type("Int32"), value.Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAdhocClauseOverADependentTypeParameterStashesItsPredicateThere(bool onMember)
    {
        var f = new BindingFragments();
        var dependent = new TypeParameter("N", 0, f.Scope, null) { IsDependent = true };
        var value = new Parameter("x", f.Built.Type("Int32"));
        var clause = Adhoc("N", 12);
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        if(onMember)
        {
            resolver.Resolve([clause], [dependent], [value], f.Scope);
        }
        else
        {
            resolver.Resolve([clause], [dependent], f.Scope);
        }

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.Same(clause.Predicate, dependent.RefinementNode);
        Assert.Same(f.Built.Type("Int32"), value.Type);
    }

    [Fact]
    public void AnAdhocClauseOverAParameterRefinesThatParametersDeclaredType()
    {
        var f = new BindingFragments();
        var first = new Parameter("x", f.Built.Type("Int32"));
        var second = new Parameter("y", f.Built.Type("Int64"));
        var clause = Adhoc("y", 12);
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        resolver.Resolve([clause], [], [first, second], f.Scope);

        Assert.Empty(f.Diagnostics.Admitted);
        var refined = Assert.IsType<RefinedType>(second.Type);
        Assert.Same(f.Built.Type("Int64"), refined.BaseType);
        Assert.Same(clause.Predicate, refined.PredicateNode);
        Assert.True(refined.IsAnonymous);
        Assert.Same(f.Built.Type("Int32"), first.Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAdhocClauseOverNoDeclaredNameIsRefusedAndRefinesNothing(bool onMember)
    {
        var f = new BindingFragments();
        var dependent = new TypeParameter("N", 0, f.Scope, null) { IsDependent = true };
        var value = new Parameter("x", f.Built.Type("Int32"));
        var clause = Adhoc("z", 12);
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        if(onMember)
        {
            resolver.Resolve([clause], [dependent], [value], f.Scope);
        }
        else
        {
            resolver.Resolve([clause], [dependent], f.Scope);
        }

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal("MIST0338", finding.Number.Value);
        Assert.Equal(new SlotValue[] { new NameValue("z") }, finding.Arguments);
        Assert.Equal(DiagnosticHelper.ResolveLocation(clause).Location, finding.Location);
        Assert.Null(dependent.RefinementNode);
        Assert.Same(f.Built.Type("Int32"), value.Type);
    }

    [Fact]
    public void ACloneClauseConstrainsTheParameterItsSourceNameIsBoundTo()
    {
        var f = new BindingFragments();
        var first = new TypeParameter("A", 0, f.Scope, null);
        var second = new TypeParameter("B", 1, f.Scope, null);
        var targets = new Dictionary<string, TypeParameter> { ["T"] = second, ["V"] = first };
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        resolver.ResolveRenamed(
            [Generic("T", 12, new ClassConstraintConstraintNode()), Generic("V", 40, new StructConstraintConstraintNode())], targets, f.Scope);

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.Equal(TypeParameterConstraintKind.Class, Assert.Single(second.ConstraintsList).Kind);
        Assert.Equal(TypeParameterConstraintKind.Struct, Assert.Single(first.ConstraintsList).Kind);
    }

    [Fact]
    public void ACloneClauseOverAParameterTheNewtypeBoundToATypeIsSkippedWithoutAFinding()
    {
        var f = new BindingFragments();
        var kept = new TypeParameter("K", 0, f.Scope, null);
        var targets = new Dictionary<string, TypeParameter> { ["T"] = kept };
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        resolver.ResolveRenamed([Generic("V", 12, new StructConstraintConstraintNode()), Adhoc("N", 40)], targets, f.Scope);

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.Empty(kept.ConstraintsList);
        Assert.Null(kept.RefinementNode);
    }

    [Fact]
    public void ACloneAdhocClauseStashesItsPredicateOnTheBoundParameter()
    {
        var f = new BindingFragments();
        var kept = new TypeParameter("M", 0, f.Scope, null) { IsDependent = true };
        var targets = new Dictionary<string, TypeParameter> { ["N"] = kept };
        var clause = Adhoc("N", 12);
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        resolver.ResolveRenamed([clause], targets, f.Scope);

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.Same(clause.Predicate, kept.RefinementNode);
    }

    [Fact]
    public void ACloneParameterBoundToTwoSourceParametersIsValidatedOnce()
    {
        var f = new BindingFragments();
        var kept = new TypeParameter("K", 0, f.Scope, null);
        var targets = new Dictionary<string, TypeParameter> { ["A"] = kept, ["B"] = kept };
        var clause = Generic("A", 12, new ClassConstraintConstraintNode(), new ClassConstraintConstraintNode());
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        resolver.ResolveRenamed([clause], targets, f.Scope);

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal("MIST0340", finding.Number.Value);
        Assert.Equal(new SlotValue[] { new NameValue("K"), new EnumValue("class") }, finding.Arguments);
        Assert.Equal(DiagnosticHelper.ResolveLocation(clause).Location, finding.Location);
    }

    [Fact]
    public void ACloneFindingIsReportedAtTheClauseBoundToItsParameter()
    {
        var f = new BindingFragments();
        var first = new TypeParameter("K", 0, f.Scope, null);
        var second = new TypeParameter("L", 1, f.Scope, null);
        var targets = new Dictionary<string, TypeParameter> { ["T"] = first, ["V"] = second };
        var repeated = Generic("V", 40, new StructConstraintConstraintNode(), new StructConstraintConstraintNode());
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        resolver.ResolveRenamed([Generic("T", 12, new ClassConstraintConstraintNode()), repeated], targets, f.Scope);

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal("MIST0340", finding.Number.Value);
        Assert.Equal(new SlotValue[] { new NameValue("L"), new EnumValue("struct") }, finding.Arguments);
        Assert.Equal(DiagnosticHelper.ResolveLocation(repeated).Location, finding.Location);
        Assert.Equal(40, Assert.IsType<SourceRange>(finding.Location).Offset);
    }

    private static GenericConstraintClauseWhereClauseNode Generic(string name, int offset, params ConstraintNode[] constraints)
    {
        var clause = BindingFragments.At<GenericConstraintClauseWhereClauseNode>(offset);
        clause.Name = BindingFragments.Token(name);
        clause.Constraints = new ConstraintListNode { Constraint = constraints[0], Constraints = [.. constraints.Skip(1)] };
        return clause;
    }

    private static AdhocRefinementConstraintWhereClauseNode Adhoc(string name, int offset)
    {
        var clause = BindingFragments.At<AdhocRefinementConstraintWhereClauseNode>(offset);
        clause.Name = BindingFragments.Token(name);
        clause.Predicate = BindingFragments.At<AndPatternNode>(offset + 2);
        return clause;
    }
}