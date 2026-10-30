// Tier 4: a binding failure remains one diagnostic when the resulting model reaches every analysis rule.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Integration;

[Trait("Tier", "4")]
public sealed class BindingToAnalysisBoundaryTests : ProgramModelProbe
{
    public BindingToAnalysisBoundaryTests() : base("mist-binding-analysis-seam-")
    {
    }

    protected override string Fixtures => "";

    protected override string Preamble => "";

    [Theory]
    [InlineData("unresolved-identifier", "Console.WriteLine(neverDeclared);", "MIST0296")]
    [InlineData("refused-member-access-as-an-argument", "int k = 5; Console.WriteLine(k.Lenght);", "MIST0301")]
    [InlineData("local-from-a-refused-member-access-passed-on", "int k = 5; var n = k.Lenght; Console.WriteLine(n);", "MIST0301")]
    [InlineData("unresolved-receiver-called-as-argument", "Console.WriteLine(nothing.Describe());", "MIST0296")]
    [InlineData("local-from-an-ambiguous-call-passed-on", "byte small = 2; var r = Sink.Take(small); Console.WriteLine(r);", "MIST0386")]
    [InlineData("local-from-an-ambiguous-index-passed-on", "var table = new Table(); byte small = 2; var r = table[small]; Console.WriteLine(r);", "MIST0386")]
    public void BindingFailureDoesNotCascade(string caseName, string statements, string expected)
    {
        var source = $$"""
            namespace Probe;
            public class Console
            {
                public static void WriteLine(unknown value) { }
            }
            public class Sink
            {
                public static string Take(uint value) { return "uint"; }
                public static string Take(short value) { return "short"; }
            }
            public class Table
            {
                public string this[uint value] { get { return "uint"; } }
                public string this[short value] { get { return "short"; } }
            }
            public class Probe
            {
                public static void Run() { {{statements}} }
            }
            """;
        var model = Build(source, out var binding);
        var bag = new DiagnosticBag();
        foreach(var finding in binding)
        {
            bag.Add(finding);
        }

        ProgramAnalysisRunner.Run(model, bag);
        var diagnostics = bag.ToImmutableArray().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).ToArray();
        Assert.True(diagnostics is [var only] && only.Number.Value == expected,
            $"{caseName}: expected only {expected}; got {string.Join(", ", diagnostics.Select(diagnostic => diagnostic.Number.Value))}");
    }

    [Fact]
    public void AnalysisOwnedInputsSurviveInlineLocalAndAwaitBinding()
    {
        const string source = """
            namespace Probe;
            public class Console { public static void WriteLine(unknown value) {} }
            public class Box { public string Describe() { return "box"; } }
            public class Probe {
                public static void InlineOperator(Box p) { Console.WriteLine(p + 1); }
                public static void LocalOperator(Box p) { var n = p + 1; Console.WriteLine(n); }
                public static void InlineMember(unknown u) { Console.WriteLine(u.Length); }
                public static void LocalMember(unknown u) { var n = u.Length; Console.WriteLine(n); }
                public static void AwaitMember(unknown u) { var r = await u.Length; }
                public static void AwaitOperator(Box p) { var r = await (p + 1); }
                public static void InlineConditional(Box p) { Console.WriteLine(p?.Describe()); }
                public static void LocalConditional(Box p) { var r = p?.Describe(); Console.WriteLine(r); }
            }
            """;
        var model = Build(source, out var binding);
        Assert.Empty(binding);
        var bag = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, bag);
        var diagnostics = bag.ToImmutableArray().Where(finding => finding.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .OrderBy(finding => finding.Position().Start.Line).ToArray();
        string[] expected = [
            "MIST0200", "MIST0200",
            "MIST0202", "MIST0202",
            "MIST0202", "MIST0200",
            "MIST0205", "MIST0205",
        ];
        Assert.Equal(expected, diagnostics.Select(finding => finding.Number.Value));
        var owners = SourceBodies.Of(model).SelectMany(root => BodyWalker.DescendantsAndSelf(root.Root))
            .OfType<Expression>().Where(expression => expression is BinaryExpression
                or MemberAccessExpression { IsConditional: true }
                or MemberAccessExpression { Receiver.Type: ProgramModel.Model.Types.UnknownType })
            .OrderBy(expression => DiagnosticHelper.ResolveLocation(expression.Node).Location.Position().Start.Line).ToArray();
        Assert.Equal(expected.Length, owners.Length);
        for(var i = 0; i < diagnostics.Length; i++)
        {
            Assert.Equal(i + 5, diagnostics[i].Position().Start.Line);
            Assert.Equal(DiagnosticHelper.ResolveLocation(owners[i].Node).Location, diagnostics[i].Location);
            Assert.EndsWith("Program.mist", diagnostics[i].Location.Path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnUnresolvedBaseDoesNotCascadeIntoTheSlotsItMightFill()
    {
        var model = Build("""
            namespace Probe;
            public abstract IGet { int Get(); }
            public class Thing : Missing, IGet { }
            """, out var binding);
        var bag = new DiagnosticBag();
        foreach(var finding in binding)
        {
            bag.Add(finding);
        }

        ProgramAnalysisRunner.Run(model, bag);
        var diagnostics = bag.ToImmutableArray().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).ToArray();
        Assert.True(diagnostics is [{ Number.Value: "MIST0298" }],
            $"expected only MIST0298; got {string.Join(", ", diagnostics.Select(diagnostic => diagnostic.Number.Value))}");
    }
}