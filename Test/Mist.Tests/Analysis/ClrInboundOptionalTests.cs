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
/// The CLR null boundary: a reference-typed result handed to Mist by the CLR is <c>T | void</c>,
/// and a reference-typed argument sent to the CLR is not.
/// <para>
/// THE ASYMMETRY IS THE POINT, and it is not an accident of what was easy to implement. Mist has no
/// null, so an outbound reference is non-null by construction and a union there would only give
/// overload resolution more candidates to be ambiguous between. Inbound is the only direction null
/// can arrive from, and NRT annotations are advisory -- a lying annotation is still a naked null --
/// so they are ignored and every reference result is wrapped.
/// </para>
/// </summary>
public sealed class ClrInboundOptionalTests
{
    private static ImmutableArray<Diagnostic> CompileSource(string source)
    {
        var driver = new CompilerDriver(
            new MistLexer(),
            new MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_clr_inbound_{Guid.NewGuid():N}");
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

            return driver.Compile(options).Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { }
        }
    }

    private static ImmutableArray<Diagnostic> Errors(string body)
    {
        var source = $$"""
            using Standard;
            using System.Collections.Generic;

            namespace TestProgram;

            class Program {
                public static void Main() {
            {{body}}
                }
            }
            """;

        return CompileSource(source)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    [Fact]
    public void AClrReferenceReturn_CannotBeReadWithAPlainDot()
    {
        var errors = Errors("""
                    var list = new List<int>();
                    var found = list.FindAll((int x) => x > 0);
                    Console.WriteLine(found.Count);
            """);

        Assert.Contains(errors, error => error.Message.Contains("List<Int32> | Void", StringComparison.Ordinal));
    }

    [Fact]
    public void AClrReferenceReturn_IsReachableThroughOptionalChaining()
    {
        Assert.Empty(Errors("""
                    var list = new List<int>();
                    var found = list.FindAll((int x) => x > 0);
                    Console.WriteLine(found?.Count);
            """));
    }

    [Fact]
    public void AClrValueTypeReturn_IsNotWrapped()
    {
        Assert.Empty(Errors("""
                    var list = new List<int>();
                    list.Add(1);
                    Console.WriteLine(list.Count);
            """));
    }

    /// <summary>
    /// The outbound half. Add takes a reference-typed argument in the general case; here the value
    /// is a plain Mist String, and no narrowing is asked of it.
    /// </summary>
    [Fact]
    public void AReferenceArgumentSentToTheClr_NeedsNoNarrowing()
    {
        Assert.Empty(Errors("""
                    var names = new List<string>();
                    names.Add("mist");
                    Console.WriteLine(names.Count);
            """));
    }
}