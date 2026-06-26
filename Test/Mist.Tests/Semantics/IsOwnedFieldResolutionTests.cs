// Task 2.3 — Unit tests for `IsOwnedField` resolution on field symbols.
//
// Validates that the SemanticModelBuilder correctly sets IsOwnedField = true for:
//   - Explicitly declared `owned` fields
//   - All instance fields of `owned type` (implicit marking via PropagateOwnedFieldsForOwnedTypes)
//   - Redundant explicit `owned` on a field within an `owned type` (accepted without error)
// And that:
//   - Non-owned fields have IsOwnedField == false
//   - Static fields with `owned` produce MIST_OWN_STATIC_FIELD_OWNED error
//
// Uses in-process lex → parse → semantic model build (no process spawning for model tests).
// Uses CompilerDriver for the static field diagnostic test.
//
// Validates: Requirements 1.1, 1.2, 1.5, 3.1, 3.2

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for <see cref="Symbol.IsOwnedField"/> resolution (Task 2.3).
/// Verifies that the semantic model correctly marks fields as owned based on
/// explicit <c>owned</c> annotation or implicit propagation from <c>owned type</c>.
///
/// <b>Validates: Requirements 1.1, 1.2, 1.5, 3.1, 3.2</b>
/// </summary>
public sealed class IsOwnedFieldResolutionTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder builder = new();

    // ─── Shared CompilerDriver for diagnostic-level tests ────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Lexes, parses, and builds a semantic model from the given Mist source.
    /// Asserts no lex or parse errors are produced.
    /// </summary>
    private ISemanticModel BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse should not produce errors. Errors: {string.Join(", ", parseBag.ToImmutableArray().Select(d => d.Message))}");
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        return builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);
    }

    /// <summary>
    /// Finds a field symbol by name in the semantic model and returns it cast to <see cref="Symbol"/>.
    /// </summary>
    private static Symbol FindField(ISemanticModel model, string fieldName)
    {
        return (Symbol)model.AllSymbols.First(s => s.Name == fieldName && s.Kind == SymbolKind.Field);
    }

    /// <summary>
    /// Compiles source using the full pipeline and returns diagnostics.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_owned_field_" + Guid.NewGuid().ToString("N"));
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

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Explicit `owned` field → IsOwnedField == true (Req 1.1, 1.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExplicitOwnedField_IsOwnedField_ReturnsTrue()
    {
        const string source = """
            class Widget
            {
                public int Value;
            }
            class Container
            {
                owned Widget data;
            }
            """;

        var model = BuildModel(source);
        var dataField = FindField(model, "data");

        Assert.True(dataField.IsOwnedField);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Non-owned field → IsOwnedField == false
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NonOwnedField_IsOwnedField_ReturnsFalse()
    {
        const string source = """
            class Widget
            {
                public int Value;
            }
            class Container
            {
                Widget data;
            }
            """;

        var model = BuildModel(source);
        var dataField = FindField(model, "data");

        Assert.False(dataField.IsOwnedField);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. `owned type` implicit marking: all instance fields report
    //    IsOwnedField == true (Req 3.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedType_AllInstanceFields_AreImplicitlyOwned()
    {
        const string source = """
            owned class Vault
            {
                int Secret;
                int AccessCount;
            }
            """;

        var model = BuildModel(source);
        var secretField = FindField(model, "Secret");
        var accessCountField = FindField(model, "AccessCount");

        Assert.True(secretField.IsOwnedField);
        Assert.True(accessCountField.IsOwnedField);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Redundant `owned` on a field within an `owned type` is accepted
    //    without error (Req 3 AC 2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedType_RedundantOwnedOnField_AcceptedWithoutError()
    {
        const string source = """
            class Widget
            {
                public int Value;
            }
            owned class Vault
            {
                owned Widget data;
                int count;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        // Verify IsOwnedField is true (set both explicitly and via propagation)
        var model = BuildModel(source);
        var dataField = FindField(model, "data");
        var countField = FindField(model, "count");

        Assert.True(dataField.IsOwnedField);
        Assert.True(countField.IsOwnedField);

        // Verify no error is emitted for the redundant annotation
        var diagnostics = CompileAndGetDiagnostics(source);
        // Should not have any error related to field ownership being redundant
        var ownedFieldErrors = diagnostics.Where(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("data", StringComparison.Ordinal) &&
            d.Message.Contains("redundant", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(ownedFieldErrors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. `owned` on static field → MIST_OWN_STATIC_FIELD_OWNED error
    //    (Req 1.5)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedOnStaticField_EmitsError()
    {
        const string source = """
            class Widget
            {
                public int Value;
            }
            class Container
            {
                static owned Widget shared;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_STATIC_FIELD_OWNED");

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("shared", diag.Message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Additional: `owned type` does NOT mark static fields (Req 1.5
    //    implies statics excluded from propagation)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedType_StaticField_IsNotMarkedOwned()
    {
        const string source = """
            owned class Vault
            {
                int instanceField;
                static int staticField;
            }
            """;

        var model = BuildModel(source);
        var instanceField = FindField(model, "instanceField");
        var staticField = FindField(model, "staticField");

        Assert.True(instanceField.IsOwnedField);
        Assert.False(staticField.IsOwnedField);
    }
}