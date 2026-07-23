// Feature: variadics - Smoke test for the classic (legacy Mist.Semantics) enforcement
// of one Unbracketed_Pack per type-parameter list and slot-counted type arity (Task 9.2).
// Requirements: 3.4, 3.5, 3.6.
//
// Task 9.2 rides on top of the pack representation added by task 9.1 and completes, over
// the LEGACY Mist.Semantics model, the two rules Requirement 3 attaches to a
// type-parameter list:
//   * MIST_VAR_0001 when a list declares more than one Unbracketed_Pack (3.4, 3.5), and
//   * arity computed as the count of declared slots, each pack counting as exactly one
//     regardless of how many types it later absorbs (3.6).
//
// Both rules live in SemanticModelBuilder.ExtractTypeParameters. A `params` pack is always
// Unbracketed; a lone explicit `Type[]:` pack is Unbracketed by inverse sugar; any other
// explicit pack stays Bracketed. Because a lone explicit pack can never be the *second*
// unbracketed pack, "more than one Unbracketed_Pack" is exactly "more than one `params`
// pack" -- which is what the builder checks. Arity falls out of the representation for
// free: each declared node (pack or not) contributes exactly one TypeParameterType slot,
// and the legacy model derives generic arity from TypeParameters.Count.
//
// This drives SemanticModelBuilder entirely in-process (ZERO external process spawns: no
// dotnet run, no compiled DLL). The exhaustive classification/arity unit suite over the
// legacy model is task 9.5 and the property test is task 9.4; this is the minimal smoke
// check for task 9.2.

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
/// Smoke tests asserting that the classic <see cref="SemanticModelBuilder"/> reports
/// <c>MIST_VAR_0001</c> when a type-parameter list declares more than one
/// Unbracketed_Pack (Requirements 3.4, 3.5) and counts type arity as the number of
/// declared slots, each pack counting as exactly one (Requirement 3.6).
/// </summary>
public sealed class VariadicsSemanticModelPackEnforcementSmokeTests
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 3.4 / 3.5: at most one Unbracketed_Pack per type-parameter list
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoParamsPacks_ReportMultipleParamsPacks_OverLegacyModel()
    {
        // Two `params` packs are two Unbracketed packs -> the rule fires (3.4, 3.5).
        var diagnostics = BuildDiagnostics("public class Foo<params A, params B> { }");

        Assert.Contains(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void TwoExplicitPacks_DoNotReportMultipleParamsPacks_OverLegacyModel()
    {
        // Two explicit `Type[]:` packs are both Bracketed (inverse sugar promotes a pack
        // only when it is the list's lone pack), so there is no *second* Unbracketed pack
        // and the rule must not fire.
        var diagnostics = BuildDiagnostics("public class Foo<Type[]:A, Type[]:B> { }");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void OneParamsAndOneExplicitPack_DoNotReportMultipleParamsPacks_OverLegacyModel()
    {
        // `params A` is the single Unbracketed pack; the explicit `Type[]:B` stays
        // Bracketed. Exactly one Unbracketed pack -> the rule must not fire.
        var diagnostics = BuildDiagnostics("public class Foo<params A, Type[]:B> { }");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void SingleParamsPack_DoesNotReportMultipleParamsPacks_OverLegacyModel()
    {
        // A single `params` pack is well-formed.
        var diagnostics = BuildDiagnostics("public class Foo<params TArgs> { }");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.6: arity is the count of declared slots; each pack is one slot
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MixedSlotList_CountsEachPackAsExactlyOneSlot_OverLegacyModel()
    {
        // The design's arity-5 example: two fixed scalars, two Bracketed `Type[]:` packs,
        // and one Unbracketed `params` pack. Each pack occupies exactly one slot
        // regardless of how many types it later absorbs, so arity is 5 -- and with only
        // one Unbracketed pack there is no MIST_VAR_0001.
        var (model, diagnostics) = BuildModel(
            "public class Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes> { }");

        var typeParams = TypeParametersOf(model, "Foo");
        Assert.Equal(5, typeParams.Count);

        // Exactly one slot is the single bracket-free (Unbracketed) pack.
        Assert.Equal(1, typeParams.Count(p => p is TypeParameterType { IsUnbracketedPack: true }));
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void FuncLikeVariadicFamily_HasArityTwo_OverLegacyModel()
    {
        // The Func/Action bootstrap collapses to one `Func<params TArgs, TResult>`. Its
        // arity is 2 -- the pack slot plus the trailing result slot -- not "pack length + 1".
        var (model, _) = BuildModel("public class Func<params TArgs, TResult> { }");

        var typeParams = TypeParametersOf(model, "Func");
        Assert.Equal(2, typeParams.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns), returning the
    /// model together with every construction-stage diagnostic. Pack classification, the
    /// at-most-one-Unbracketed_Pack rule, and arity are all decided during model
    /// construction, so inspecting the result and the construction diagnostics is
    /// sufficient for this smoke check.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
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

        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parse.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Builds the model and returns only the construction-stage diagnostics.
    /// </summary>
    private static ImmutableArray<Diagnostic> BuildDiagnostics(string source)
    {
        return BuildModel(source).Diagnostics;
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