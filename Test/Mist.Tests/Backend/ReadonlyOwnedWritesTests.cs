// Tier 3 (Component): purity Requirement 11, the fifteenth readonly-contexts rule -- a readonly context
// may write a field, an index or a setter of what it OWNS and may call a local member on it, while a
// write through an unrestricted parameter and a call to an unmarked member stay refused. The ids are
// the readonly analyzer's own, since the amendment changes what it admits, not what it says.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ReadonlyOwnedWritesTests : ProgramModelProbe
{
    private const string FieldAssigned = "MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER";
    private const string CallsMutable = "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE";

    private static readonly string[] Ids =
    [
        FieldAssigned, CallsMutable, "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY", "MIST_PA_READONLY_CONTEXT_ALLOCATES_MUTABLE",
        "MIST_PA_FIELD_ASSIGNED_IN_READONLY_BLOCK", "MIST_PA_READONLY_FIELD_ASSIGNED", "MIST_PA_READONLY_PARAMETER_CALLS_MUTABLE",
        "MIST_PA_READONLY_PARAMETER_SETS_PROPERTY", "MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_INDEX",
    ];

    public ReadonlyOwnedWritesTests()
        : base("MistReadonlyOwnedWrites_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Bag
        {
            public int Count;

            public pure Bag() { }

            public local void Add(int x) { Count = Count + 1; }

            public local readonly int Size() { return Count; }

            public readonly int Peek() { return Count + Globals.Counter; }

            public void Poke() { Count = 0; Globals.Counter = 1; }
        }

        public class Globals
        {
            public static int Counter;
        }

        public class Cell
        {
            public int Level { get; set; }
        }

        public class Grid
        {
            public int cell;

            public int this[int i]
            {
                get { return cell; }
                set { cell = value; }
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public readonly int Build() { owned var b = new Bag(); b.Add(1); b.Count = 5; return b.Count; }")]
    [InlineData("public readonly int Build() { var b = new Bag(); b.Add(1); b.Count = 5; return b.Count; }")]
    [InlineData("public readonly int Build() { var b = new Bag(); return b.Size(); }")]
    [InlineData("public readonly int Build() { var c = new Cell(); c.Level = 2; return c.Level; }")]
    [InlineData("public readonly int Build() { var g = new Grid(); g[0] = 1; return g[0]; }")]
    [InlineData("public readonly int Build() { int[] xs = [0, 0]; xs[0] = 1; return xs[0]; }")]
    [InlineData("public readonly int Build(owned Bag b) { b.Add(1); b.Count = 5; return b.Count; }")]
    public void AReadonlyMember_MayWriteAndLocallyCallWhatItOwns(string member)
    {
        Assert.Empty(Analyse(member));
    }

    [Theory]
    [InlineData("public readonly int Poke(Bag x) { x.Count = 1; return 1; }", FieldAssigned)]
    [InlineData("public readonly int Poke(frozen Bag x) { x.Count = 1; return 1; }", FieldAssigned)]
    [InlineData("public readonly int Poke() { var b = new Bag(); b.Poke(); return 1; }", CallsMutable)]
    [InlineData("public readonly int Poke(Bag x) { x.Add(1); return 1; }", CallsMutable)]
    [InlineData("public readonly int Poke(Bag x) { x.Poke(); return 1; }", CallsMutable)]
    public void AReadonlyMember_StillMayNotWriteWhatItDoesNotOwnOrCallAnUnmarkedMember(string member, string expected)
    {
        var finding = Assert.Single(Analyse(member));

        Assert.Equal(expected, finding.Id.Value);
    }

    private List<Diagnostic> Analyse(string member)
    {
        var model = BuildModel("", $"public class Host {{ public int n; {member} }}", "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && Ids.Contains(diagnostic.Id.Value))];
    }
}