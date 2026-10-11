using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
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

    [Theory]
    [InlineData("whole", "", "'IShow', 'stampable'")]
    [InlineData("whole", "IShow", "'stampable'")]
    [InlineData("whole", "IShow, stampable", "")]
    [InlineData("spread", "", "'IShow', 'stampable'")]
    [InlineData("spread", "IShow, stampable", "")]
    [InlineData("element", "", "'IShow'")]
    [InlineData("element", "IShow", "")]
    public void AForwardedPackOrElement_IsJudgedForEveryConstraintItsPackLacks(string forwarding, string declared, string refused)
    {
        var fragment = new DeclarationFragments();
        var shown = fragment.Abstract("IShow");
        var gate = fragment.Class("Gate");
        var required = DeclarationFragments.Parameter(gate, gate.Scope, "Ts");
        required.PackKind = forwarding == "element" ? TypePackKind.None : TypePackKind.Unbracketed;
        required.ConstraintsList.Add(new TypeConstraint(shown));
        if(forwarding != "element")
        {
            required.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));
        }

        var wrap = fragment.Class("Wrap");
        var forwarded = DeclarationFragments.Parameter(wrap, wrap.Scope, "Us");
        forwarded.PackKind = TypePackKind.Unbracketed;
        forwarded.PackLength = 2;
        foreach(var offered in declared.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            forwarded.ConstraintsList.Add(offered == "IShow" ? new TypeConstraint(shown) : new KeywordConstraint(TypeParameterConstraintKind.Stampable));
        }

        IMistType argument = forwarding switch
        {
            "whole" => forwarded,
            "spread" => Pack(fragment, new SpreadType(forwarded)),
            _ => forwarded.ElementAt(1),
        };
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(gate, [argument], HandBuiltModel.Node(55));

        Assert.Equal(refused, string.Join(", ", fragment.Run(new ConstraintAnalyzer()).Select(Spelled).Order(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("Int32", 1)]
    [InlineData("T", 1)]
    [InlineData("Shown, T", 1)]
    [InlineData("Shown, shown T, Us...", 1)]
    [InlineData("Shown, shown T, shown Us...", 0)]
    public void EachPositionOfAPackValue_IsJudgedByWhatFillsIt(string elements, int findings)
    {
        var fragment = new DeclarationFragments();
        var shown = fragment.Abstract("IShow");
        var widget = fragment.Class("Shown");
        widget.BaseAbstractTypes.Add(shown);
        var gate = fragment.Class("Gate");
        var required = DeclarationFragments.Parameter(gate, gate.Scope, "Ts");
        required.PackKind = TypePackKind.Unbracketed;
        required.ConstraintsList.Add(new TypeConstraint(shown));
        var wrap = fragment.Class("Wrap");

        var filled = elements.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(element =>
        {
            var offering = element.StartsWith("shown ", StringComparison.Ordinal);
            var name = offering ? element[6..] : element;

            if(name is "Int32" or "Shown")
            {
                return name == "Shown" ? widget : (IMistType)fragment.Built.Type(name);
            }

            var parameter = DeclarationFragments.Parameter(wrap, wrap.Scope, name.TrimEnd('.'));
            parameter.PackKind = name.EndsWith("...", StringComparison.Ordinal) ? TypePackKind.Unbracketed : TypePackKind.None;
            if(offering)
            {
                parameter.ConstraintsList.Add(new TypeConstraint(shown));
            }

            return parameter.IsPack ? new SpreadType(parameter) : parameter;
        }).ToArray();
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(gate, [Pack(fragment, filled)], HandBuiltModel.Node(55));

        Assert.Equal(findings, fragment.Run(new ConstraintAnalyzer()).Count);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void ABoundNamingThePack_IsComparedAfterEachSideIsRebound(bool rankedAgainstItself, int findings)
    {
        var fragment = new DeclarationFragments();
        var rank = fragment.Abstract("IRank<>");
        _ = DeclarationFragments.Parameter(rank, rank.Scope, "T");
        var gate = fragment.Class("Gate");
        var required = DeclarationFragments.Parameter(gate, gate.Scope, "Ts");
        required.PackKind = TypePackKind.Unbracketed;
        required.ConstraintsList.Add(new TypeConstraint(fragment.Built.Interner.GetOrCreateConstructedType(rank, [required])));
        var wrap = fragment.Class("Wrap");
        var forwarded = DeclarationFragments.Parameter(wrap, wrap.Scope, "Us");
        forwarded.PackKind = TypePackKind.Unbracketed;
        forwarded.ConstraintsList.Add(new TypeConstraint(
            fragment.Built.Interner.GetOrCreateConstructedType(rank, [rankedAgainstItself ? forwarded : fragment.Built.Type("Int32")])));
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(gate, [Pack(fragment, new SpreadType(forwarded))], HandBuiltModel.Node(55));

        Assert.Equal(findings, fragment.Run(new ConstraintAnalyzer()).Count);
    }

    private static LiftedType Pack(DeclarationFragments fragment, params IMistType[] elements)
    {
        return new LiftedType(fragment.Built.Type("Type"), new TypePackValue(elements));
    }

    private static string Spelled(Diagnostic finding)
    {
        return finding.Message.Contains("'stampable'", StringComparison.Ordinal) ? "'stampable'" : "'IShow'";
    }
}