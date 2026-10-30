using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CloneDiagnosticCauseTests
{
    [Fact]
    public void EqualCloneFailuresKeepDistinctCausesAndNeverQuoteThem()
    {
        var source = new ClassType("Source", "Probe", new Scope(null), null, null);
        var clone = Clone(source);
        var bag = new DiagnosticBag(PipelineStage.SemanticModelConstruction);
        var location = new SourceRange(new SourceIdentity(new SourceText("source.mist", "failure")), 0, 7);
        var causes = new List<Diagnostic>();
        bag.ReportTyped(ProgramAnalysisDiagnostics.NotDefaultable, location, DiagnosticSnapshot.Type(source));

        CloneDiagnostics.Run([clone], _ =>
        {
            for(var i = 0; i < 2; i++)
            {
                var admission = DiagnosticFactory.Create(ProgramAnalysisDiagnostics.NotDefaultable, location, bag.Stage,
                    [DiagnosticSnapshot.Type(source)]);
                bag.Admit(admission);
                causes.Add(admission.Occurrence);
            }
        }, bag);

        var findings = bag.ToImmutableArray().Skip(1).ToArray();
        Assert.Equal(2, findings.Length);
        Assert.NotEqual(causes[0].Ordinal, causes[1].Ordinal);
        Assert.Equal(causes.Select(cause => cause.Ordinal), findings.Select(finding => finding.Cause!.Value));
        Assert.All(findings, finding =>
        {
            Assert.Equal(ProgramModelDiagnostics.NewtypeCloneInvalid.Number, finding.Number);
            Assert.DoesNotContain("has no default", finding.Message);
            Assert.Equal(3, finding.Arguments.Length);
        });
    }

    [Theory]
    [InlineData("clone.mist", "newtype", false)]
    [InlineData("other.mist", "newtype", true)]
    [InlineData("clone.mist", "changed", true)]
    public void CloneContainmentRequiresTheSameFileIdentity(string path, string text, bool wraps)
    {
        var source = new ClassType("Source", "Probe", new Scope(null), null, null);
        var clone = Clone(source);
        var bag = new DiagnosticBag();
        var location = new SourceRange(new SourceIdentity(new SourceText(path, text)), 0, 1);
        Diagnostic? cause = null;

        CloneDiagnostics.Run([clone], _ =>
        {
            var admission = DiagnosticFactory.Create(ProgramAnalysisDiagnostics.NotDefaultable, location, bag.Stage,
                [DiagnosticSnapshot.Type(source)]);
            bag.Admit(admission);
            cause = admission.Occurrence;
        }, bag);

        var finding = Assert.Single(bag.ToImmutableArray());
        Assert.NotNull(cause);
        if(wraps)
        {
            Assert.Equal(ProgramModelDiagnostics.NewtypeCloneInvalid.Number, finding.Number);
            Assert.Equal(cause.Ordinal, finding.Cause);
        }
        else
        {
            Assert.Same(cause, finding);
            Assert.Null(finding.Cause);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CloneReplayFollowsTheSourceProducerIdentity(bool sameNode)
    {
        var source = new ClassType("Source", "Probe", new Scope(null), null, null);
        var clone = Clone(source);
        var bag = new DiagnosticBag();
        var originalNode = new TrueLiteralExpressionPrimaryNode
        {
            Span = new Storm.Runtime.SourceSpan(0, 1),
            Origin = new ParsedOrigin(new SourceText("source.mist", "x")),
        };
        var reboundNode = sameNode ? originalNode : new TrueLiteralExpressionPrimaryNode
        {
            Span = originalNode.Span,
            Origin = originalNode.Origin,
        };
        bag.ReportAtNodeTyped(ProgramAnalysisDiagnostics.NotDefaultable, originalNode, null, DiagnosticSnapshot.Type(source));
        var original = Assert.Single(bag.Admitted);

        CloneDiagnostics.Run([clone], _ => bag.ReportAtNodeTyped(
            ProgramAnalysisDiagnostics.NotDefaultable, reboundNode, null, DiagnosticSnapshot.Type(source)), bag);

        Assert.Same(original, bag.Admitted[0]);
        Assert.Equal(sameNode ? 1 : 2, bag.Admitted.Count);
        if(!sameNode)
        {
            Assert.Equal(ProgramModelDiagnostics.NewtypeCloneInvalid.Number, bag.Admitted[1].Number);
        }
    }

    private static ClassType Clone(ClassType source)
    {
        return new ClassType("Clone", "Probe", new Scope(null), null, null)
        {
            CloneSource = source,
            CloneSourceType = source,
            NewtypeDeclaration = new NewtypeDeclarationNode
            {
                Span = new Storm.Runtime.SourceSpan(0, 7),
                Origin = new ParsedOrigin(new SourceText("clone.mist", "newtype")),
            },
        };
    }
}