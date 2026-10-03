// Tier 4: a binding failure remains one diagnostic when the resulting model reaches every analysis rule.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
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
    [InlineData("unresolved-identifier", "Console.WriteLine(neverDeclared);", "MIST_PM_UNRESOLVED_IDENTIFIER")]
    [InlineData("refused-member-access-as-an-argument", "int k = 5; Console.WriteLine(k.Lenght);", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("local-from-a-refused-member-access-passed-on", "int k = 5; var n = k.Lenght; Console.WriteLine(n);", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("unresolved-receiver-called-as-argument", "Console.WriteLine(nothing.Describe());", "MIST_PM_UNRESOLVED_IDENTIFIER")]
    [InlineData("local-from-an-ambiguous-call-passed-on", "byte small = 2; var r = Sink.Take(small); Console.WriteLine(r);", "MIST_PM_AMBIGUOUS_OVERLOAD")]
    [InlineData("local-from-an-ambiguous-index-passed-on", "var table = new Table(); byte small = 2; var r = table[small]; Console.WriteLine(r);", "MIST_PM_AMBIGUOUS_OVERLOAD")]
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
        Assert.True(diagnostics is [var only] && only.Id.Value == expected,
            $"{caseName}: expected only {expected}; got {string.Join(", ", diagnostics.Select(diagnostic => diagnostic.Id.Value))}");
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
        Assert.True(diagnostics is [{ Id.Value: "MIST_PM_UNRESOLVED_TYPE" }],
            $"expected only MIST_PM_UNRESOLVED_TYPE; got {string.Join(", ", diagnostics.Select(diagnostic => diagnostic.Id.Value))}");
    }
}