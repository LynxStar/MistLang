using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ConstraintKeywordValidationTests
{
    [Theory]
    [InlineData("class", "struct")]
    [InlineData("struct", "class")]
    public void ClassWithStructIsRefusedOnceNamingClassThenStruct(string written, string then)
    {
        var f = new BindingFragments();
        var parameter = new TypeParameter("T", 0, f.Scope, null);
        var clause = Clause("T", 12, Keyword(written), Keyword(then));

        new ConstraintResolver(f.Builder, f.Builder.Resolver).Resolve([clause], [parameter], f.Scope);

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal("MIST0339", finding.Number.Value);
        Assert.Equal(new SlotValue[] { new NameValue("T"), new EnumValue("class"), new EnumValue("struct") }, finding.Arguments);
        Assert.Equal(DiagnosticHelper.ResolveLocation(clause).Location, finding.Location);
    }

    [Theory]
    [InlineData("class", "stampable")]
    [InlineData("readonly", "mutable")]
    [InlineData("linear", "affine")]
    [InlineData("linear", "relevant")]
    [InlineData("affine", "relevant")]
    public void AMutuallyExclusivePairIsRefusedOnceNamingBothKeywords(string written, string then)
    {
        var f = new BindingFragments();
        var parameter = new TypeParameter("T", 0, f.Scope, null);
        var clause = Clause("T", 12, Keyword(written), Keyword(then));

        new ConstraintResolver(f.Builder, f.Builder.Resolver).Resolve([clause], [parameter], f.Scope);

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal("MIST0339", finding.Number.Value);
        Assert.Equal(new NameValue("T"), finding.Arguments[0]);
        Assert.Equal(
            new[] { written, then }.Order(),
            finding.Arguments.Skip(1).Select(argument => Assert.IsType<EnumValue>(argument).Token).Order());
        Assert.Equal(DiagnosticHelper.ResolveLocation(clause).Location, finding.Location);
    }

    [Theory]
    [InlineData("struct", "stampable")]
    [InlineData("linear", "class")]
    [InlineData("relevant", "struct")]
    public void ACompatibleKeywordPairIsAccepted(string written, string then)
    {
        var f = new BindingFragments();
        var parameter = new TypeParameter("T", 0, f.Scope, null);

        new ConstraintResolver(f.Builder, f.Builder.Resolver).Resolve([Clause("T", 12, Keyword(written), Keyword(then))], [parameter], f.Scope);

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.Equal(2, parameter.ConstraintsList.Count);
    }

    [Fact]
    public void ConstructorSignaturesBesideABaseClassAreAllKeptAndNoneIsADuplicate()
    {
        var f = new BindingFragments();
        var animal = HandBuiltModel.Class("Animal");
        f.Register(animal);
        var parameter = new TypeParameter("T", 0, f.Scope, null);
        var clause = Clause(
            "T", 12,
            new TypeConstraintConstraintNode { Type = BindingFragments.TypeName("Animal") },
            new ConstructorConstraintConstraintNode(),
            new ConstructorConstraintConstraintNode
            {
                ParamTypes = new ConstraintParameterTypeListNode { TypeReference = BindingFragments.TypeName("Int32") },
            });

        new ConstraintResolver(f.Builder, f.Builder.Resolver).Resolve([clause], [parameter], f.Scope);

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.Collection(
            parameter.ConstraintsList,
            constraint => Assert.Same(animal, Assert.IsType<TypeConstraint>(constraint).ConstraintType),
            constraint => Assert.Empty(Assert.IsType<ConstructorConstraint>(constraint).ParameterTypes),
            constraint => Assert.Same(f.Built.Type("Int32"), Assert.Single(Assert.IsType<ConstructorConstraint>(constraint).ParameterTypes)));
    }

    [Theory]
    [InlineData("class", TypeParameterConstraintKind.Class, 2)]
    [InlineData("struct", TypeParameterConstraintKind.Struct, 2)]
    [InlineData("struct", TypeParameterConstraintKind.Struct, 3)]
    [InlineData("stampable", TypeParameterConstraintKind.Stampable, 2)]
    [InlineData("readonly", TypeParameterConstraintKind.Readonly, 2)]
    [InlineData("mutable", TypeParameterConstraintKind.Mutable, 2)]
    [InlineData("owned", TypeParameterConstraintKind.Owned, 2)]
    [InlineData("linear", TypeParameterConstraintKind.Linear, 2)]
    [InlineData("affine", TypeParameterConstraintKind.Affine, 2)]
    [InlineData("relevant", TypeParameterConstraintKind.Relevant, 2)]
    [InlineData("default", TypeParameterConstraintKind.Default, 2)]
    public void ARepeatedKeywordIsReportedOncePerRedundantOccurrence(string keyword, TypeParameterConstraintKind kind, int occurrences)
    {
        var f = new BindingFragments();
        var parameter = new TypeParameter("T", 0, f.Scope, null);
        var clause = Clause("T", 12, [.. Enumerable.Range(0, occurrences).Select(_ => Keyword(keyword))]);

        new ConstraintResolver(f.Builder, f.Builder.Resolver).Resolve([clause], [parameter], f.Scope);

        Assert.Equal(occurrences - 1, f.Diagnostics.Admitted.Count);
        Assert.All(f.Diagnostics.Admitted, finding =>
        {
            Assert.Equal("MIST0340", finding.Number.Value);
            Assert.Equal(new SlotValue[] { new NameValue("T"), new EnumValue(keyword) }, finding.Arguments);
            Assert.Equal(DiagnosticHelper.ResolveLocation(clause).Location, finding.Location);
        });
        Assert.NotEmpty(parameter.ConstraintsList);
        Assert.All(parameter.ConstraintsList, constraint => Assert.Equal(kind, constraint.Kind));
    }

    [Fact]
    public void ADuplicateAndAConflictOnOneParameterAreBothReported()
    {
        var f = new BindingFragments();
        var parameter = new TypeParameter("T", 0, f.Scope, null);
        var clause = Clause("T", 12, Keyword("class"), Keyword("class"), Keyword("struct"));

        new ConstraintResolver(f.Builder, f.Builder.Resolver).Resolve([clause], [parameter], f.Scope);

        Assert.Equal(2, f.Diagnostics.Admitted.Count);
        var duplicate = Assert.Single(f.Diagnostics.Admitted, finding => finding.Number.Value == "MIST0340");
        Assert.Equal(new SlotValue[] { new NameValue("T"), new EnumValue("class") }, duplicate.Arguments);
        var conflict = Assert.Single(f.Diagnostics.Admitted, finding => finding.Number.Value == "MIST0339");
        Assert.Equal(new SlotValue[] { new NameValue("T"), new EnumValue("class"), new EnumValue("struct") }, conflict.Arguments);
    }

    [Fact]
    public void EachParameterIsValidatedOnItsOwnConstraintsAtItsOwnClause()
    {
        var f = new BindingFragments();
        var first = new TypeParameter("T", 0, f.Scope, null);
        var second = new TypeParameter("U", 1, f.Scope, null);
        var repeated = Clause("U", 40, Keyword("class"), Keyword("class"));

        new ConstraintResolver(f.Builder, f.Builder.Resolver).Resolve([Clause("T", 12, Keyword("struct")), repeated], [first, second], f.Scope);

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal("MIST0340", finding.Number.Value);
        Assert.Equal(new SlotValue[] { new NameValue("U"), new EnumValue("class") }, finding.Arguments);
        Assert.Equal(DiagnosticHelper.ResolveLocation(repeated).Location, finding.Location);
        Assert.Equal(40, Assert.IsType<SourceRange>(finding.Location).Offset);
    }

    private static GenericConstraintClauseWhereClauseNode Clause(string name, int offset, params ConstraintNode[] constraints)
    {
        var clause = BindingFragments.At<GenericConstraintClauseWhereClauseNode>(offset);
        clause.Name = BindingFragments.Token(name);
        clause.Constraints = new ConstraintListNode { Constraint = constraints[0], Constraints = [.. constraints.Skip(1)] };
        return clause;
    }

    private static ConstraintNode Keyword(string keyword)
    {
        return keyword switch
        {
            "class" => new ClassConstraintConstraintNode(),
            "struct" => new StructConstraintConstraintNode(),
            "stampable" => new StampableConstraintConstraintNode(),
            "readonly" => new ReadonlyConstraintConstraintNode(),
            "mutable" => new MutableConstraintConstraintNode(),
            "owned" => new OwnedConstraintConstraintNode(),
            "linear" => new LinearConstraintConstraintNode(),
            "affine" => new AffineConstraintConstraintNode(),
            "relevant" => new RelevantConstraintConstraintNode(),
            "default" => new DefaultConstraintConstraintNode(),
            _ => throw new ArgumentOutOfRangeException(nameof(keyword), keyword, null),
        };
    }
}