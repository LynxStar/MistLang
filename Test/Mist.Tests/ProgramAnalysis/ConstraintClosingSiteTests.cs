using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: registered type, method and pack closings report on their written closing site.
[Trait("Tier", "2")]
public sealed class ConstraintClosingSiteTests
{
    [Theory]
    [InlineData("type")]
    [InlineData("method")]
    [InlineData("pack-element-against-a-constraint-naming-its-pack")]
    public void ClosingReportsItsOwnNode(string site)
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Gate");
        var registry = fragment.Built.Model.ConstructorConstraintClosures;
        var node = HandBuiltModel.Node(55);

        if(site == "pack-element-against-a-constraint-naming-its-pack")
        {
            var ranked = fragment.Abstract("IRank");
            _ = DeclarationFragments.Parameter(ranked, ranked.Scope, "T");
            var pack = DeclarationFragments.Parameter(owner, owner.Scope, "Ts");
            pack.PackKind = TypePackKind.Unbracketed;
            pack.ConstraintsList.Add(new TypeConstraint(fragment.Built.Interner.GetOrCreateConstructedType(ranked, [pack])));
            var score = fragment.Class("Score");
            var grade = fragment.Class("Grade");
            var ranking = fragment.Built.Interner.GetOrCreateConstructedType(ranked, [score]);
            score.BaseAbstractTypes.Add(ranking);
            grade.BaseAbstractTypes.Add(ranking);
            var held = fragment.Built.Interner.LiftPack(fragment.Built.Type("Type"), new TypePackValue([score, grade]));
            registry.RecordType(owner, [held], node);
        }
        else if(site == "type")
        {
            var parameter = DeclarationFragments.Parameter(owner, owner.Scope, "T");
            parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
            registry.RecordType(owner, [HandBuiltModel.Struct("SomeStruct")], node);
        }
        else
        {
            var method = DeclarationFragments.Method(owner, "Take", fragment.Built.Type("Int32"));
            var parameter = DeclarationFragments.Parameter(method, owner.Scope, "T");
            parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
            var argument = HandBuiltModel.Struct("SomeStruct");
            registry.RecordMethod(method, [argument], TypeSubstitution.Empty.With(parameter, argument), node);
        }

        var finding = Assert.Single(fragment.Run(new ConstraintAnalyzer()));
        Assert.Equal("MIST0178", finding.Number.Value);
        Assert.Equal("hand-built.mist", finding.Location.Path);
        Assert.Equal(56, finding.Position().Start.Column);
    }
}