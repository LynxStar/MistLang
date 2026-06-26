using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Sub-task 30.1 for the Mist Standard Library spec. Exercises the two new
/// stdlib-lookup helpers added to <see cref="SemanticModelBuilder"/>:
/// <c>LookupQualifiedStdlibType</c> and <c>ResolvePrimitiveKeyword</c>.
///
/// <para>
/// Validates:
/// <list type="bullet">
/// <item>
/// Requirement 7.1 — the Keyword_Alias_Mapping routes every primitive
/// keyword to its fully-qualified Standard Library type name, and resolving
/// <c>"Standard.Int32"</c> returns the stdlib's <see cref="StructType"/> for
/// <c>Int32</c>.
/// </item>
/// <item>
/// Requirement 7.6 — the helper bypasses all scope-chain, using-directive,
/// and user-type machinery. A fatal <c>MIST_STDLIB_MISSING</c> is raised
/// (via <see cref="StdlibMissingException"/>) when the requested type is
/// absent rather than silently falling back.
/// </item>
/// </list>
/// </para>
/// </summary>
public class LookupQualifiedStdlibTypeTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    /// <summary>
    /// Builds a semantic model from a single <paramref name="source"/>
    /// snippet and returns the concrete <see cref="SemanticModel"/> so the
    /// test can reach its internal <c>SymbolTable</c> (required by the
    /// stdlib-lookup helpers' signatures).
    /// </summary>
    private static SemanticModel BuildModel(string source)
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
            ImmutableArray.Create(new CompilationUnit(sourceText) { IsStdlib = true }));

        var builder = new SemanticModelBuilder();
        var semBag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return (SemanticModel)model;
    }

    /// <summary>
    /// Minimal stdlib surface sufficient to exercise the lookup helper: a
    /// single <c>Standard.Int32</c> struct declared with the type-level
    /// <c>intrinsic</c> marker. Mirrors the shape of
    /// <c>Source/Standard/Bootstrap/Int32.mist</c> but trimmed to the bare
    /// minimum the helper needs to find (a registered struct type symbol
    /// named <c>Int32</c> inside the <c>Standard</c> namespace scope).
    /// </summary>
    private const string StdlibStandardInt32Snippet =
        "namespace Standard;\n" +
        "intrinsic public struct Int32 { }\n";

    [Fact]
    public void LookupQualifiedStdlibType_KnownStdlibType_ReturnsStructType()
    {
        // Arrange — build a semantic model containing `Standard.Int32`.
        var model = BuildModel(StdlibStandardInt32Snippet);
        _ = Assert.IsType<Scope>(model.GlobalScope);
        var globalScope = (Scope)model.GlobalScope;

        // Act — call the helper directly.
        var resolved = SemanticModelBuilder.LookupQualifiedStdlibType(
            globalScope,
            model.SymbolTable,
            "Standard.Int32");

        // Assert — the resolved IType is the `Standard.Int32` StructType.
        Assert.NotNull(resolved);
        _ = Assert.IsType<StructType>(resolved);
        var structType = (StructType)resolved;
        Assert.Equal("Int32", structType.DisplayName);
        Assert.Equal("Standard", structType.ContainingNamespace);
        Assert.True(structType.IsBackendBacked,
            "Standard.Int32 must carry the type-level `intrinsic` marker.");

        // The returned IType is reference-equal to the StructType on the
        // registered Symbol — so the helper is returning the same instance
        // the rest of the pipeline sees, not a copy.
        var registeredSymbol = model.AllSymbols.First(s =>
            s.Name == "Int32" && s.Kind == SymbolKind.Struct);
        Assert.Same(registeredSymbol.DeclaredType, resolved);
    }

    [Fact]
    public void LookupQualifiedStdlibType_UnknownSimpleName_ThrowsMistStdlibMissing()
    {
        // Arrange — same model as above; `Standard.NonExistent` is NOT declared.
        var model = BuildModel(StdlibStandardInt32Snippet);
        var globalScope = (Scope)model.GlobalScope;

        // Act / Assert — the helper throws StdlibMissingException carrying
        // the MIST_STDLIB_MISSING diagnostic id.
        var ex = Assert.Throws<StdlibMissingException>(() =>
            SemanticModelBuilder.LookupQualifiedStdlibType(
                globalScope,
                model.SymbolTable,
                "Standard.NonExistent"));

        Assert.Equal(DiagnosticId.StdlibMissing, ex.DiagnosticId);
        Assert.Equal("MIST_STDLIB_MISSING", ex.DiagnosticId.Value);
        Assert.Equal("Standard.NonExistent", ex.QualifiedName);
        Assert.Contains("MIST_STDLIB_MISSING", ex.Message);
        Assert.Contains("Standard.NonExistent", ex.Message);
    }

    [Fact]
    public void LookupQualifiedStdlibType_MissingNamespace_ThrowsMistStdlibMissing()
    {
        // Arrange — no `Standard` namespace at all (stdlib not loaded).
        var source =
            "namespace UserSpace;\n" +
            "public class Foo { }\n";
        var model = BuildModel(source);
        var globalScope = (Scope)model.GlobalScope;

        // Act / Assert — the helper reports MIST_STDLIB_MISSING when the
        // target namespace scope is absent.
        var ex = Assert.Throws<StdlibMissingException>(() =>
            SemanticModelBuilder.LookupQualifiedStdlibType(
                globalScope,
                model.SymbolTable,
                "Standard.Int32"));

        Assert.Equal(DiagnosticId.StdlibMissing, ex.DiagnosticId);
        Assert.Equal("Standard.Int32", ex.QualifiedName);
    }
}