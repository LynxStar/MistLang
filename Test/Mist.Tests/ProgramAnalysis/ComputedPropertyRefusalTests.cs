using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ComputedPropertyRefusalTests
{
    [Fact]
    public void AFieldReferenceInAComputedProperty_IsReportedOnceAtTheReference()
    {
        var fragment = new DeclarationFragments();
        var reference = new BackingFieldReferenceExpression(HandBuiltModel.Node(20), Property(computed: true));
        fragment.Root = reference;

        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));

        Assert.Equal(ProgramAnalysisDiagnostics.FieldInComputedProperty.Number, finding.Number);
        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Equal(DiagnosticHelper.ResolveLocation(reference.Node).Location, finding.Location);
        Assert.Contains("'Value'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldReferenceBesideASetter_IsNotReported()
    {
        var fragment = new DeclarationFragments
        {
            Root = new BackingFieldReferenceExpression(HandBuiltModel.Node(20), Property(computed: false)),
        };

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    private static Property Property(bool computed)
    {
        return new Property("Value", new HandBuiltModel().Type("Int32"), DeclarationFragments.Public)
        {
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block() },
            Setter = computed ? null : new PropertyAccessor { Body = HandBuiltModel.Block() },
            UsesBackingField = true,
        };
    }
}