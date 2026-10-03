using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: registered open closings are judged by the analyzer at the site that forwards the parameter.
[Trait("Tier", "2")]
public sealed class ConstraintForwardingSiteTests
{
    [Fact]
    public void UnconstrainedForwardingIsRefusedAtItsOwnSite()
    {
        var fragment = new DeclarationFragments();
        var gate = fragment.Class("Gate");
        var required = DeclarationFragments.Parameter(gate, gate.Scope, "T");
        required.ConstraintsList.Add(new TypeConstraint(fragment.Abstract("IShow")));
        var wrap = fragment.Class("Wrap");
        var forwarded = DeclarationFragments.Parameter(wrap, wrap.Scope, "U");
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(gate, [forwarded], HandBuiltModel.Node(55));

        var finding = Assert.Single(fragment.Run(new ConstraintAnalyzer()));
        Assert.Equal("MIST_PA_CONSTRAINT_NOT_SATISFIED", finding.Id.Value);
        Assert.Equal("hand-built.mist", finding.CompilationUnitPath);
        Assert.Equal(56, finding.Location.Start.Column);
    }

    [Fact]
    public void ASufficientChainOfBoundsIsAdmittedAtTypeAndMethodSites()
    {
        var fragment = new DeclarationFragments();
        var shown = fragment.Abstract("IShow");
        var gate = fragment.Class("Gate");
        var required = DeclarationFragments.Parameter(gate, gate.Scope, "T");
        required.ConstraintsList.Add(new TypeConstraint(shown));
        var take = DeclarationFragments.Method(gate, "Take", fragment.Built.Type("Int32"));
        var taken = DeclarationFragments.Parameter(take, gate.Scope, "M");
        taken.ConstraintsList.Add(new TypeConstraint(shown));
        var wrap = fragment.Class("Wrap");
        var forwarded = DeclarationFragments.Parameter(wrap, wrap.Scope, "U");
        var bounding = DeclarationFragments.Parameter(wrap, wrap.Scope, "V");
        forwarded.ConstraintsList.Add(new TypeConstraint(bounding));
        bounding.ConstraintsList.Add(new TypeConstraint(shown));
        var registry = fragment.Built.Model.ConstructorConstraintClosures;
        registry.RecordType(gate, [forwarded], HandBuiltModel.Node(55));
        registry.RecordMethod(take, [forwarded], TypeSubstitution.Empty.With(taken, forwarded), HandBuiltModel.Node(70));

        Assert.Empty(fragment.Run(new ConstraintAnalyzer()));
    }

    [Theory]
    [InlineData(TypeParameterConstraintKind.Struct, 1)]
    [InlineData(TypeParameterConstraintKind.Stampable, 0)]
    public void StampableForwardingIsJudgedOnce(TypeParameterConstraintKind offered, int findings)
    {
        var fragment = new DeclarationFragments();
        var gate = fragment.Class("Gate");
        var required = DeclarationFragments.Parameter(gate, gate.Scope, "T");
        required.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));
        var wrap = fragment.Class("Wrap");
        var forwarded = DeclarationFragments.Parameter(wrap, wrap.Scope, "U");
        forwarded.ConstraintsList.Add(new KeywordConstraint(offered));
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(gate, [forwarded], HandBuiltModel.Node(55));

        Assert.Equal(findings, fragment.Run(new ConstraintAnalyzer()).Count);
    }

    [Fact]
    public void AWholePackForwardedIsJudgedForStampableAlone()
    {
        var fragment = new DeclarationFragments();
        var gate = fragment.Class("Gate");
        var required = DeclarationFragments.Parameter(gate, gate.Scope, "Ts");
        required.PackKind = TypePackKind.Unbracketed;
        required.ConstraintsList.Add(new TypeConstraint(fragment.Abstract("IShow")));
        required.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));
        var wrap = fragment.Class("Wrap");
        var forwarded = DeclarationFragments.Parameter(wrap, wrap.Scope, "Us");
        forwarded.PackKind = TypePackKind.Unbracketed;
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(gate, [forwarded], HandBuiltModel.Node(55));

        var finding = Assert.Single(fragment.Run(new ConstraintAnalyzer()));
        Assert.Contains("'stampable'", finding.Message, StringComparison.Ordinal);
    }
}