// Feature: drop-semantics, Property 3: Drop-as-consume for owned types
//
// **Validates: Requirements 3.1, 3.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for drop-as-consume on owned (affine/linear) types.
///
/// Feature: drop-semantics, Property 3: Drop-as-consume for owned types
/// **Validates: Requirements 3.1, 3.2**
///
/// For any variable <c>v</c> of an affine or linear type, if <c>v.Drop()</c> is called at
/// source location L, then any access to <c>v</c> at a location dominated by L SHALL produce
/// a compile-time "use of dropped value" error. Linear variables report
/// <c>MIST_DROP_USE_AFTER_MOVE</c>; affine variables report <c>MIST_DROP_USE_AFTER_DROP</c>.
///
/// Each generated program declares an affine or linear type with an explicit <c>Drop()</c>
/// method and a non-drop accessor, then in <c>Main</c> creates a local of that type, calls
/// <c>v.Drop()</c>, and accesses <c>v</c> afterward. The discipline lives on the TYPE (the
/// class is declared <c>affine</c>/<c>linear</c>); the local is declared as
/// <c>FieldHandle f = new FieldHandle();</c> with no local ownership specifier.
///
/// Compilation runs entirely in-process via a shared <see cref="CompilerDriver"/> whose
/// analyzer pipeline includes <see cref="DropAnalyzer"/>; diagnostics are inspected directly.
/// Spawns 0 external processes.
/// </summary>
public sealed class DropAsConsumePropertyTests
{
    private const string UseAfterMoveId = "MIST_DROP_USE_AFTER_MOVE";
    private const string UseAfterDropId = "MIST_DROP_USE_AFTER_DROP";

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

    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_drop_consume_" + Guid.NewGuid().ToString("N"));
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
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            return SharedDriver.Compile(options).Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Scenario ─────────────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated use-after-drop scenario: an affine/linear type whose local is dropped then
    /// accessed in one of several ways. <see cref="ExpectedDiagnosticId"/> is the use-after id
    /// that must be present for the discipline (affine → drop, linear → move).
    /// </summary>
    public sealed record DropConsumeScenario(
        string Source,
        string Discipline,
        string AccessKind,
        string ExpectedDiagnosticId,
        string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // Owned disciplines under test and the use-after id each must report.
    private static readonly (string Keyword, string ExpectedId)[] OwnedDisciplines =
    [
        ("affine", UseAfterDropId),
        ("linear", UseAfterMoveId),
    ];

    // Ways to access the variable after Drop() — each references `f` after consumption.
    //   {0} is the variable name.
    private static readonly (string Name, string Template)[] AccessKinds =
    [
        ("call-accessor", "int x = {0}.GetX();"),
        ("read-in-expr", "int x = {0}.GetX() + 1;"),
        ("drop-again", "{0}.Drop();"),
        ("pass-to-writeline", "Console.WriteLine({0}.GetX());"),
    ];

    private static Gen<DropConsumeScenario> GenScenario()
    {
        return from discipline in Gen.Elements(OwnedDisciplines)
               from access in Gen.Elements(AccessKinds)
               select Build(discipline, access);
    }

    private static DropConsumeScenario Build(
        (string Keyword, string ExpectedId) discipline,
        (string Name, string Template) access)
    {
        var accessStmt = string.Format(System.Globalization.CultureInfo.InvariantCulture, access.Template, "f");

        // The discipline lives on the TYPE (FieldHandle is declared affine/linear).
        // The local uses no ownership specifier: `FieldHandle f = new FieldHandle();`.
        var source = $$"""
            using Standard;

            {{discipline.Keyword}} class FieldHandle
            {
                int Value;

                public FieldHandle()
                {
                    this.Value = 0;
                }

                public void Drop()
                {
                }

                public int GetX()
                {
                    return Value;
                }
            }
            class Program
            {
                public static void Main()
                {
                    FieldHandle f = new FieldHandle();
                    f.Drop();
                    {{accessStmt}}
                }
            }
            """;

        var label = $"discipline={discipline.Keyword}, access={access.Name} "
            + $"=> expect {discipline.ExpectedId}";

        return new DropConsumeScenario(
            source, discipline.Keyword, access.Name, discipline.ExpectedId, label);
    }

    public static Arbitrary<DropConsumeScenario> ArbitraryDropConsumeScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Drop-as-consume for owned types
    //
    // Calling Drop() on an affine/linear local consumes it; any later access is a
    // compile-time use-after-drop/move error matching the discipline's id.
    //
    // **Validates: Requirements 3.1, 3.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DropAsConsumePropertyTests) })]
    public Property DropConsumes_UseAfterDropIsRejected(DropConsumeScenario scenario)
    {
        var diagnostics = Compile(scenario.Source);

        var hasExpected = diagnostics.Any(d => d.Id.Value == scenario.ExpectedDiagnosticId);

        return hasExpected.ToProperty()
            .Label($"Expected a {scenario.ExpectedDiagnosticId} diagnostic for {scenario.Label}, "
                + $"but none was present.\nDiagnostics: "
                + $"{string.Join(", ", diagnostics.Select(d => d.Id.Value))}\n"
                + $"Source:\n{scenario.Source}");
    }

    // ─── Example-based sanity checks ──────────────────────────────────────

    [Theory]
    [InlineData("affine", UseAfterDropId)]
    [InlineData("linear", UseAfterMoveId)]
    public void DropConsumes_AccessAfterDrop_ReportsUseAfter(string discipline, string expectedId)
    {
        var source = $$"""
            using Standard;

            {{discipline}} class FieldHandle
            {
                int Value;

                public FieldHandle()
                {
                    this.Value = 0;
                }

                public void Drop()
                {
                }

                public int GetX()
                {
                    return Value;
                }
            }
            class Program
            {
                public static void Main()
                {
                    FieldHandle f = new FieldHandle();
                    f.Drop();
                    int x = f.GetX();
                }
            }
            """;

        var diagnostics = Compile(source);

        Assert.Contains(diagnostics, d => d.Id.Value == expectedId);
    }
}