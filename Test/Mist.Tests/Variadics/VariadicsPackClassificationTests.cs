// Feature: variadics - Unit tests for pack classification and arity-in-slots (Task 4.5)
// Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6
//
// These tests drive program-model construction entirely in-process via
// ProgramModelBuilder.Build (parse -> build program model -> inspect the resulting
// TypeParameter.PackKind / FixedPackLength / slot count and the MIST_VAR_0001
// diagnostic collected in the construction DiagnosticBag). Classification and the
// at-most-one-Unbracketed_Pack rule live in TypeParameterPopulator, wired through
// TypeBuilder during Phase 1 of ProgramModelBuilder. There are ZERO external process
// spawns: the builder runs directly and emit is never invoked.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Unit tests for type-parameter pack classification and arity-in-slots
/// (Requirements 3.1-3.6). Each test builds the program model in-process and asserts
/// on the constructed <see cref="TypeParameter"/> slots:
/// <list type="bullet">
///   <item><c>params TArgs</c> is the list's single <c>Unbracketed</c> pack (3.1);</item>
///   <item>a lone <c>Type[]:TArgs</c> pack is <c>Unbracketed</c> by inverse sugar (3.2);</item>
///   <item>non-lone <c>Type[]:</c> packs are <c>Bracketed</c> (3.3);</item>
///   <item>arity is the declared slot count, each pack counting as one slot (3.6);</item>
///   <item>a second <c>params</c> pack raises <c>MIST_VAR_0001</c> (3.4, 3.5).</item>
/// </list>
/// </summary>
public sealed class VariadicsPackClassificationTests : IDisposable
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    private readonly string outputDir;

    public VariadicsPackClassificationTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsPack_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the temporary build directory.
            }
        }

        GC.SuppressFinalize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.1: `params TArgs` classified as the single Unbracketed pack
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParamsPack_IsClassifiedUnbracketed()
    {
        var (model, diagnostics) = BuildModel("public class Bundle<params TArgs> { }");

        var bundle = FindType(model, "Bundle");

        // One declared slot -> arity 1.
        _ = Assert.Single(bundle.TypeParametersList);

        var pack = bundle.TypeParametersList[0];
        Assert.Equal("TArgs", pack.Name);
        Assert.True(pack.IsPack);
        Assert.Equal(TypeParameterPackKind.Unbracketed, pack.PackKind);
        Assert.True(pack.IsUnbracketedPack);
        // `params TArgs` is sugar for the unknown-length `Type[]:TArgs`.
        Assert.Null(pack.FixedPackLength);

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.2: a lone `Type[]:TArgs` pack is Unbracketed via inverse sugar
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LoneExplicitPack_IsUnbracketedByInverseSugar()
    {
        var (model, diagnostics) = BuildModel("public class Box<Type[]:A> { }");

        var box = FindType(model, "Box");

        _ = Assert.Single(box.TypeParametersList);

        var pack = box.TypeParametersList[0];
        Assert.Equal("A", pack.Name);
        Assert.True(pack.IsPack);
        // Inverse sugar: the only pack in the list becomes the Unbracketed pack.
        Assert.Equal(TypeParameterPackKind.Unbracketed, pack.PackKind);
        Assert.True(pack.IsUnbracketedPack);
        Assert.Null(pack.FixedPackLength);

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void LonePackWithRegularSibling_IsUnbracketedByInverseSugar()
    {
        // The pack is the only *pack* in the list (TKey is a regular parameter), so
        // inverse sugar still applies: TArgs is Unbracketed even though it is not the
        // only slot.
        var (model, diagnostics) = BuildModel("public class Pair<TKey, Type[]:TArgs> { }");

        var pair = FindType(model, "Pair");

        Assert.Equal(2, pair.TypeParametersList.Count);

        var key = pair.TypeParametersList[0];
        Assert.Equal("TKey", key.Name);
        Assert.False(key.IsPack);
        Assert.Equal(TypeParameterPackKind.None, key.PackKind);

        var pack = pair.TypeParametersList[1];
        Assert.Equal("TArgs", pack.Name);
        Assert.Equal(TypeParameterPackKind.Unbracketed, pack.PackKind);
        Assert.True(pack.IsUnbracketedPack);

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void LoneFixedSizeExplicitPack_IsUnbracketed_AndRecordsLength()
    {
        // A lone fixed-size `Type[N]:` pack is Unbracketed by inverse sugar and records
        // its comptime length N in FixedPackLength.
        var (model, diagnostics) = BuildModel("public struct ShapePair<Type[2]:TShapes> { }");

        var shapePair = FindType(model, "ShapePair");

        _ = Assert.Single(shapePair.TypeParametersList);

        var pack = shapePair.TypeParametersList[0];
        Assert.Equal("TShapes", pack.Name);
        Assert.True(pack.IsPack);
        Assert.Equal(TypeParameterPackKind.Unbracketed, pack.PackKind);
        Assert.Equal(2L, pack.FixedPackLength);

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.3: multiple `Type[]:` packs -> each non-lone one is Bracketed
    // (this is also the non-error case `Foo<Type[]:A, Type[]:B>`)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleExplicitPacks_AreBracketed_AndDoNotError()
    {
        var (model, diagnostics) = BuildModel("public class TwoPacks<Type[]:A, Type[]:B> { }");

        var twoPacks = FindType(model, "TwoPacks");

        Assert.Equal(2, twoPacks.TypeParametersList.Count);

        var first = twoPacks.TypeParametersList[0];
        Assert.Equal("A", first.Name);
        Assert.True(first.IsPack);
        Assert.Equal(TypeParameterPackKind.Bracketed, first.PackKind);
        Assert.False(first.IsUnbracketedPack);

        var second = twoPacks.TypeParametersList[1];
        Assert.Equal("B", second.Name);
        Assert.True(second.IsPack);
        Assert.Equal(TypeParameterPackKind.Bracketed, second.PackKind);
        Assert.False(second.IsUnbracketedPack);

        // Neither pack is Unbracketed, so there is no more-than-one-params violation.
        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Non-error case: one `params` pack + one `Type[]:` pack
    // (one Unbracketed + one Bracketed, no MIST_VAR_0001)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParamsPackPlusExplicitPack_IsOneUnbracketedOneBracketed_AndDoesNotError()
    {
        var (model, diagnostics) = BuildModel("public class Mixed<params A, Type[]:B> { }");

        var mixed = FindType(model, "Mixed");

        Assert.Equal(2, mixed.TypeParametersList.Count);

        var unbracketed = mixed.TypeParametersList[0];
        Assert.Equal("A", unbracketed.Name);
        Assert.Equal(TypeParameterPackKind.Unbracketed, unbracketed.PackKind);
        Assert.True(unbracketed.IsUnbracketedPack);

        // B is a pack too, so A is not the lone pack; B stays Bracketed.
        var bracketed = mixed.TypeParametersList[1];
        Assert.Equal("B", bracketed.Name);
        Assert.Equal(TypeParameterPackKind.Bracketed, bracketed.PackKind);
        Assert.False(bracketed.IsUnbracketedPack);

        // Exactly one Unbracketed pack -> valid.
        Assert.Equal(1, CountUnbracketedPacks(mixed));
        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.6: arity is the declared slot count, each pack = one slot
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ArityFiveExample_HasArityFive_AndCorrectPerSlotClassification()
    {
        // The design's worked arity-5 example: two bracketed `Type[]:` packs, one
        // `params` pack, and two regular scalar slots.
        var (model, diagnostics) = BuildModel(
            "public class Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes> { }");

        var foo = FindType(model, "Foo");

        // Arity is the number of declared slots (each pack counts as one), so 5.
        Assert.Equal(5, foo.TypeParametersList.Count);

        // The arity-bearing name is built by TypeBuilder.BuildGenericName: 5 slots -> 4 commas.
        Assert.Equal("Foo<,,,,>", foo.Name);

        var tval = foo.TypeParametersList[0];
        Assert.Equal("TVal", tval.Name);
        Assert.False(tval.IsPack);
        Assert.Equal(TypeParameterPackKind.None, tval.PackKind);

        var targs = foo.TypeParametersList[1];
        Assert.Equal("TArgs", targs.Name);
        Assert.Equal(TypeParameterPackKind.Bracketed, targs.PackKind);

        var ctorArgs = foo.TypeParametersList[2];
        Assert.Equal("CtorArgs", ctorArgs.Name);
        Assert.Equal(TypeParameterPackKind.Unbracketed, ctorArgs.PackKind);
        Assert.True(ctorArgs.IsUnbracketedPack);

        var args3 = foo.TypeParametersList[3];
        Assert.Equal("Args3", args3.Name);
        Assert.Equal(TypeParameterPackKind.Bracketed, args3.PackKind);

        var tres = foo.TypeParametersList[4];
        Assert.Equal("TRes", tres.Name);
        Assert.False(tres.IsPack);
        Assert.Equal(TypeParameterPackKind.None, tres.PackKind);

        // Exactly one Unbracketed pack (CtorArgs) -> valid, no diagnostic.
        Assert.Equal(1, CountUnbracketedPacks(foo));
        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 3.4, 3.5: more than one Unbracketed pack -> MIST_VAR_0001
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DuplicateParamsPacks_EmitMistVar0001()
    {
        var (model, diagnostics) = BuildModel("public class Foo<params A, params B> { }");

        var foo = FindType(model, "Foo");

        // Both `params` packs are classified Unbracketed...
        Assert.Equal(2, foo.TypeParametersList.Count);
        Assert.Equal(TypeParameterPackKind.Unbracketed, foo.TypeParametersList[0].PackKind);
        Assert.Equal(TypeParameterPackKind.Unbracketed, foo.TypeParametersList[1].PackKind);
        Assert.Equal(2, CountUnbracketedPacks(foo));

        // ...and the second one triggers exactly one MIST_VAR_0001.
        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));

        var diagnostic = diagnostics.Single(d => d.Id.Value == MultipleParamsPacksId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        // The message names the list kind being validated.
        Assert.Contains("type-parameter", diagnostic.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the program model entirely
    /// in-process, returning the model together with every diagnostic collected
    /// during construction (where MIST_VAR_0001 is reported).
    /// </summary>
    private (IProgramModel Model, ImmutableArray<Diagnostic> BuildDiagnostics) BuildModel(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("test.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var model = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        Assert.NotNull(model);
        return (model!, buildDiagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Finds the single source-declared <see cref="MemberType"/> whose simple name
    /// (the part before any arity-bearing <c>&lt;...&gt;</c> suffix) matches
    /// <paramref name="simpleName"/>.
    /// </summary>
    private static MemberType FindType(IProgramModel model, string simpleName)
    {
        var matches = model.TypesByFQN.Values
            .OfType<MemberType>()
            .Where(t => t.Origin == TypeOrigin.Source && SimpleName(t.Name) == simpleName)
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"Expected exactly one source type named '{simpleName}', found {matches.Count} "
                + $"({string.Join(", ", matches.Select(t => t.Name))}).");
        return matches[0];
    }

    /// <summary>
    /// Strips the arity-bearing angle-bracket suffix from a type name, so
    /// <c>Foo&lt;,,,,&gt;</c> becomes <c>Foo</c>.
    /// </summary>
    private static string SimpleName(string typeName)
    {
        var angle = typeName.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? typeName : typeName[..angle];
    }

    private static int CountUnbracketedPacks(MemberType type)
    {
        return type.TypeParametersList.Count(p => p.IsUnbracketedPack);
    }

    private static int CountDiagnostics(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Count(d => d.Id.Value == id);
    }
}