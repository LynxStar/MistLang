// Tier 3 (Component): purity Requirement 12.2 -- a refinement predicate may read a property whose getter
// is a pure member, declared or inferred, and still may not read one whose getter reads its instance's
// current state, a local readonly member being not enough there. The refinement alias is declared and
// never used, since the predicate is judged where it is declared.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class StablePureGetterTests : ProgramModelProbe
{
    private const string OverMutable = "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER";

    public StablePureGetterTests()
        : base("MistStablePureGetter_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public pure int Sum() { return A + B; }")]
    [InlineData("public int Sum() { return A + B; }")]
    [InlineData("public int Sum() { return Twice(A) + B; } public int Twice(int n) { return n * 2; }")]
    [InlineData("public static pure int Sum() { return 3; }")]
    public void AGetterCallingAPureMember_IsStableEnoughForAPredicate(string members)
    {
        Assert.Empty(Analyse($$"""
            public class Jar
            {
                public readonly int A;
                public readonly int B;

                public pure Jar(int a, int b) { A = a; B = b; }

                {{members}}

                public int Both { get { return Sum(); } }
            }

            type Rich = Jar { Both: > 1 };
            """));
    }

    [Theory]
    [InlineData("public int A; public int Both { get { return A; } }")]
    [InlineData("public int A; public int Cur() { return A; } public int Both { get { return Cur(); } }")]
    [InlineData("public int A; public local readonly int Cur() { return A; } public int Both { get { return Cur(); } }")]
    [InlineData("public readonly int A; public int Both { get { return A + Globals.Counter; } }")]
    [InlineData("public readonly int A; public int Now() { return Globals.Counter; } public int Both { get { return A + Now(); } }")]
    public void AGetterReadingWhatCanChange_IsRefusedForAPredicate(string members)
    {
        var finding = Assert.Single(Analyse($$"""
            public class Globals { public static int Counter; }

            public class Jar
            {
                {{members}}
            }

            type Rich = Jar { Both: > 1 };
            """));

        Assert.Equal(OverMutable, finding.Id.Value);
        Assert.Contains("Both", finding.Message, StringComparison.Ordinal);
    }

    private List<Diagnostic> Analyse(string extraTypes)
    {
        var model = BuildModel("", extraTypes, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id.Value == OverMutable)];
    }
}