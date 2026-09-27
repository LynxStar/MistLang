// Tier 3 (Component): two contracts the purity spec states and no single body can be trusted to keep --
// Requirement 8.4, an abstract member's declared promise binds every implementation in the program, and
// Requirement 16.5 met at a runtime type test: the promise is erased on emission, so `is`, `as` and a
// cast against a pure function type cannot tell a pure value from a plain one and are refused unless the
// static type already answers.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class PurityContractTests : ProgramModelProbe
{
    private const string ContractNotMet = "MIST_PA_PURITY_CONTRACT_NOT_MET";
    private const string Untestable = "MIST_PA_PURE_FUNCTION_TYPE_UNTESTABLE";
    private const string NoConversion = "MIST_PA_CAST_HAS_NO_CONVERSION";

    private static readonly string[] Ids =
    [
        ContractNotMet, Untestable, NoConversion, "MIST_PA_PURE_SCOPE_WRITES", "MIST_PA_PURE_SCOPE_READS_MUTABLE",
        "MIST_PA_PURE_SCOPE_CALLS_IMPURE", "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT", "MIST_PA_PURE_SCOPE_STORES_UNOWNED",
        "MIST_PA_PURE_FUNCTION_REQUIRED", "MIST_PA_DEFAULT_NOT_CONSISTENT",
    ];

    public PurityContractTests()
        : base("MistPurityContract_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Globals
        {
            public static int Counter;
            public static readonly int Limit = 3;
        }

        type Reader = pure func<int>;
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public abstract Shape { pure int Area(); } public class Noisy : Shape { public int Area() { return Globals.Counter; } }", "Area")]
    [InlineData("public abstract Shape { pure int Area(); } public class Noisy : Shape { public readonly int Area() { return Globals.Counter; } }", "Area")]
    [InlineData("public abstract Sink { local void Add(int n); } public class Loud : Sink { public void Add(int n) { Globals.Counter = n; } }", "Add")]
    public void AnImplementationThatBreaksTheAbstractsPromise_IsRefusedNamingTheMember(string declarations, string member)
    {
        var finding = Assert.Single(Analyse("", declarations));

        Assert.Equal(ContractNotMet, finding.Id.Value);
        Assert.Contains(member, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public abstract Shape { pure int Area(); } public class Square : Shape { public int Area() { return 4; } }")]
    [InlineData("public abstract Shape { pure int Area(); } public class Square : Shape { public pure int Area() { return Globals.Limit; } }")]
    [InlineData("public abstract Sink { local void Add(int n); } public class Quiet : Sink { public int Count; public void Add(int n) { Count = Count + n; } }")]
    [InlineData("public abstract Sink { local void Add(int n); } public class Still : Sink { public pure void Add(int n) { } }")]
    public void AnImplementationThatKeepsTheAbstractsPromise_IsAdmittedWhateverItDeclares(string declarations)
    {
        Assert.Empty(Analyse("", declarations));
    }

    [Theory]
    [InlineData("func<int> plain = () => 1; object o = plain; if(o is Reader r) { }")]
    [InlineData("func<int> plain = () => 1; object o = plain; var r = o as Reader;")]
    public void ARuntimeTestAgainstAPureFunctionType_IsRefusedAsUntestable(string statement)
    {
        var finding = Assert.Single(Analyse(statement, ""));

        Assert.Equal(Untestable, finding.Id.Value);
        Assert.Contains("pure func<", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACastToAPureFunctionType_IsRefusedAsHavingNoConversion()
    {
        var finding = Assert.Single(Analyse("func<int> plain = () => 1; object o = plain; var r = (Reader)o;", ""));

        Assert.Equal(NoConversion, finding.Id.Value);
        Assert.Contains("pure func<", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeTestTheStaticTypeAlreadyAnswers_IsAdmitted()
    {
        Assert.Empty(Analyse("Reader p = () => 1; if(p is Reader q) { }", ""));
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes)
    {
        var model = BuildModel(statement, extraTypes, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && Ids.Contains(diagnostic.Id.Value))];
    }
}