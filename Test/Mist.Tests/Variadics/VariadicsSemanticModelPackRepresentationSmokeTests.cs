// Feature: variadics - Smoke test for the classic (legacy Mist.Semantics) pack
// representation (Task 9.1). Requirements: 3.1, 3.2, 3.3.
//
// Task 9.1 adds the pack type-parameter representation to the LEGACY Mist.Semantics
// model (the classic, emitting pipeline), mirroring the completed ProgramModel track
// (task 4.1). Where the ProgramModel represents a pack with the TypeParameterPackKind
// enum plus FixedPackLength on its TypeParameter, the legacy model records the same
// state as flags on TypeParameterType: IsPack, IsUnbracketedPack, and the fixed-length
// domain PackFixedArity (Type[] -> null, Type[N] -> N).
//
// This drives SemanticModelBuilder entirely in-process (ZERO external process spawns:
// no dotnet run, no compiled DLL) and asserts the pack flags on the declared type's
// TypeParameterType slots:
//   - `params TArgs`            -> Unbracketed pack, unknown length            (3.1)
//   - lone `Type[]:A`           -> Unbracketed pack via inverse sugar          (3.2)
//   - multiple `Type[]:` packs  -> each is a Bracketed pack (none unbracketed) (3.3)
//   - lone `Type[N]:TShapes`    -> Unbracketed pack, PackFixedArity == N
//
// This is the minimal smoke check for the classic-track representation; the exhaustive
// classification/arity unit suite over the legacy model is task 9.5 and the property
// test is task 9.4.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Smoke tests asserting that the classic <see cref="SemanticModelBuilder"/> models a
/// type-parameter pack as a <see cref="TypeParameterType"/> flagged
/// <see cref="TypeParameterType.IsPack"/>, marks the list's single bracket-free pack via
/// <see cref="TypeParameterType.IsUnbracketedPack"/>, and records a fixed-size domain in
/// <see cref="TypeParameterType.PackFixedArity"/> (Requirements 3.1-3.3).
/// </summary>
public sealed class VariadicsSemanticModelPackRepresentationSmokeTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.1: `params TArgs` is the single Unbracketed pack
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParamsPack_IsModeledAsUnbracketedPack_OverLegacyModel()
    {
        var typeParams = TypeParametersOf(BuildModel("public class Bundle<params TArgs> { }"), "Bundle");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(typeParams));
        Assert.Equal("TArgs", pack.Name);
        Assert.True(pack.IsPack);
        Assert.True(pack.IsUnbracketedPack);
        // `params TArgs` is sugar for the unknown-length `Type[]:TArgs`.
        Assert.Null(pack.PackFixedArity);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.2: a lone `Type[]:A` pack is Unbracketed via inverse sugar
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LoneExplicitPack_IsUnbracketedByInverseSugar_OverLegacyModel()
    {
        var typeParams = TypeParametersOf(BuildModel("public class Box<Type[]:A> { }"), "Box");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(typeParams));
        Assert.Equal("A", pack.Name);
        Assert.True(pack.IsPack);
        Assert.True(pack.IsUnbracketedPack);
        Assert.Null(pack.PackFixedArity);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.3: multiple `Type[]:` packs -> each is Bracketed (none unbracketed)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleExplicitPacks_AreBothBracketed_OverLegacyModel()
    {
        var typeParams = TypeParametersOf(BuildModel("public class TwoPacks<Type[]:A, Type[]:B> { }"), "TwoPacks");

        Assert.Equal(2, typeParams.Count);

        var first = Assert.IsType<TypeParameterType>(typeParams[0]);
        Assert.Equal("A", first.Name);
        Assert.True(first.IsPack);
        Assert.False(first.IsUnbracketedPack);

        var second = Assert.IsType<TypeParameterType>(typeParams[1]);
        Assert.Equal("B", second.Name);
        Assert.True(second.IsPack);
        Assert.False(second.IsUnbracketedPack);

        // Requirement 3.3: more than one Type[]: pack is permitted, and neither is
        // promoted to the single bracket-free slot (inverse sugar applies only to a
        // list with exactly one pack).
        Assert.Equal(0, typeParams.Count(p => p is TypeParameterType { IsUnbracketedPack: true }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fixed-size domain Type[N] records its comptime length N (also over a struct)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LoneFixedSizeExplicitPack_RecordsFixedArity_OverLegacyModel()
    {
        var typeParams = TypeParametersOf(BuildModel("public struct ShapePair<Type[2]:TShapes> { }"), "ShapePair");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(typeParams));
        Assert.Equal("TShapes", pack.Name);
        Assert.True(pack.IsPack);
        // Inverse sugar: the lone pack becomes the Unbracketed pack.
        Assert.True(pack.IsUnbracketedPack);
        // Type[2] records its comptime length for the (later) capability ladder.
        Assert.Equal(2, pack.PackFixedArity);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns). The pack
    /// classification runs during model construction, so inspecting the resulting
    /// <see cref="TypeParameterType"/> slots is sufficient for this smoke check.
    /// </summary>
    private static ISemanticModel BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            "Smoke-test source is expected to parse without lex/parse errors.");

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", source))));
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();

        return builder.Build(
            compilation,
            ImmutableArray.Create(parse.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    /// <summary>
    /// Finds the single source-declared class or struct symbol named
    /// <paramref name="typeName"/> and returns its declared type parameters.
    /// </summary>
    private static IReadOnlyList<IType> TypeParametersOf(ISemanticModel model, string typeName)
    {
        var symbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == typeName && s.Kind is SymbolKind.Class or SymbolKind.Struct);
        Assert.NotNull(symbol);

        return symbol!.DeclaredType switch
        {
            ClassType classType => classType.TypeParameters,
            StructType structType => structType.TypeParameters,
            var other => throw new InvalidOperationException(
                $"Type '{typeName}' resolved to an unexpected declared type: {other?.GetType().Name ?? "null"}."),
        };
    }
}