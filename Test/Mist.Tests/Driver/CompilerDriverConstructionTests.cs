using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

[Trait("Tier", "4")]
public sealed class CompilerDriverConstructionTests
{
    [Fact]
    public void CleanDriver_RejectsABackendOtherThanCSharpBeforeLoading()
    {
        var reporter = new RecordingReporter();
        var driver = new CompilerDriver(new UnusedLexer(), new UnusedParser(), reporter, ambientTypes: _ => StubAmbientTypeSource.Instance);

        var result = driver.Compile(Options(new BackendId("llvm")));

        Assert.False(result.Succeeded);
        Assert.Equal(DriverDiagnostics.UnknownBackend.Id, Assert.Single(result.Diagnostics).Id);
        Assert.Empty(reporter.Stages);
    }

    private static CompilerOptions Options(BackendId backend)
    {
        return new CompilerOptions(
            ProjectFolder: "unread",
            SourcePaths: ["unread/Program.mist"],
            Backend: backend,
            OutputDirectory: "unread/out",
            BinDirectory: "unread/bin",
            ProjectName: "Unread",
            NamespaceRoot: "Unread");
    }

    private sealed class UnusedLexer : ILexer
    {
        public TokenStream Lex(SourceText source, DiagnosticBag diagnostics)
        {
            throw new InvalidOperationException("The lexer ran.");
        }
    }

    private sealed class UnusedParser : IParser
    {
        public ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics)
        {
            throw new InvalidOperationException("The parser ran.");
        }
    }

    private sealed class RecordingReporter : ICompilationProgressReporter
    {
        public List<string> Stages { get; } = [];

        public void ReportStageStart(string stageId, string title)
        {
            Stages.Add(stageId);
        }

        public void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details)
        {
            Stages.Add(stageId);
        }

        public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed)
        {
            Stages.Add("summary");
        }
    }
}