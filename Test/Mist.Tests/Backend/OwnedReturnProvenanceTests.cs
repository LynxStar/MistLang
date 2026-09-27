// Tier 3 (Component): an `owned` return type is a promise of provenance -- the caller receives the only
// reference -- so a body may return only what it owns: a fresh allocation, an owned parameter or binding,
// or a value that cannot change. Returning this, an unrestricted parameter or a static would hand out an
// alias of something another holder still reaches, and is refused.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class OwnedReturnProvenanceTests : ProgramModelProbe
{
    private const string NotOwned = "MIST_PA_OWNED_RETURN_NOT_OWNED";

    public OwnedReturnProvenanceTests()
        : base("MistOwnedReturnProvenance_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public owned Box Same() { return this; }")]
    [InlineData("public static owned Box Id(Box x) { return x; }")]
    [InlineData("public static owned Box Get() { return Shared; }")]
    public void AnOwnedReturnOfWhatTheBodyDoesNotOwn_IsRefused(string member)
    {
        Assert.Equal(NotOwned, Assert.Single(Analyse(member)).Id.Value);
    }

    [Theory]
    [InlineData("public static owned Box Make() { return new Box(); }")]
    [InlineData("public static owned Box Build() { owned var b = new Box(); return b; }")]
    [InlineData("public static owned Box Pass(owned Box x) { return x; }")]
    [InlineData("public static owned string Text() { return \"text\"; }")]
    [InlineData("public static owned int Num() { return 1; }")]
    public void AnOwnedReturnOfWhatTheBodyOwnsOrCannotChange_IsAdmitted(string member)
    {
        Assert.Empty(Analyse(member));
    }

    private List<Diagnostic> Analyse(string member)
    {
        var model = BuildModel("", $"public class Box {{ public int N; public static Box Shared = new Box(); {member} }}", "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id.Value == NotOwned)];
    }
}