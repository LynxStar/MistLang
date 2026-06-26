using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for type alias semantic model diagnostics.
///
/// Validates:
/// * Requirement 4.3 — <c>MIST_SB_0061</c> fires when a type alias name
///   is already defined in the same scope.
/// * Requirement 4.4 — <c>MIST_SB_0062</c> fires when the target type in
///   a type alias declaration could not be resolved.
/// * Requirement 5.4 — <c>MIST_SB_0060</c> fires when a type alias forms
///   a cycle (direct or indirect).
/// </summary>
public class TypeAliasDiagnosticTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder builder = new();

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, runs the semantic model
    /// builder, and returns the diagnostics it produced.
    /// </summary>
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

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ─── MIST_SB_0061 — duplicate type alias name (Requirement 4.3) ──────

    [Fact]
    public void MIST_SB_0061_FiresOnDuplicateAliasName()
    {
        // Two type aliases with the same name in the same scope.
        var source = "type Foo = int; type Foo = string;";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var dupDiags = WithId(diagnostics, "MIST_SB_0061");

        _ = Assert.Single(dupDiags);
        var d = dupDiags[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Foo", d.Message);
        Assert.Contains("already defined", d.Message);
    }

    [Fact]
    public void MIST_SB_0061_DoesNotFireOnDistinctAliasNames()
    {
        // Two type aliases with different names — no duplicate.
        var source = "type Foo = int; type Bar = string;";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0061"));
    }

    // ─── MIST_SB_0062 — unresolved type in alias target (Requirement 4.4) ─

    [Fact]
    public void MIST_SB_0062_FiresOnUnresolvedTargetType()
    {
        // The target type 'NonExistentType' does not exist in scope.
        var source = "type Foo = NonExistentType;";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var unresolvedDiags = WithId(diagnostics, "MIST_SB_0062");

        _ = Assert.Single(unresolvedDiags);
        var d = unresolvedDiags[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("could not be resolved", d.Message);
        Assert.Contains("Foo", d.Message);
    }

    [Fact]
    public void MIST_SB_0062_DoesNotFireOnResolvedTargetType()
    {
        // The target type 'int' is a well-known primitive — resolves fine.
        var source = "type Foo = int;";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0062"));
    }

    // ─── MIST_SB_0060 — cyclic type alias (Requirement 5.4) ──────────────

    [Fact]
    public void MIST_SB_0060_FiresOnDirectCycle()
    {
        // Direct cycle: A references B, B references A.
        var source = "type A = B; type B = A;";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var cycleDiags = WithId(diagnostics, "MIST_SB_0060");

        // At least one cycle diagnostic should fire.
        Assert.NotEmpty(cycleDiags);
        Assert.All(cycleDiags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void MIST_SB_0060_FiresOnIndirectCycle()
    {
        // Indirect cycle: A → B → C → A.
        var source = "type A = B; type B = C; type C = A;";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var cycleDiags = WithId(diagnostics, "MIST_SB_0060");

        // At least one cycle diagnostic should fire.
        Assert.NotEmpty(cycleDiags);
        Assert.All(cycleDiags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void MIST_SB_0060_DoesNotFireOnNonCyclicChain()
    {
        // Non-cyclic chain: A → B → int (no cycle).
        var source = "type A = B; type B = int;";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0060"));
    }
}