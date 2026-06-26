// Feature: abstract-type-unification, Phase 3 (Default Implementations)
// **Validates: Requirements 16.1, 16.2, 16.3, 16.4, 16.5**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Backend;

/// <summary>
/// Example-based tests verifying that the C# emitter lowers default method
/// implementations on abstract types into C# default interface methods (DIM),
/// and that concrete types receive forwarding stubs for the defaults they do
/// not override (Rust-style direct-call semantics).
///
/// All work runs in-process via the shared <see cref="CompilerDriver"/>; no
/// external process is spawned (EmitToDisk is false — the emitted C# is
/// inspected as text).
///
/// **Validates: Requirements 16.1, 16.2, 16.3, 16.4, 16.5**
/// </summary>
public sealed class AbstractDefaultMethodEmissionTests : IDisposable
{
    private readonly string outputDir;

    public AbstractDefaultMethodEmissionTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistDefaultMethodEmit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private string CompileToCSharp(string source)
    {
        // Append a trivial entry point so the type checker does not halt the
        // pipeline (and skip backend emission) with MIST_TC_0017.
        var withEntryPoint = source
            + "\n\npublic class __EntryPoint { public static void Main() { } }\n";

        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, withEntryPoint);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = SharedDriver.Compile(options);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error
                && d.Id.Value != "MIST_TC_0017")
            .ToList();
        Assert.True(errors.Count == 0,
            $"Compilation failed:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");

        Assert.NotNull(result.EmittedFiles);
        var userFile = result.EmittedFiles!.Values
            .FirstOrDefault(content => content.Contains("namespace TestProgram"));
        Assert.False(string.IsNullOrEmpty(userFile),
            "No emitted user C# file was found.");
        return userFile!;
    }

    /// <summary>Collapses runs of whitespace so emitted-shape assertions are robust to indentation.</summary>
    private static string Normalize(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ");

    // ═══════════════════════════════════════════════════════════════════════
    // 16.1 — Default method emits as a C# default interface method (DIM, body present)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DefaultMethod_EmitsAsDefaultInterfaceMethodWithBody()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            abstract IGreeting
            {
                string Name();
                string Greet() { return $"Hello, I am {this.Name()}"; }
            }
            """;

        var csharp = CompileToCSharp(source);
        var norm = Normalize(csharp);

        // The interface declares the slot as a signature-only method.
        Assert.Contains("interface IGreeting", norm);
        Assert.Contains("string Name();", norm);

        // The default method emits with a body inside the interface (a DIM).
        Assert.Contains("string Greet()", norm);
        Assert.Contains("return $\"Hello, I am {this.Name()}\"", norm);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 16.2 / 16.3 — Concrete type that does NOT override gets a forwarding stub
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConcreteTypeNotOverriding_GetsForwardingStub()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            abstract IGreeting
            {
                string Name();
                string Greet() { return $"Hello, I am {this.Name()}"; }
            }

            public class Person : IGreeting
            {
                public string Name() { return "Alice"; }
            }
            """;

        var csharp = CompileToCSharp(source);
        var norm = Normalize(csharp);

        // The concrete type receives a copy of the default method body so it is
        // callable directly on the concrete type (Rust-style semantics). A naive
        // ((IFace)this).Method() forwarding stub would cause infinite recursion
        // because the public method becomes the implicit interface implementation.
        Assert.Contains("public string Greet()", norm);
        Assert.Contains("return $\"Hello, I am {this.Name()}\"", norm);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 16.4 — Concrete type that overrides emits ONLY its own implementation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConcreteTypeOverriding_EmitsOnlyConcreteImplementation_NoForwardingStub()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            abstract IGreeting
            {
                string Name();
                string Greet() { return $"Hello, I am {this.Name()}"; }
            }

            public class Shouter : IGreeting
            {
                public string Name() { return "Bob"; }
                public string Greet() { return "HELLO"; }
            }
            """;

        var csharp = CompileToCSharp(source);
        var norm = Normalize(csharp);

        // The concrete override is present.
        Assert.Contains("return \"HELLO\"", norm);
        // No forwarding stub is emitted when the concrete type overrides the default.
        Assert.DoesNotContain("((IGreeting)this).Greet()", norm);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 16.2 — Defaults inherited transitively also receive forwarding stubs
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InheritedDefault_GetsForwardingStubOnConcreteType()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            abstract IBase
            {
                string Tag();
                string Banner() { return $"[{this.Tag()}]"; }
            }

            abstract IDerived : IBase
            {
                int Extra();
            }

            public class Widget : IDerived
            {
                public string Tag() { return "W"; }
                public int Extra() { return 1; }
            }
            """;

        var csharp = CompileToCSharp(source);
        var norm = Normalize(csharp);

        // The default lives on IBase as a DIM.
        Assert.Contains("string Banner()", norm);
        // Widget implements IDerived (: IBase transitively) and receives a copy of
        // the inherited default method body so the default is callable directly.
        Assert.Contains("public string Banner()", norm);
        Assert.Contains("return $\"[{this.Tag()}]\"", norm);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 16.2 — A default method taking parameters forwards its arguments
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DefaultMethodWithParameters_ForwardingStubPassesArguments()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            abstract IPrefixer
            {
                string Prefix();
                string Decorate(string text) { return $"{this.Prefix()}{text}"; }
            }

            public class Brackets : IPrefixer
            {
                public string Prefix() { return "<"; }
            }
            """;

        var csharp = CompileToCSharp(source);
        var norm = Normalize(csharp);

        // The forwarding stub duplicates the default body on the concrete type,
        // including the parameter, so it's callable directly without interface cast.
        Assert.Contains("public string Decorate(string text)", norm);
        Assert.Contains("return $\"{this.Prefix()}{text}\"", norm);
    }
}