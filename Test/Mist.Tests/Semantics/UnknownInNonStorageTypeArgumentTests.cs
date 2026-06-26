// Feature: unknown-type (Phase B, Task 14)
// Validates Requirements 7.1, 7.2, 7.3, 7.4 — `unknown` as a type argument
// is permitted in non-storage positions (local variables, method parameters,
// method return types), and nested occurrences at arbitrary depth resolve
// without any special casing beyond what Task 8 established.
//
// This is a regression test: Task 8 already demonstrated the resolution
// path works end-to-end (see UnknownTypeResolutionTests). Task 14 locks
// in that `unknown`-in-type-argument programs parse, resolve, and do NOT
// produce storage-ban diagnostics when used in the permitted positions.
//
// All tests run fully in-process — no `dotnet` process spawns (per the
// workspace test-process-spawning policy).

using System.Collections.Immutable;
using System.Linq;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Regression tests for Task 14.2 — `unknown` as a type argument in
/// non-storage positions.
///
/// <para>
/// The positive tests below exercise the four positions called out in the
/// task: method parameter type, method return type, local variable type,
/// and nested type-argument occurrences. In every case the semantic model
/// builder must:
/// </para>
/// <list type="bullet">
/// <item>Parse and resolve the source without producing a parse error.</item>
/// <item>NOT emit any storage-ban diagnostic
/// (<c>MIST_SB_0050</c>–<c>0057</c>, <c>MIST_SB_0054</c>): none of these
/// positions are writable storage.</item>
/// </list>
/// </summary>
public class UnknownInNonStorageTypeArgumentTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder builder = new();

    private ImmutableArray<Diagnostic> BuildAndCollectDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, "Parse should not produce errors");
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        _ = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return semBag.ToImmutableArray();
    }

    /// <summary>
    /// Returns <c>true</c> if no storage-ban diagnostic
    /// (<c>MIST_SB_0050</c>–<c>0057</c>) was emitted.
    /// </summary>
    private static bool HasNoStorageBanDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return !diagnostics.Any(d =>
            d.Id.Value.StartsWith("MIST_SB_00", StringComparison.Ordinal) &&
            d.Id.Value.Length == "MIST_SB_0000".Length &&
            d.Id.Value[^4] == '0' &&
            d.Id.Value[^3] == '0' &&
            (d.Id.Value[^2] == '5' || d.Id.Value[^2] == '6') &&
            d.Id.Value[^1] is >= '0' and <= '9');
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.1 — `unknown` as a type argument is permitted.
    // Requirement 7.4 — local, parameter, and return positions accept
    // `Closed_Generic_With_Unknown`.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OptionalOfUnknown_AsLocalVariableType_DoesNotEmit_StorageBanDiagnostic()
    {
        // `Optional<unknown> o = 0;` — Req 7.4: local variable is a
        // non-storage position; the storage ban must not fire.
        const string source =
            "class Optional<T> { } " +
            "class Foo { public void M() { Optional<unknown> o = 0; } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.True(HasNoStorageBanDiagnostics(diagnostics),
            $"Expected no storage-ban diagnostics; got: {string.Join(", ", diagnostics.Select(d => d.Id.Value))}");
    }

    [Fact]
    public void ListOfUnknown_AsMethodParameterType_DoesNotEmit_StorageBanDiagnostic()
    {
        // `fn M(List<unknown> xs)` — Req 7.4: method parameter is a
        // non-storage position.
        const string source =
            "class List<T> { } " +
            "class Foo { public void M(List<unknown> xs) { } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.True(HasNoStorageBanDiagnostics(diagnostics),
            $"Expected no storage-ban diagnostics; got: {string.Join(", ", diagnostics.Select(d => d.Id.Value))}");
    }

    [Fact]
    public void MapOfStringUnknown_AsMethodReturnType_DoesNotEmit_StorageBanDiagnostic()
    {
        // `fn M() : Map<string, unknown>` — Req 7.4: method return type is a
        // non-storage position.
        const string source =
            "class Map<K, V> { } " +
            "class Foo { public Map<string, unknown> M() { return 0; } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.True(HasNoStorageBanDiagnostics(diagnostics),
            $"Expected no storage-ban diagnostics; got: {string.Join(", ", diagnostics.Select(d => d.Id.Value))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.2 — nested `unknown` occurrences at arbitrary depth
    // resolve without any special casing.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OptionalOfListOfUnknown_NestedOnLocal_DoesNotEmit_StorageBanDiagnostic()
    {
        // `Optional<List<unknown>> o` — Req 7.2: two-level nesting.
        const string source =
            "class Optional<T> { } " +
            "class List<T> { } " +
            "class Foo { public void M() { Optional<List<unknown>> o = 0; } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.True(HasNoStorageBanDiagnostics(diagnostics),
            $"Expected no storage-ban diagnostics; got: {string.Join(", ", diagnostics.Select(d => d.Id.Value))}");
    }

    [Fact]
    public void MapOfUnknownListOfUnknown_NestedOnParameter_DoesNotEmit_StorageBanDiagnostic()
    {
        // `Map<unknown, List<unknown>>` — Req 7.2: two unknown positions,
        // one direct and one nested.
        const string source =
            "class Map<K, V> { } " +
            "class List<T> { } " +
            "class Foo { public void M(Map<unknown, List<unknown>> m) { } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.True(HasNoStorageBanDiagnostics(diagnostics),
            $"Expected no storage-ban diagnostics; got: {string.Join(", ", diagnostics.Select(d => d.Id.Value))}");
    }
}