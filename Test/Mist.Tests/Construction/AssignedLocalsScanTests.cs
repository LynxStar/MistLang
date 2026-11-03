using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction.Binding;

namespace Mist.Tests.Construction;

// The sound gate behind proof-sensitive arithmetic (exceptions Requirement 4): a local a callable
// writes anywhere carries no proof, so OperandFacts.AssignedNames must see the write WHATEVER control
// flow or nesting it sits in -- a later statement, a branch, a loop backedge, or an initializer the
// write hides inside. Over-approximation stays sound; a MISSED write is the unsoundness the fix closes.
[Trait("Tier", "2")]
public sealed class AssignedLocalsScanTests
{
    private static IReadOnlySet<string> Assigned(string body)
    {
        var source = $$"""
            namespace Probe;

            public class Probe
            {
                public static void Run()
                {
                    {{body}}
                }
            }
            """;

        var diagnostics = new DiagnosticBag();
        var tokens = new MistLexer().Lex(new SourceText("Probe.mist", source), diagnostics);
        var parsed = new MistParser().Parse(tokens, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(parsed.Root);

        return OperandFacts.AssignedNames(parsed.Root);
    }

    [Fact]
    public void ADeclarationWithoutReassignmentIsNotAnAssignment()
    {
        Assert.DoesNotContain("d", Assigned("int d = 1; int q = 20 / d;"));
    }

    [Fact]
    public void ATopLevelReassignmentIsSeen()
    {
        Assert.Contains("d", Assigned("int d = 1; d = 0; int q = 20 / d;"));
    }

    [Fact]
    public void AReassignmentInsideABranchIsSeen()
    {
        Assert.Contains("d", Assigned("int d = 1; if (d > 0) { d = 0; } int q = 20 / d;"));
    }

    [Fact]
    public void AReassignmentOnALoopBackedgeIsSeen()
    {
        Assert.Contains("d", Assigned("int d = 2; while (d >= 0) { int q = 20 / d; d = d - 1; }"));
    }

    [Fact]
    public void AWriteHiddenInAnInitializerIsSeen()
    {
        Assert.Contains("d", Assigned("int d = 1; var hidden = (d = 0); int q = 20 / d;"));
    }

    [Fact]
    public void OnlyTheWrittenLocalIsMarked()
    {
        var assigned = Assigned("int d = 1; int e = 2; e = 3; int q = d / e;");
        Assert.Contains("e", assigned);
        Assert.DoesNotContain("d", assigned);
    }
}