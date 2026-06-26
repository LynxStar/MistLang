// Task 15.3 — Unit tests for orchestrator integration.
//
// End-to-end integration tests that exercise the full OwnershipAnalyzer pipeline
// with all analyzers enabled (TypeCheckerAnalyzer, ReadonlyAnalyzer, OwnershipAnalyzer).
// Each test feeds a complete Mist program through in-process CompilerDriver.Compile()
// and asserts the expected diagnostic outcome:
//
//   - Owned type with valid fields compiles cleanly (Req 4.4, 5.1)
//   - Owned type with violating field → MIST_OWN_FIELD_TYPE_VIOLATION (Req 4.4, 5.1)
//   - Method call through shared binding where method requires borrowed
//     → MIST_OWN_THIS_TIER_MISMATCH (Req 6.3, 17.16)
//   - Owned binding, move field out, access sibling → succeeds (Req 16.2)
//   - var x = new T(); x.MutateTier(); → inference gives Owned, call succeeds (Req 18.1)
//
// All compilation is in-process via a single shared CompilerDriver.
// 0 external process spawns (test-process-spawning policy).
//
// Validates: Requirements 4.4, 5.1, 6.3, 16.2, 17.16, 18.1

using System.Collections.Immutable;
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
/// End-to-end integration tests for the rewritten OwnershipAnalyzer orchestrator
/// (Task 15.3). Each test compiles a complete Mist program through the full pipeline
/// and asserts the expected diagnostic outcome.
///
/// <b>Validates: Requirements 4.4, 5.1, 6.3, 16.2, 17.16, 18.1</b>
/// </summary>
public sealed class OrchestratorIntegrationTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_orch_int_" + Guid.NewGuid().ToString("N"));
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

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Diagnostic? FindById(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.FirstOrDefault(d => d.Id.Value == id);
    }

    private static bool HasErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. End-to-end: owned type with valid fields compiles cleanly
    //    (Req 4.4, Req 5.1)
    //
    // An owned type whose fields are all either owned types, IClonable, or
    // value types passes the FieldTypeRestrictionVerifier and compiles with
    // no errors.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedType_WithValidFields_CompilesCleanly()
    {
        const string source = """
            owned class Vault
            {
                public owned string Secret;
                public int AccessCount;

                public Vault(owned string secret)
                {
                    this.Secret = secret;
                    this.AccessCount = 0;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var v = new Vault("top-secret");
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No field-type violation or any other ownership error expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_FIELD_TYPE_VIOLATION"));
        Assert.False(HasErrors(diagnostics),
            $"Expected clean compilation but got errors: {string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. End-to-end: owned type with violating field
    //    → MIST_OWN_FIELD_TYPE_VIOLATION (Req 4.4, Req 5.1)
    //
    // An owned type with a field whose type is a non-owned reference type
    // that does NOT implement IClonable triggers the field-type restriction.
    // Gadget carries an array field which blocks IClonable auto-derivation.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedType_WithViolatingField_EmitsFieldTypeViolation()
    {
        const string source = """
            class Gadget
            {
                public int[] Data;
            }

            owned class Container
            {
                public Gadget Item;
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Gadget is a non-owned reference type that cannot auto-derive IClonable
        // (has an array field). The field-type restriction fires.
        var fieldTypeViolation = FindById(diagnostics, "MIST_OWN_FIELD_TYPE_VIOLATION");
        Assert.NotNull(fieldTypeViolation);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. End-to-end: method call through shared binding where method
    //    requires `borrowed` → MIST_OWN_THIS_TIER_MISMATCH (Req 6.3, 17.16)
    //
    // A shared (unrestricted) binding calling a method that writes to an
    // owned field triggers the this-tier call-site check. The method's
    // inferred this-tier is Borrowed, but the shared binding satisfies only
    // None — so the call is rejected.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedBinding_CallsMethodRequiringBorrowed_EmitsThisTierMismatch()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }

                public void Mutate(owned string newData)
                {
                    this.Data = newData;
                }
            }

            class Program
            {
                public static void Use(Container c)
                {
                    c.Mutate("updated");
                }

                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // The shared parameter 'c' cannot call Mutate() which requires at least
        // Borrowed tier on 'this' (it writes to owned field Data).
        var tierMismatch = FindById(diagnostics, "MIST_OWN_THIS_TIER_MISMATCH");
        Assert.NotNull(tierMismatch);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. End-to-end: owned binding, move field out, access sibling
    //    → succeeds (Req 16.2)
    //
    // Per-field state tracking allows moving out of one owned field while
    // the sibling field remains accessible.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedBinding_MoveFieldOut_AccessSibling_Succeeds()
    {
        const string source = """
            class Container
            {
                public owned string First;
                public owned string Second;

                public Container(owned string first, owned string second)
                {
                    this.First = first;
                    this.Second = second;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var c = new Container("aaa", "bbb");
                    owned var moved = c.First;
                    var sibling = c.Second;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Moving First out leaves Second accessible (sibling independence).
        // No use-after-move or field state violation expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_USE_AFTER_MOVE"));
        Assert.Null(FindById(diagnostics, "MIST_OWN_FIELD_STATE_VIOLATION"));
        Assert.False(HasErrors(diagnostics),
            $"Expected clean compilation but got errors: {string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. End-to-end: owned var x = new T(); x.MutateTier(); → Owned binding
    //    satisfies this-tier, call succeeds (Req 17.16, Req 18.1)
    //
    // An owned binding initialized from a constructor can call methods that
    // require Borrowed access to 'this' (Owned satisfies Borrowed). This
    // validates that the orchestrator correctly chains ThisTierInferencePass
    // results with the flow-sensitive call-site check.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedBinding_ConstructorInit_MutateTierCall_Succeeds()
    {
        const string source = """
            owned class Vault
            {
                public owned string Secret;

                public Vault(owned string secret)
                {
                    this.Secret = secret;
                }

                public void RotateSecret(owned string newSecret)
                {
                    this.Secret = newSecret;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var x = new Vault("initial");
                    x.RotateSecret("rotated");
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // The owned binding 'x' provides Owned tier which satisfies RotateSecret's
        // inferred Borrowed tier (Owned >= Borrowed). No mismatch expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_THIS_TIER_MISMATCH"));
        Assert.Null(FindById(diagnostics, "MIST_OWN_MUTABLE_HIDDEN"));
        Assert.False(HasErrors(diagnostics),
            $"Expected clean compilation but got errors: {string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }
}