using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class AnalysisDiagnosticProducerTests
{
    [Fact]
    public void TypedReportingKeepsTheTypeSnapshotAndPositiveFailureSuppression()
    {
        var built = new HandBuiltModel();
        var type = built.Type("Int32");
        var reported = HandBuiltModel.Literal(type);
        var suppressed = HandBuiltModel.Literal(type, offset: 1);
        var failures = new ExpressionFailures();
        failures.Record(suppressed);
        var bag = new DiagnosticBag();
        var context = built.Context(new DefaultabilityAnalyzer(), bag, failures: failures);

        context.Report(ProgramAnalysisDiagnostics.NotDefaultable, reported.Node, [reported], DiagnosticSnapshot.Type(type));
        context.Report(ProgramAnalysisDiagnostics.NotDefaultable, suppressed.Node, [suppressed], DiagnosticSnapshot.Type(type));
        context.Report(ProgramAnalysisDiagnostics.NotDefaultable, reported.Node, [new ErrorExpression(reported.Node)], DiagnosticSnapshot.Type(type));

        var finding = Assert.Single(bag.ToImmutableArray());
        var argument = Assert.IsType<TypeValue>(Assert.Single(finding.Arguments));
        Assert.Contains(type.FullyQualifiedName, argument.Identity);
        Assert.Equal("int", argument.Spelling);
        Assert.Equal(PipelineStage.Analysis, finding.Stage);
    }

    [Fact]
    public void RelatedDeclarationKeepsItsLocationOutsideTheArguments()
    {
        var built = new HandBuiltModel();
        var bag = new DiagnosticBag();
        var context = built.Context(new DeclarationShapeAnalyzer(), bag);
        var previous = DiagnosticHelper.ResolveLocation(HandBuiltModel.Node(0, "previous.mist")).Location;
        var later = HandBuiltModel.Node(2);

        context.ReportRelated(ProgramAnalysisDiagnostics.DuplicateExtensionProperty, later, [],
            [new RelatedLocation(RelatedRole.PreviousDeclaration, previous)],
            new NameValue("Value"), DiagnosticSnapshot.Type(built.Type("Int32")));

        var finding = Assert.Single(bag.ToImmutableArray());
        Assert.Equal(previous, Assert.Single(finding.Related).Location);
        Assert.Equal(2, finding.Arguments.Length);
        Assert.DoesNotContain("previous.mist", finding.Message);
        Assert.Equal("hand-built.mist", finding.Location.Path);
    }

    [Fact]
    public void ProjectFindingUsesTheManifestLocation()
    {
        var built = new HandBuiltModel();
        var bag = new DiagnosticBag();
        var context = built.Context(new EntryPointAnalyzer(), bag);
        var project = new MistAssembly("App", MistAssemblyKind.Self, "app/mist.toml");

        context.ReportAtProject(EntryPointDiagnostics.MissingEntryPoint, project, new NameValue(project.Name));

        var finding = Assert.Single(bag.ToImmutableArray());
        Assert.Equal("app/mist.toml", Assert.IsType<ProjectLocation>(finding.Location).ManifestPath);
    }

    [Fact]
    public void ADefaultWrapperReservesItsCauseWithoutQuotingOrDuplicatingIt()
    {
        var built = new HandBuiltModel();
        var bag = new DiagnosticBag();
        var context = built.Context(new PurityAnalyzer(), bag);
        var cause = new Finding(ProgramAnalysisDiagnostics.PureScopeWrites, HandBuiltModel.Node(), [],
            [new NameValue("state"), new NameValue("a pure scope"), new NameValue("it is external")]);

        context.ReportWrapped(ProgramAnalysisDiagnostics.DefaultNotConsistent, cause,
            new NameValue("This default"), DiagnosticSnapshot.Type(built.Type("Int32")));

        var wrapper = Assert.Single(bag.ToImmutableArray());
        _ = Assert.NotNull(wrapper.Cause);
        Assert.True(wrapper.Cause > 0);
        Assert.True(wrapper.Cause < wrapper.Ordinal);
        Assert.DoesNotContain("it is external", wrapper.Message);
    }
}