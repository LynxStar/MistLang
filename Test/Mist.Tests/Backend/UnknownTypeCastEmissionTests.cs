// Feature: unknown-type (Phase E, Task 30.3)
// **Validates: Requirements 13.4, 13.5, 15.4**
//
// Unit tests for Task 30: C# emission of cast expressions involving the
// user-facing `unknown` type.
//
// Two sub-rules exercised here:
//
//   * Task 30.1 / Req 13.5, 15.4 — `(unknown) expr` emits a C# cast to
//     `object` with no operator dispatch. The emitter produces the
//     `((object) <operand>)` form (the double parens mirror the
//     existing cast-operators emission strategy of wrapping every cast
//     in an outer parenthesis so it is safe to drop into a larger
//     expression context).
//
//   * Task 30.2 / Req 13.4, 15.4 — `(T) expr` where `expr` is typed
//     `unknown` and `T` declares `implicit operator T(unknown)` (or the
//     `explicit` form) emits as a standard C# cast `((T) <operand>)`.
//     The C# compiler dispatches to the underlying `T.op_Implicit` /
//     `T.op_Explicit` method at compile time. This matches the
//     cast-operators feature's existing emission rule (see
//     `CastOperatorEmissionPropertyTests` Property 7c: "User-defined
//     cast operators are emitted as standard C# cast expressions
//     ((TargetType)operand), which lets the C# compiler invoke the
//     operator").
//
// These tests follow the workspace's `test-process-spawning.md`
// policy: compilation is performed in-process via the lexer/parser/
// semantic-model-builder/emitter pipeline directly. No `dotnet`
// subprocess is spawned and no Roslyn compilation is attempted — the
// emitted C# is inspected as text. This mirrors the pattern used by
// `CastOperatorEmissionPropertyTests` and `EmitterOutputTests`.

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
/// Unit tests for C# emission of cast expressions whose source or
/// target type is the user-facing <c>unknown</c> top type. Each test
/// lexes and parses a small Mist source string, builds the semantic
/// model, runs the type checker, and invokes <see cref="CSharpEmitter"/>
/// directly. The emitted C# file contents are then inspected with
/// substring assertions. No external process is spawned.
/// </summary>
public sealed class UnknownTypeCastEmissionTests : IDisposable
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();
    private readonly string _outputDir;

    public UnknownTypeCastEmissionTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistUnknownCastEmission_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); }
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
    /// stage produces an error diagnostic — emission of cast
    /// expressions is only meaningful when the type checker has
    /// resolved them.
    /// </summary>
    private string EmitAndReadCsharp(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);

        // Lex.
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, ErrorReport("lex", lexBag.ToImmutableArray()));

        // Parse.
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, ErrorReport("parse", parseBag.ToImmutableArray()));
        Assert.NotNull(parseResult.Root);

        // Build semantic model.
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, ErrorReport("semantic model", semBag.ToImmutableArray()));

        // Type check. Required so cast expressions with resolved
        // operators have `GetResolvedOperator` populated by the time
        // the emitter runs (`EmitCastExpression` consults it).
        var tcBag = new DiagnosticBag();
        _analyzer.Analyze(model, tcBag);
        Assert.False(tcBag.HasErrors, ErrorReport("type check", tcBag.ToImmutableArray()));

        // Emit.
        CleanOutputDir();
        var emitBag = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", emitBag);
        Assert.False(emitBag.HasErrors, ErrorReport("emit", emitBag.ToImmutableArray()));

        // Read user-emitted .cs files. Exclude the `MistVoid.g.cs`
        // helper and any `.csproj` artifact the backend may drop.
        var csFiles = Directory.GetFiles(_outputDir, "*.cs", SearchOption.AllDirectories)
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
        if (errors.IsEmpty) return "";
        return $"Unexpected errors during {stage}:\n" + string.Join(
            "\n",
            errors.Select(d => $"  [{d.Id.Value}] {d.Message}"));
    }

    private void CleanOutputDir()
    {
        if (Directory.Exists(_outputDir))
        {
            foreach (var file in Directory.GetFiles(_outputDir, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 30.1 / Req 13.5, 15.4
    //
    // `(unknown) expr` emits a C# cast to `object`. No operator dispatch
    // is performed — the top-type rule in `IsDirectlyAssignable`
    // short-circuits `CheckCastExpression` (see Task 26.2), so
    // `_model.GetResolvedOperator(castExpr)` returns null and
    // `EmitCastExpression` falls into the "identity/subtype" branch
    // that produces a standard C# cast `((object) <operand>)`.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The canonical Task 30.3 bullet 1 scenario: a cast of an integer
    /// literal to <c>unknown</c> emits <c>(object)</c> near the literal
    /// <c>42</c>. We assert the substrings after collapsing whitespace
    /// so the test is resilient to the emitter's formatting choices.
    /// </summary>
    [Fact]
    public void CastToUnknown_FromIntLiteral_EmitsObjectCast()
    {
        const string mistSource =
            "class Foo {\n" +
            "    public unknown Widen() {\n" +
            "        return (unknown) 42;\n" +
            "    }\n" +
            "    public static void Main() { }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        // Adjacency check (whitespace-insensitive): the emission shape
        // is `((object)42)` per `EmitCastExpression`.
        var normalized = Collapse(csharp);
        Assert.Contains("(object)42", normalized);

        // Sanity: the method's return type also lowers to `object`
        // via `CSharpTypeMap` (Task 29.1). This confirms the
        // `unknown` token is reaching the emitter as the top-type
        // singleton, not being mishandled as an identifier.
        Assert.Contains("object Widen", csharp);
    }

    /// <summary>
    /// Casting a user-defined class instance to <c>unknown</c>
    /// produces the same emission shape — the rule is source-type-
    /// agnostic because the top-type path short-circuits before any
    /// source-type-dependent operator search.
    /// </summary>
    [Fact]
    public void CastToUnknown_FromUserClassInstance_EmitsObjectCast()
    {
        const string mistSource =
            "class Bar {\n" +
            "    public int value;\n" +
            "    public Bar(int v) { this.value = v; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public unknown Widen(Bar b) {\n" +
            "        return (unknown) b;\n" +
            "    }\n" +
            "    public static void Main() { }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        var normalized = Collapse(csharp);
        Assert.Contains("(object)b", normalized);
        Assert.Contains("object Widen", csharp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 30.2 / Req 13.4, 15.4
    //
    // `(T) expr` where `expr` is typed `unknown` and `T` declares a
    // user-defined `operator_implicit_T(unknown)` or
    // `operator_explicit_T(unknown)` emits as a standard C# cast
    // `((T) <operand>)`. The C# compiler dispatches to the underlying
    // `T.op_Implicit` / `T.op_Explicit` method at compile time, matching
    // the cast-operators feature's existing emission rule.
    //
    // Design doc § "Cast Expression Emission" explicitly enumerates
    // this case: `UnknownType → Concrete T` with a user-defined
    // operator emits `T.op_Explicit(expr)` or `T.op_Implicit(expr)`.
    // The cast-operators feature implements that lowering by emitting
    // a standard C# cast expression and letting the C# compiler
    // resolve it — verified by `CastOperatorEmissionPropertyTests.
    // Property 7c`.
    //
    // Note: these tests inspect the cast-expression emission only.
    // The cast-operator declaration's parameter type is lowered by a
    // separate code path (`EmitParameterList`), which Task 30.2
    // also exercises — its correct output (`object`, not `unknown`)
    // is asserted below.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The canonical Task 30.3 bullet 2 scenario: a cast from an
    /// <c>unknown</c>-typed local to <c>MyInt</c> where <c>MyInt</c>
    /// declares <c>implicit operator MyInt(unknown u)</c> emits as a
    /// standard C# cast <c>((MyInt) x)</c>. The C# compiler resolves
    /// that cast to <c>MyInt.op_Implicit(x)</c> at compile time.
    /// </summary>
    [Fact]
    public void CastFromUnknown_WithImplicitOperator_EmitsStandardCast()
    {
        const string mistSource =
            "class MyInt {\n" +
            "    public int value;\n" +
            "    public MyInt(int v) { this.value = v; }\n" +
            "    public static implicit operator MyInt(unknown u) {\n" +
            "        return new MyInt(0);\n" +
            "    }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public MyInt Convert(unknown x) {\n" +
            "        return (MyInt) x;\n" +
            "    }\n" +
            "    public static void Main() { }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        // The cast expression in `Convert` emits as `((MyInt) x)`
        // (or `((MyInt)x)` with no whitespace). We assert the
        // whitespace-collapsed form so the test is resilient to
        // formatting.
        var normalized = Collapse(csharp);
        Assert.Contains("(MyInt)x", normalized);

        // The cast-operator declaration must emit with `object` as
        // the parameter type (not `unknown`) — otherwise the C# layer
        // would see an undefined identifier. This is Task 30.2's
        // emitter support: `EmitParameterList`'s source-text shortcut
        // is overridden for `unknown` parameters so they route
        // through `EmitTypeReference`, which returns `"object"`.
        Assert.Contains("implicit operator MyInt(object", csharp);
        Assert.DoesNotContain("implicit operator MyInt(unknown", csharp);
    }

    /// <summary>
    /// Same rule for an explicit operator: <c>explicit operator
    /// MyInt(unknown u)</c> also emits as a standard C# cast
    /// <c>((MyInt) x)</c>, dispatched by the C# compiler to
    /// <c>MyInt.op_Explicit</c>.
    /// </summary>
    [Fact]
    public void CastFromUnknown_WithExplicitOperator_EmitsStandardCast()
    {
        const string mistSource =
            "class MyInt {\n" +
            "    public int value;\n" +
            "    public MyInt(int v) { this.value = v; }\n" +
            "    public static explicit operator MyInt(unknown u) {\n" +
            "        return new MyInt(0);\n" +
            "    }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public MyInt Convert(unknown x) {\n" +
            "        return (MyInt) x;\n" +
            "    }\n" +
            "    public static void Main() { }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        var normalized = Collapse(csharp);
        Assert.Contains("(MyInt)x", normalized);
        Assert.Contains("explicit operator MyInt(object", csharp);
        Assert.DoesNotContain("explicit operator MyInt(unknown", csharp);
    }

    /// <summary>
    /// Negative control: a cast to <c>unknown</c> must not emit a
    /// static method call of the shape <c>object.op_Implicit(...)</c>.
    /// The top-type path in <c>EmitCastExpression</c> skips operator
    /// dispatch entirely (Task 30.1).
    /// </summary>
    [Fact]
    public void CastToUnknown_DoesNotEmitOperatorStaticCall()
    {
        const string mistSource =
            "class Foo {\n" +
            "    public unknown Widen() {\n" +
            "        return (unknown) 42;\n" +
            "    }\n" +
            "    public static void Main() { }\n" +
            "}\n";

        var csharp = EmitAndReadCsharp(mistSource);

        Assert.DoesNotContain("object.op_Implicit", csharp);
        Assert.DoesNotContain("object.op_Explicit", csharp);
    }

    /// <summary>
    /// Collapses all whitespace in <paramref name="s"/> so the
    /// substring assertions above are insensitive to the emitter's
    /// spacing/indentation choices.
    /// </summary>
    private static string Collapse(string s) =>
        new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
}
