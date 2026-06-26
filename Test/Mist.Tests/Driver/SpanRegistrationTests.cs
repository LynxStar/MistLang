// Feature: stack-only-types
// Task 3.2: Smoke test for Span.mist registration
// Validates: Requirements 1.1, 1.3, 2.1, 2.3
//
// Architecture note: like Array<T, Length>, the dependent-param generic intrinsic
// Span<T, Length:ZeroOrPositiveInt> is dropped by the PEG parser (the declaration
// never registers a StructType in the semantic model). This is the established
// Array precedent — use-site routing to the dedicated SpanType IType carries all
// ref-struct/backend-backed semantics. This test verifies that (a) Span.mist loads
// without errors and (b) the SpanType carries the correct Kind and ref-struct
// characteristics.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Driver;

/// <summary>
/// Smoke test for <c>Source/Standard/Bootstrap/Span.mist</c> registration.
/// Follows the Array precedent: the parser drops the dependent-param generic
/// declaration, so no <c>StructType</c> is registered in the semantic model.
/// Instead, the dedicated <see cref="SpanType"/> IType carries ref-struct and
/// backend-backed semantics at use-sites. This test verifies:
/// <list type="number">
///   <item><c>Span.mist</c> loads cleanly through the stdlib pipeline (zero errors).</item>
///   <item><see cref="SpanType"/> carries <c>Kind == TypeKind.Struct</c> (ref struct value type).</item>
///   <item>Mutable and read-only forms are correctly distinguished.</item>
/// </list>
/// </summary>
public sealed class SpanRegistrationTests
{
    /// <summary>
    /// Walks up from the test assembly's output directory until a directory
    /// containing <c>Mist.slnx</c> is found.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root (Mist.slnx) from AppContext.BaseDirectory.");
    }

    [Fact]
    public void SpanMist_LoadsWithoutErrors()
    {
        var repoRoot = FindRepoRoot();
        var stdlibRoot = Path.Combine(repoRoot, "Source", "Standard");
        Assert.True(Directory.Exists(stdlibRoot), $"Stdlib root not found: {stdlibRoot}");

        var spanFile = Path.Combine(stdlibRoot, "Bootstrap", "Span.mist");
        Assert.True(File.Exists(spanFile), $"Span.mist not found: {spanFile}");

        var loaderDiagnostics = new DiagnosticBag();
        var units = StdlibLoader.LoadFrom(stdlibRoot, loaderDiagnostics);
        Assert.Empty(loaderDiagnostics.ToImmutableArray());
        Assert.NotEmpty(units);

        ILexer lexer = new Mist.Lexing.MistLexer();
        IParser parser = new Mist.Parsing.MistParser();
        var pipelineDiagnostics = new DiagnosticBag();

        foreach(var unit in units)
        {
            var tokens = lexer.Lex(unit.Source, pipelineDiagnostics);
            var parseResult = parser.Parse(tokens, pipelineDiagnostics);
            Assert.NotNull(parseResult.Root);
        }

        var errors = pipelineDiagnostics
            .ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        Assert.True(
            errors.Length == 0,
            "Expected zero Error-severity diagnostics loading Span.mist, but got:\n"
                + string.Join("\n", errors.Select(d => $"  [{d.Id.Value}] {d.Message}")));
    }

    [Theory]
    [InlineData(false, "Span<int>")]
    [InlineData(true, "ReadOnlySpan<int>")]
    public void SpanType_CarriesRefStructSemantics(bool isReadOnly, string expectedDisplayName)
    {
        // The dedicated SpanType IType is the carrier of ref-struct semantics
        // (matching the ArrayType precedent for Array<T, Length>).
        var element = new StubElementType();
        var span = new SpanType(element, isReadOnly: isReadOnly);

        // R1.3/R2.3: Kind == TypeKind.Struct (ref struct ⇒ value/struct kind)
        Assert.Equal(TypeKind.Struct, span.Kind);

        // Correct mutable/read-only discrimination
        Assert.Equal(isReadOnly, span.IsReadOnly);

        // DisplayName matches the expected form
        Assert.Equal(expectedDisplayName, span.DisplayName);
    }

    [Fact]
    public void SpanType_KnownLength_FromStackalloc()
    {
        // Simulates what SemanticModelBuilder produces for `stackalloc int[4]`
        var element = new StubElementType();
        var span = new SpanType(element, knownLength: 4L, isReadOnly: false);

        Assert.Equal(TypeKind.Struct, span.Kind);
        Assert.Equal(4L, span.KnownLength);
        Assert.Equal("Span<int, 4>", span.DisplayName);
    }

    [Fact]
    public void SpanType_UnknownLength_FromAlias()
    {
        // Simulates what SemanticModelBuilder produces for Span<T> (= Span<T, unknown>)
        var element = new StubElementType();
        var span = new SpanType(element, isReadOnly: false);

        Assert.Equal(TypeKind.Struct, span.Kind);
        Assert.Null(span.KnownLength);
        Assert.Equal("Span<int>", span.DisplayName);
    }

    /// <summary>
    /// Minimal element-type stand-in with a stable DisplayName.
    /// </summary>
    private sealed class StubElementType : IType
    {
        public string DisplayName => "int";
        public TypeKind Kind => TypeKind.Struct;
    }
}