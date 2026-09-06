using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Extension members reached through <c>?.</c> on an optional receiver.
/// <para>
/// THE BUILDER ALWAYS RESOLVED THESE. It projects the optional to T and looks the member up through
/// the extension registry, then leaves validation to the type checker -- which re-resolved the same
/// member with a helper that reads declared member scopes only, and rejected every extension. The
/// tests below are written against the SHAPES that recheck could not see rather than against one
/// reproducer, because the blind spot was the helper, not any particular receiver.
/// </para>
/// </summary>
public sealed class OptionalChainingExtensionTests
{
    private static ImmutableArray<Diagnostic> Errors(string source)
    {
        var driver = new CompilerDriver(
            new MistLexer(),
            new MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_opt_ext_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            return driver.Compile(options).Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { }
        }
    }

    private const string BoxWithExtension = """
        using Standard;

        namespace TestProgram;

        public class Box {
            public int V;
            public Box(int v) { V = v; }
        }

        extension(Box self)
        {
            public int Doubled() { return self.V * 2; }
        }

        public class Program {
            public static void Main() {
        """;

    [Fact]
    public void AMistExtensionMethod_ResolvesThroughOptionalChaining()
    {
        Assert.Empty(Errors($$"""
            {{BoxWithExtension}}
                    Box? maybe = new Box(21);
                    Console.WriteLine(maybe?.Doubled());
            } }
            """));
    }

    [Fact]
    public void AMistExtensionMethod_StillResolvesOnAPlainReceiver()
    {
        Assert.Empty(Errors($$"""
            {{BoxWithExtension}}
                    Box plain = new Box(21);
                    Console.WriteLine(plain.Doubled());
            } }
            """));
    }

    /// <summary>
    /// The guard that keeps the relaxation from swallowing real errors. A member that exists nowhere
    /// -- not declared, not an extension -- must still be refused through <c>?.</c>.
    /// </summary>
    [Fact]
    public void AMemberThatExistsNowhere_IsStillRefusedThroughOptionalChaining()
    {
        Assert.NotEmpty(Errors($$"""
            {{BoxWithExtension}}
                    Box? maybe = new Box(21);
                    Console.WriteLine(maybe?.NoSuchMember());
            } }
            """));
    }
}