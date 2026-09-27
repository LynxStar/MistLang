// Tier 3 (Component): the REAL Standard/Experimental/List.mist carries the promises purity Requirement 9.3
// names -- a pure constructor, local readonly readers, local mutators -- so the spec's own example programs
// are built over the real Standard sources and judged by the rules: a pure scope owning a list it built, a
// pure scope reading a frozen list, a constructorless type holding a list, and what they refuse. Each refusal
// is also a PurityRefusalTests row over a hand-built list; this class proves the declaration, not the rule.
// The samples in AllFeaturesV2 that use List<T> carry the runtime claim.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Integration;

[Trait("Tier", "3")]
public sealed class StandardListPurityTests
{
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";

    private const string Admitted = """
        public class Holder
        {
            public List<int> Items = new List<int>();
        }

        public class Listed
        {
            public static pure int Seeded()
            {
                owned var l = new List<int>();
                l.Add(1);
                return l.Count;
            }

            public static pure int Size(frozen List<int> xs)
            {
                return xs.Count;
            }
        }
        """;

    private const string ReadsAnUnrestrictedList = """
        public class Listed
        {
            public static pure int Bad(List<int> xs)
            {
                return xs.Count;
            }
        }
        """;

    private const string WritesAFrozenList = """
        public class Listed
        {
            public static pure int Poke(frozen List<int> xs)
            {
                xs[0] = 1;
                return 0;
            }
        }
        """;

    [Fact]
    public void APureScopeOwningOrFreezingAList_AndAConstructorlessTypeHoldingOne_IsAdmitted()
    {
        Assert.Empty(Errors(
            "owned var xs = new List<int>(); xs.Add(1); xs.Add(2); var seeded = Listed.Seeded(); var size = Listed.Size(xs); var holder = new Holder(); holder.Items.Add(3); var held = holder.Items.Count;",
            Admitted));
    }

    [Fact]
    public void APureScopeReadingAnUnrestrictedListsCount_IsRefusedForWantingAnOwnedOrFrozenReceiver()
    {
        var error = Assert.Single(Errors("owned var xs = new List<int>(); var read = Listed.Bad(xs);", ReadsAnUnrestrictedList));

        Assert.Equal(CallsImpure, error.Id.Value);
        Assert.Contains("Count", error.Message, StringComparison.Ordinal);
        Assert.Contains("local readonly", error.Message, StringComparison.Ordinal);
        Assert.Contains("owned or frozen receiver", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APureScopeWritingAnElementOfAFrozenList_IsRefused()
    {
        var error = Assert.Single(Errors("owned var xs = new List<int>(); xs.Add(0); var poked = Listed.Poke(xs);", WritesAFrozenList));

        Assert.Equal(Writes, error.Id.Value);
        Assert.Contains("frozen", error.Message, StringComparison.Ordinal);
    }

    private static List<Diagnostic> Errors(string statements, string types)
    {
        var model = StandardModelProbe.Build(statements, out var binding, types);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. binding.Concat(findings.ToImmutableArray()).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }
}