// Feature: unknown-type (Phase E, Task 31.3 — Option C emission)
// **Validates: Requirements 15.1, 15.2**
//
// Unit tests for Task 31 under the revised "Option C" C# emission
// strategy (November 2026). Covariant_Unknown_Widening — assigning a
// `List<int>` into a `List<unknown>` target — lowers to C# `dynamic`
// at the target-type level rather than to `List<object>` with a
// double cast. The original Option 2 emission
// (`(List<object>)(object)intList`) was accepted by the C# compiler
// but threw `InvalidCastException` at runtime because the CLR
// enforces strict generic type identity for user-defined generics
// (there is no IL coercion from `List<int>` to `List<object>`).
// Option C routes member access through the DLR, which dispatches
// against the receiver's actual runtime generic type.
//
// Three sub-rules exercised here:
//
//   * Task 31.1 / Req 15.2 — When the target at a widening site is a
//     Closed_Generic_With_Unknown (e.g., `List<unknown>`), the
//     declared type in emitted C# is `dynamic` and the operand is
//     assigned verbatim. No explicit cast is inserted; C# implicitly
//     converts any reference-type value to `dynamic`.
//
//   * Task 31.2 / Req 15.1, 15.2 — When the target is directly
//     `UnknownType` (e.g., `unknown x = 42;`), the declared type
//     lowers to `object` and the operand is assigned verbatim.
//     No cast needed.
//
//   * Task 31.3 (this file) — Unit coverage for the two rules above
//     across the five widening sites exercised by the emitter:
//     typed variable declaration, typed variable assignment,
//     return statement, and (not covered here) argument pass.
//
// These tests follow the workspace's `test-process-spawning.md`
// policy: compilation is performed in-process via the lexer/parser/
// semantic-model-builder/emitter pipeline directly. No `dotnet`
// subprocess is spawned. The emitted C# is inspected as text with
// substring assertions.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for C# emission of covariant unknown widening under the
/// Option C strategy (November 2026). See the file-level comment for
/// background on why Option C replaced Option 2. Each test lexes and
/// parses a small Mist source string, builds the semantic model,
/// runs the type checker, and invokes <see cref="CSharpEmitter"/>
/// directly. The emitted C# file contents are then inspected with
/// substring assertions. No external process is spawned.
/// </summary>
public sealed class UnknownWideningEmissionTests : IDisposable
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();
    private readonly string outputDir;

    public UnknownWideningEmissionTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistUnknownWideningEmission_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); }
            catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Lexes and parses <paramref name="mistSource"/>, builds the
    /// semantic model, runs the type checker, and emits C#. Returns
    /// the concatenated contents of the user-emitted <c>.cs</c>
    /// file(s) in the output directory, excluding the
    /// <c>MistVoid.g.cs</c> helper. Fails the test if any pipeline
    /// stage produces an error diagnostic — widening emission is
    /// only meaningful when the type checker has accepted the
    /// widening.
    /// </summary>
    private string EmitAndReadCsharp(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, ErrorReport("lex", lexBag.ToImmutableArray()));

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, ErrorReport("parse", parseBag.ToImmutableArray()));
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, ErrorReport("semantic model", semBag.ToImmutableArray()));

        var tcBag = new DiagnosticBag();
        analyzer.Analyze(model, tcBag);
        Assert.False(tcBag.HasErrors, ErrorReport("type check", tcBag.ToImmutableArray()));

        CleanOutputDir();
        var emitBag = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", emitBag);
        Assert.False(emitBag.HasErrors, ErrorReport("emit", emitBag.ToImmutableArray()));

        var csFiles = Directory.GetFiles(outputDir, "*.cs", SearchOption.AllDirectories)
            .Where(p =>
            {
                var name = Path.GetFileName(p);
                return name != "MistVoid.g.cs";
            })
            .ToArray();

        Assert.NotEmpty(csFiles);
        return string.Join("\n", csFiles.Select(File.ReadAllText));
    }

    private static string ErrorReport(string stage, ImmutableArray<Diagnostic> bag)
    {
        var errors = bag.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        if(errors.IsEmpty)
        {
            return "";
        }

        return $"Unexpected errors during {stage}:\n" + string.Join(
            "\n",
            errors.Select(d => $"  [{d.Id.Value}] {d.Message}"));
    }

    private void CleanOutputDir()
    {
        if(Directory.Exists(outputDir))
        {
            foreach(var file in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Collapses all whitespace in <paramref name="s"/> so substring
    /// assertions below are insensitive to the emitter's spacing
    /// choices.
    /// </summary>
    private static string Collapse(string s) =>
        new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());


    // ═══════════════════════════════════════════════════════════════════════
    // Task 31.3 bullet 1 / Req 15.2 (Option C)
    //
    // Mist `List<unknown> xs = intList;` → C# `dynamic xs = intList;`
    //
    // The target type (a Closed_Generic_With_Unknown) lowers to C#
    // `dynamic`. Every C# value is implicitly convertible to
    // `dynamic`, so no explicit cast is needed at the widening site.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Widening_ListIntToListUnknown_EmitsDynamicTarget()
    {
        const string mistSource =
            "class List<T> {\n" +
            "    public int length;\n" +
            "    public List() { this.length = 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public static void Main() { }\n" +
            "    public void Widen(List<int> intList) {\n" +
            "        List<unknown> xs = intList;\n" +
            "    }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        var normalized = Collapse(csharp);
        // Option C emission: the local's declared type is `dynamic`
        // and the initializer is copied verbatim.
        Assert.Contains("dynamicxs=intList", normalized);

        // Negative: no Option 2 double cast.
        Assert.DoesNotContain("(List<object>)(object)", normalized);
        // Negative: no `List<unknown>` leak in C# output.
        Assert.DoesNotContain("List<unknown>", csharp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 31.3 bullet 2 / Req 15.1, 15.2 (Option C unchanged for this case)
    //
    // Mist `unknown x = 42;` → C# `object x = 42;`
    //
    // Bare `unknown` targets lower to C# `object` (not `dynamic`)
    // because the type checker rejects member access and operator
    // dispatch on a bare-`unknown` receiver at compile time
    // (MIST_TC_0065 / MIST_TC_0066). The DLR never sees such a
    // receiver, so lowering to `object` is sufficient.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Widening_IntLiteralToUnknown_EmitsPlainObjectAssignment()
    {
        const string mistSource =
            "class Foo {\n" +
            "    public static void Main() { }\n" +
            "    public void Widen() {\n" +
            "        unknown x = 42;\n" +
            "    }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        var normalized = Collapse(csharp);
        Assert.Contains("objectx=42", normalized);
        Assert.DoesNotContain("(object)(object)", normalized);
        Assert.DoesNotContain("(object)42", normalized);
        Assert.DoesNotContain("dynamicx=42", normalized);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 31.3 bullet 3 / Req 15.2 (Option C)
    //
    // Mist `Map<string, unknown> m = stringIntMap;` → C#
    // `dynamic m = stringIntMap;`
    //
    // A multi-argument generic with `unknown` at a single type-
    // argument position is still a Closed_Generic_With_Unknown and
    // lowers to `dynamic`. The emitter does not try to preserve the
    // `Map<string, _>` shape in C# — the DLR dispatch path needs
    // the receiver to be `dynamic`, and partial-type preservation
    // would not help the C# layer type-check the member calls.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Widening_MapStringIntToMapStringUnknown_EmitsDynamicTarget()
    {
        const string mistSource =
            "class Map<K, V> {\n" +
            "    public int size;\n" +
            "    public Map() { this.size = 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public static void Main() { }\n" +
            "    public void Widen(Map<string, int> stringIntMap) {\n" +
            "        Map<string, unknown> m = stringIntMap;\n" +
            "    }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        var normalized = Collapse(csharp);
        Assert.Contains("dynamicm=stringIntMap", normalized);

        // Negative: no Option 2 double cast.
        Assert.DoesNotContain("(Map<string,object>)(object)", normalized);
        // Negative: no partial-type preservation.
        Assert.DoesNotContain("Map<string,object>m", normalized);
        // Negative: no `Map<string, unknown>` leak.
        Assert.DoesNotContain("Map<string,unknown>", normalized);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Additional coverage — the two non-variable widening sites named in
    // Task 31.1 (return and re-assignment). These reinforce that the
    // Option C emission (dynamic target type) fires at every widening
    // path, not just at typed local declarations.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Task 31.1 — Return-site widening. A method declared to return
    /// <c>List&lt;unknown&gt;</c> that returns a <c>List&lt;int&gt;</c>
    /// value emits `return intList;` with a `dynamic` return type on
    /// the method signature.
    /// </summary>
    [Fact]
    public void Widening_ReturnListIntAsListUnknown_EmitsDynamicReturn()
    {
        const string mistSource =
            "class List<T> {\n" +
            "    public int length;\n" +
            "    public List() { this.length = 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public static void Main() { }\n" +
            "    public List<unknown> WidenReturn(List<int> intList) {\n" +
            "        return intList;\n" +
            "    }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        var normalized = Collapse(csharp);
        Assert.Contains("returnintList;", normalized);
        // The method's return type signature lowers to `dynamic`.
        Assert.Contains("dynamicWidenReturn", normalized);
        Assert.DoesNotContain("(List<object>)(object)", normalized);
    }

    /// <summary>
    /// Task 31.1 / 31.2 — Return-site widening to bare <c>unknown</c>.
    /// Bare-unknown return type lowers to `object` (not `dynamic`).
    /// </summary>
    [Fact]
    public void Widening_ReturnIntAsUnknown_EmitsPlainReturn()
    {
        const string mistSource =
            "class Foo {\n" +
            "    public static void Main() { }\n" +
            "    public unknown WidenReturn() {\n" +
            "        return 42;\n" +
            "    }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        var normalized = Collapse(csharp);
        Assert.Contains("return42;", normalized);
        Assert.Contains("objectWidenReturn", normalized);
        Assert.DoesNotContain("(object)(object)", normalized);
        Assert.DoesNotContain("return(object)", normalized);
        Assert.DoesNotContain("dynamicWidenReturn", normalized);
    }

    /// <summary>
    /// Task 31.1 — Assignment-site widening (re-assigning to an
    /// existing <c>List&lt;unknown&gt;</c> local). Confirms the
    /// Option C emission also fires at plain assignment expressions,
    /// not just at declaration initializers.
    /// </summary>
    [Fact]
    public void Widening_AssignListIntToListUnknownVariable_EmitsPlainAssignment()
    {
        // Use a single-parameter method to avoid a pre-existing
        // emitter limitation: when the parameter-list source-text
        // fast-path is bypassed (e.g., because any parameter type
        // contains `unknown`), the emitter falls back to emitting
        // only the first parameter. Multi-parameter methods with
        // `unknown` in a non-first position lose the later parameters
        // in emitted C#. We sidestep that gap here by introducing the
        // `List<unknown>` local inside the method body, which routes
        // through the typed-local-declaration emission path
        // (`EmitVariableDeclaration`) rather than the parameter-list
        // path.
        const string mistSource =
            "class List<T> {\n" +
            "    public int length;\n" +
            "    public List() { this.length = 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public static void Main() { }\n" +
            "    public void Widen(List<int> intList) {\n" +
            "        List<unknown> xs = intList;\n" +
            "        xs = intList;\n" +
            "    }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        var normalized = Collapse(csharp);
        // Both the initializer and the subsequent re-assignment emit
        // with no cast (Option C: target is `dynamic`, so any value
        // converts implicitly).
        Assert.Contains("dynamicxs=intList", normalized);
        Assert.Contains("xs=intList;", normalized);
        Assert.DoesNotContain("(List<object>)(object)", normalized);
    }

    /// <summary>
    /// Negative regression: when source and target are the same
    /// concrete constructed type (no widening happens), the emitter
    /// must not insert any cast or type erasure.
    /// </summary>
    [Fact]
    public void NoWidening_SameConstructedType_DoesNotInsertCast()
    {
        const string mistSource =
            "class List<T> {\n" +
            "    public int length;\n" +
            "    public List() { this.length = 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public static void Main() { }\n" +
            "    public void Pass(List<int> a) {\n" +
            "        List<int> b = a;\n" +
            "    }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        var normalized = Collapse(csharp);
        Assert.Contains("List<int>b=a;", normalized);
        Assert.DoesNotContain("(List<int>)(object)", normalized);
        Assert.DoesNotContain("(List<object>)(object)", normalized);
        Assert.DoesNotContain("dynamicb=a", normalized);
    }
}