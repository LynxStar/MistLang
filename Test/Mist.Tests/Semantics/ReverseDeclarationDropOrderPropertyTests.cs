// Feature: drop-semantics, Property 10: Reverse declaration order

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property 10: Reverse Declaration Order for Multiple Drops.
///
/// **Validates: Requirements 4.5, 13.1**
///
/// For any scope containing N variables with drop obligations (N ≥ 2), the compiler SHALL
/// insert <c>Drop()</c> calls in reverse declaration order — the last declared variable is
/// dropped first.
///
/// Each scenario declares N linear locals (a <c>linear class</c> with a <c>Drop()</c>) in a
/// single scope, all reaching scope exit live (no move/return), then compiles fully
/// in-process via a shared <see cref="CompilerDriver"/> with <c>EmitToDisk: false</c> and
/// inspects the emitted C# text. The positions of each <c>&lt;local&gt;.Drop()</c> call must
/// appear in strictly reverse declaration order.
///
/// Compilation runs entirely in-process and spawns 0 external processes.
/// </summary>
public sealed class ReverseDeclarationDropOrderPropertyTests
{
    // ─── Shared driver (DropAnalyzer + emitter included) ─────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── In-process compile helper ───────────────────────────────────────

    private static CompilerResult CompileSource(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_revdrop_pbt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
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
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            return SharedDriver.Compile(options);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    // ─── Source builder ──────────────────────────────────────────────────

    // A linear class with a void Drop(). Declaring N locals of this type in a single
    // scope, all live at scope exit, forces the emitter to auto-insert N scope-exit
    // Drop() calls (Requirements 4.1, 4.5, 13.1).
    private const string LinearResourceType = """
        linear class Res
        {
            int Id;

            public Res(int id)
            {
                this.Id = id;
            }

            public void Drop()
            {
            }

            public int GetId()
            {
                return Id;
            }
        }
        """;

    // Local names in declaration order. Distinct, recognizable identifiers so we can
    // locate "<name>.Drop()" unambiguously in the emitted C#.
    private static readonly string[] LocalNames =
    {
        "res0", "res1", "res2", "res3", "res4",
        "res5", "res6", "res7", "res8", "res9",
    };

    /// <summary>
    /// Builds a program declaring <paramref name="n"/> linear locals in Main, all kept live
    /// at scope exit (each is read via GetFd-style accessor, never moved or returned), so the
    /// emitter inserts a scope-exit Drop() for every one of them.
    /// </summary>
    private static string BuildProgram(int n)
    {
        var decls = new System.Text.StringBuilder();
        for(var i = 0; i < n; i++)
        {
            var name = LocalNames[i];
            decls.AppendLine($"        Res {name} = new Res({i});");
        }

        // Keep every local live at scope exit: use each one in a no-op read so it is not
        // dead-code eliminated and is not moved/returned.
        for(var i = 0; i < n; i++)
        {
            var name = LocalNames[i];
            decls.AppendLine($"        int use{i} = {name}.GetId();");
        }

        return $$"""
            {{LinearResourceType}}
            class Program
            {
                public static void Main()
                {
            {{decls}}    }
            }
            """;
    }

    // ─── Order extraction ────────────────────────────────────────────────

    /// <summary>
    /// Returns the declaration-order indices of the locals as they appear (by their
    /// <c>&lt;name&gt;.Drop()</c> call) in the emitted C#, ordered by emission position.
    /// </summary>
    private static List<int> GetDropEmissionOrder(string emitted, int n)
    {
        var positions = new List<(int Position, int DeclIndex)>();
        for(var i = 0; i < n; i++)
        {
            var marker = $"{LocalNames[i]}.Drop()";
            var pos = emitted.IndexOf(marker, StringComparison.Ordinal);
            if(pos >= 0)
            {
                positions.Add((pos, i));
            }
        }

        return positions
            .OrderBy(p => p.Position)
            .Select(p => p.DeclIndex)
            .ToList();
    }

    // ─── Generator: N in [2, 10] ─────────────────────────────────────────

    public static Arbitrary<int> ArbitraryDropCount() =>
        Gen.Choose(2, 10).ToArbitrary();

    // ─── Property 10 ─────────────────────────────────────────────────────

    /// <summary>
    /// For N linear locals (N in [2, 10]) live at scope exit, the emitted Drop() calls
    /// appear in strictly reverse declaration order: the last declared local drops first.
    ///
    /// **Validates: Requirements 4.5, 13.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ReverseDeclarationDropOrderPropertyTests) }, MaxTest = 100)]
    public Property DropsEmittedInReverseDeclarationOrder(int n)
    {
        var source = BuildProgram(n);
        var result = CompileSource(source);

        // Compilation must succeed (ignore the entry-point diagnostic which is unrelated).
        var errors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        if(errors.Count > 0)
        {
            return false.ToProperty().Label(
                $"Unexpected compile errors for N={n}: {string.Join("; ", errors.Select(e => e.Id.Value + ": " + e.Message))}\nSource:\n{source}");
        }

        var emitted = GetEmittedCSharp(result);
        if(emitted is null)
        {
            return false.ToProperty().Label($"No emitted C# produced for N={n}.\nSource:\n{source}");
        }

        var order = GetDropEmissionOrder(emitted, n);

        // Every declared local must have an emitted Drop() call.
        if(order.Count != n)
        {
            return false.ToProperty().Label(
                $"Expected {n} Drop() calls, found {order.Count}. Emission order: [{string.Join(", ", order)}].\nEmitted:\n{emitted}");
        }

        // Reverse declaration order: indices must be n-1, n-2, ..., 1, 0.
        var expected = Enumerable.Range(0, n).Reverse().ToList();
        var ok = order.SequenceEqual(expected);

        return ok.ToProperty().Label(
            $"Drop order mismatch for N={n}. Expected [{string.Join(", ", expected)}], got [{string.Join(", ", order)}].\nEmitted:\n{emitted}");
    }

    // ─── Explicit boundary examples ──────────────────────────────────────

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void Drops_ReverseDeclarationOrder_Examples(int n)
    {
        var source = BuildProgram(n);
        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected compile errors: {string.Join("; ", errors.Select(e => e.Id.Value + ": " + e.Message))}");

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);

        var order = GetDropEmissionOrder(emitted!, n);
        var expected = Enumerable.Range(0, n).Reverse().ToList();
        Assert.Equal(expected, order);
    }
}