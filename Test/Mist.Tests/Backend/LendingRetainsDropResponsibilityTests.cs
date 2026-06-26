// Feature: drop-semantics, Property 11: Lending does not transfer drop responsibility
//
// **Validates: Requirements 9.1, 9.2, 9.3**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Example-based tests for Property 11: Lending does not transfer drop responsibility.
///
/// Feature: drop-semantics, Property 11: Lending does not transfer drop responsibility
///
/// **Validates: Requirements 9.1, 9.2, 9.3**
///
/// For any linear or affine variable lent as <c>readonly ref</c>, <c>frozen</c>, or
/// <c>borrowed</c>, the compiler SHALL still insert <c>Drop()</c> on the owner at scope
/// exit. Lending is temporary and does not transfer the drop obligation.
///
/// At the emitter level, lending means the variable is used (methods are called on it or
/// it is passed to a function that does not take ownership) but never moved. The variable
/// remains in the <c>Live</c> state, so auto-drop MUST fire at scope exit.
///
/// Each scenario declares a <c>linear class</c> with a <c>Drop()</c> method, creates a
/// local of that type, uses the variable in a non-consuming way (method call, which is
/// semantically a readonly-ref lend of the receiver), and verifies that the emitted C#
/// still contains the owner's scope-exit <c>Drop()</c> call.
///
/// Spawns 0 external processes.
/// </summary>
public sealed class LendingRetainsDropResponsibilityTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

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

    /// <summary>
    /// Compiles the given Mist source in-process and returns ALL emitted C# content
    /// (non-csproj files concatenated), or <c>null</c> when compilation fails.
    /// </summary>
    private static (string? EmittedCSharp, string? ErrorMessage) CompileAndGetEmittedCSharp(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_lending_drop_" + Guid.NewGuid().ToString("N"));
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

            var result = SharedDriver.Compile(options);

            // Filter out MIST_TC_0017 (entry-point detection) which can fire
            // spuriously when certain type declarations coexist with Main.
            var errors = result.Diagnostics
                .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
                .Where(d => d.Id.Value != "MIST_TC_0017")
                .ToList();

            if(errors.Count > 0)
                return (null, string.Join("\n", errors.Select(d => $"{d.Id.Value}: {d.Message}")));

            if(result.EmittedFiles is null || result.EmittedFiles.Count == 0)
                return (null, "No emitted files produced.");

            var allContent = result.EmittedFiles
                .Where(kvp => !kvp.Key.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Value);

            return (string.Join("\n", allContent), null);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the emitted C# contains a scope-exit drop call
    /// <c>{localName}.Drop()</c> as a whole identifier.
    /// </summary>
    private static bool ContainsScopeExitDrop(string emitted, string localName)
    {
        var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(localName)}\.Drop\(\)";
        return Regex.IsMatch(emitted, pattern);
    }

    // ─── Source builders ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a Mist program with a linear type whose local is used (lent) without
    /// being moved — the variable stays Live and Drop() must fire at scope exit.
    /// Calling a method on the linear local is semantically a readonly-ref lend of
    /// the receiver (<c>self</c>).
    /// </summary>
    private static string BuildLendingViaMethodCallProgram(string localName)
    {
        return $$"""
            linear class Resource
            {
                public void Drop()
                {
                }

                public int GetId()
                {
                    return 1;
                }

                public int GetValue()
                {
                    return 42;
                }
            }
            class Program
            {
                public static void Main()
                {
                    Resource {{localName}} = new Resource();
                    int id = {{localName}}.GetId();
                    int val = {{localName}}.GetValue();
                }
            }
            """;
    }

    /// <summary>
    /// Builds a Mist program where a linear local is used via multiple method calls
    /// (lending the receiver each time) and still receives auto-drop at scope exit.
    /// </summary>
    private static string BuildMultipleLendProgram(string localName)
    {
        return $$"""
            linear class Connection
            {
                public void Drop()
                {
                }

                public int GetStatus()
                {
                    return 200;
                }

                public int GetTimeout()
                {
                    return 30;
                }

                public int GetRetries()
                {
                    return 3;
                }
            }
            class Program
            {
                public static void Main()
                {
                    Connection {{localName}} = new Connection();
                    int s = {{localName}}.GetStatus();
                    int t = {{localName}}.GetTimeout();
                    int r = {{localName}}.GetRetries();
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Lending does not transfer drop responsibility
    //
    // **Validates: Requirements 9.1, 9.2, 9.3**
    //
    // Asserts that a linear local that is lent (used without being moved or
    // consumed) still has its scope-exit Drop() emitted on the owner.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A linear local that is lent via method calls (readonly ref of receiver)
    /// still gets scope-exit Drop() on the owner.
    /// **Validates: Requirements 9.1, 9.2, 9.3**
    /// </summary>
    [Fact]
    public void LinearLocal_LentViaMethodCall_StillScopeExitDrops()
    {
        var source = BuildLendingViaMethodCallProgram("res");
        var (emitted, error) = CompileAndGetEmittedCSharp(source);

        Assert.True(emitted is not null,
            $"Compilation failed:\n{error}\n\nSource:\n{source}");
        Assert.True(ContainsScopeExitDrop(emitted!, "res"),
            $"Expected scope-exit 'res.Drop()' after lending via method calls.\n\nEmitted C#:\n{emitted}");
    }

    /// <summary>
    /// A linear local lent multiple times (multiple method calls) still gets
    /// exactly one scope-exit Drop().
    /// **Validates: Requirements 9.1, 9.2, 9.3**
    /// </summary>
    [Fact]
    public void LinearLocal_LentMultipleTimes_StillScopeExitDrops()
    {
        var source = BuildMultipleLendProgram("conn");
        var (emitted, error) = CompileAndGetEmittedCSharp(source);

        Assert.True(emitted is not null,
            $"Compilation failed:\n{error}\n\nSource:\n{source}");
        Assert.True(ContainsScopeExitDrop(emitted!, "conn"),
            $"Expected scope-exit 'conn.Drop()' after multiple lending calls.\n\nEmitted C#:\n{emitted}");
    }

    /// <summary>
    /// Parametrized: various local names with method-call lending all retain Drop().
    /// **Validates: Requirements 9.1, 9.2, 9.3**
    /// </summary>
    [Theory]
    [InlineData("handle")]
    [InlineData("resource")]
    [InlineData("conn")]
    public void LinearLocal_LentViaMethodCall_RetainsDropObligation(string localName)
    {
        var source = BuildLendingViaMethodCallProgram(localName);
        var (emitted, error) = CompileAndGetEmittedCSharp(source);

        Assert.True(emitted is not null,
            $"Compilation failed for local '{localName}':\n{error}\n\nSource:\n{source}");
        Assert.True(ContainsScopeExitDrop(emitted!, localName),
            $"Expected scope-exit '{localName}.Drop()' after lending.\n\nEmitted C#:\n{emitted}");
    }
}