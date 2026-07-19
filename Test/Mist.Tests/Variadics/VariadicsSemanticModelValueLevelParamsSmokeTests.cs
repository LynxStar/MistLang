// Feature: variadics - Smoke test for the classic (legacy Mist.Semantics) homogeneous
// value-level `params T[]` representation (Task 10.3). Requirements: 13.1, 13.4, 13.5.
//
// Task 10.3 adds the homogeneous value-level params representation to the LEGACY
// Mist.Semantics model (the classic, emitting pipeline), mirroring the completed
// ProgramModel track (task 5.3) and sitting alongside the value-pack representation of
// task 10.1. Where a value pack `params TArgs args` becomes the single-slot tuple
// `(TArgs...)`, a Value_Level_Params `params T[] xs` KEEPS its real homogeneous array type
// (an ArrayType) and is merely marked with the Flattened_Call_Convention so the backend
// can later emit a C# `params T[]`. The two are distinguished from a type-parameter-list
// `params` purely by POSITION: a value-side `params` sits in the value parameter list,
// whereas `params TArgs` inside `<>` is a Type_Parameter_Pack.
//
// The homogeneous element type is spelled with a concrete primitive (`int`) because that
// is how value-level params is used in practice (for example the sample's
// `SumAll(params int[] xs)`); a concrete `int[]` resolves to a real ArrayType in the legacy
// model. (A generic `T[]` element is an orthogonal, pre-existing array-resolution concern.)
//
// This drives SemanticModelBuilder entirely in-process (ZERO external process spawns: no
// dotnet run, no compiled DLL) and asserts, over the declared parameter Symbols:
//   - `params int[] xs`   -> real array type (ArrayType), flattened                  (13.1)
//   - it is DISTINCT from a value pack `params TArgs` (a TupleType, not an ArrayType)  (13.1)
//   - a value-level params is exactly ONE slot; `Mixed(int, params int[])` has arity 2
//   - a plain `int[] items` (no `params`) keeps the SAME array type but is NOT flattened
//     (the `params` modifier is the sole marker of the convention)                   (13.4)
//   - a value-level params counts toward the one-`params`-per-list rule: a second
//     value-side `params` alongside it raises MIST_VAR_0001, a single one does not    (13.5)

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Smoke tests asserting that the classic <see cref="SemanticModelBuilder"/> represents a
/// homogeneous value-level <c>params T[] xs</c> as a single parameter whose
/// <see cref="Symbol.DeclaredType"/> is the array type <c>T[]</c>
/// (<see cref="ArrayType"/>) marked with <see cref="Symbol.HasFlattenedCallConvention"/>,
/// keeps it distinct from a value pack <c>params TArgs</c> (a <see cref="TupleType"/>),
/// leaves a plain (non-<c>params</c>) array parameter unflattened, and counts a
/// value-level params toward the one-<c>params</c>-per-value-parameter-list rule
/// (Requirements 13.1, 13.4, 13.5).
/// </summary>
public sealed class VariadicsSemanticModelValueLevelParamsSmokeTests
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    // A single variadic class carrying a pack `TArgs` (for the value-pack contrast) and a
    // set of methods exercising each value-side shape over a concrete `int` element: a
    // value-level params (`params int[]`), a value pack (`params TArgs`), a plain array
    // parameter (`int[]`, no `params`), and a mixed list. The `params` sits in the
    // VALUE-parameter list in every method, distinguishing it from the type-parameter-list
    // pack `TArgs`.
    private const string Source =
        "public class Demo<params TArgs>\n"
        + "{\n"
        + "    public void TakeArray(params int[] xs) { }\n"
        + "\n"
        + "    public void TakePack(params TArgs args) { }\n"
        + "\n"
        + "    public void PlainArray(int[] items) { }\n"
        + "\n"
        + "    public void Mixed(int first, params int[] rest) { }\n"
        + "}";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 13.1: `params int[] xs` keeps its real array type, flattened
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValueLevelParams_KeepsArrayType_MarkedFlattened_OverLegacyModel()
    {
        var (model, _) = Build(Source);
        var xs = Assert.Single(ParametersOf(model, "TakeArray"));
        Assert.Equal("xs", xs.Name);

        // Requirement 13.1: `params int[] xs` is represented with its REAL array type (an
        // ArrayType over the element type) -- NOT the tuple `(TArgs...)` that a value pack
        // uses.
        var array = Assert.IsType<ArrayType>(xs.DeclaredType);
        Assert.NotNull(array.ElementType);

        // ... and its parameter symbol carries the Flattened_Call_Convention so the backend
        // can later emit a C# `params T[]`.
        Assert.True(xs.HasFlattenedCallConvention);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 13.1: the array form is DISTINCT from a value pack (tuple) form
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValueLevelParams_IsDistinctFromValuePack_OverLegacyModel()
    {
        var (model, _) = Build(Source);

        // Value_Level_Params: keeps the homogeneous array type; it is NOT a tuple.
        var xs = Assert.Single(ParametersOf(model, "TakeArray"));
        _ = Assert.IsType<ArrayType>(xs.DeclaredType);
        Assert.False(xs.DeclaredType is TupleType);
        Assert.True(xs.HasFlattenedCallConvention);

        // Value_Pack: `params TArgs args` becomes the single-slot tuple `(TArgs...)`; it is
        // NOT an array. Both spellings share the Flattened_Call_Convention, so only the
        // parameter TYPE (array vs tuple) distinguishes the homogeneous from the pack form.
        var args = Assert.Single(ParametersOf(model, "TakePack"));
        var tuple = Assert.IsType<TupleType>(args.DeclaredType);
        Assert.False(args.DeclaredType is ArrayType);
        var pack = Assert.IsType<TypeParameterType>(Assert.Single(tuple.ElementTypes));
        Assert.True(pack.IsPack);
        Assert.True(args.HasFlattenedCallConvention);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // A plain `int[] items` (no `params`) keeps the same array type but is NOT flattened
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PlainArrayParameter_IsOrdinary_NotFlattened_SameArrayShape_OverLegacyModel()
    {
        var (model, _) = Build(Source);

        var items = Assert.Single(ParametersOf(model, "PlainArray"));

        // A plain `int[] items` parameter (no `params`) is an ordinary array parameter that
        // is NOT flattened -- the `params` modifier is the sole marker of the convention.
        var plainArray = Assert.IsType<ArrayType>(items.DeclaredType);
        Assert.False(items.HasFlattenedCallConvention);

        // It has the SAME array element type as the value-level params form; only the
        // `params` marker distinguishes the flattened-call form.
        var xs = Assert.Single(ParametersOf(model, "TakeArray"));
        var flattenedArray = Assert.IsType<ArrayType>(xs.DeclaredType);
        Assert.Equal(plainArray.ElementType.DisplayName, flattenedArray.ElementType.DisplayName);
        Assert.True(xs.HasFlattenedCallConvention);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Arity: a value-level params counts as exactly one slot (mirrors the value pack)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MixedList_CountsValueLevelParamsAsOneSlot_OverLegacyModel()
    {
        var (model, _) = Build(Source);
        var parameters = ParametersOf(model, "Mixed");

        // `Mixed(int first, params int[] rest)` has arity 2 (the value-level params counts as
        // one slot, mirroring the value pack and the type-parameter side).
        Assert.Equal(2, parameters.Count);

        Assert.Equal("first", parameters[0].Name);
        Assert.False(parameters[0].HasFlattenedCallConvention);

        // The trailing value-level params is the single flattened array slot.
        Assert.Equal("rest", parameters[1].Name);
        _ = Assert.IsType<ArrayType>(parameters[1].DeclaredType);
        Assert.True(parameters[1].HasFlattenedCallConvention);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 13.5: a value-level params counts toward one-`params`-per-list
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoValueLevelParams_ReportMultipleParamsPacks_OverLegacyModel()
    {
        // Two Value_Level_Params in one value-parameter list: both count as a value-side
        // `params`, so the second violates the rule (13.5 -> MIST_VAR_0001).
        var (_, diagnostics) = Build(
            "public class Demo\n"
            + "{\n"
            + "    public void M(params int[] a, params int[] b) { }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void ValuePackPlusValueLevelParams_ReportMultipleParamsPacks_OverLegacyModel()
    {
        // A Value_Pack (`params TArgs`) plus a Value_Level_Params (`params int[]`): both
        // count as a value-side `params`, so the mix violates the rule (13.5 -> MIST_VAR_0001).
        var (_, diagnostics) = Build(
            "public class Demo<params TArgs>\n"
            + "{\n"
            + "    public void M(params TArgs a, params int[] b) { }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void SingleValueLevelParams_DoesNotReport_OverLegacyModel()
    {
        // A single value-level params surrounded by fixed scalar parameters is well-formed:
        // exactly one value-side `params` is the permitted bracket-free slot (13.5).
        var (_, diagnostics) = Build(
            "public class Demo\n"
            + "{\n"
            + "    public void M(int first, params int[] rest) { }\n"
            + "}");

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns), returning the
    /// model together with every construction-stage diagnostic. Parameter type resolution,
    /// the value-level params representation, and the one-value-side-<c>params</c> rule all
    /// run during model construction, so inspecting the declared parameter <see cref="Symbol"/>
    /// instances and the construction diagnostics is sufficient.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) Build(string source)
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

        Assert.NotNull(model);
        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Returns the declared parameter <see cref="Symbol"/> instances (in declaration order)
    /// of the single source method named <paramref name="methodName"/>, read from the
    /// declaring AST node so the assertions inspect exactly what the canonical
    /// <c>RegisterParameters</c> pass produced.
    /// </summary>
    private static IReadOnlyList<Symbol> ParametersOf(ISemanticModel model, string methodName)
    {
        var methodSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == methodName && s.Kind == SymbolKind.Method);
        Assert.NotNull(methodSymbol);

        var paramList = model.GetDeclaringNode(methodSymbol!) is MethodDeclarationNode method
            ? method.Parameters
            : null;

        var result = new List<Symbol>();
        if(paramList is null)
        {
            return result;
        }

        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode && model.GetDeclaredSymbol(paramNode) is Symbol sym)
            {
                result.Add(sym);
            }
        }

        return result;
    }

    private static int CountDiagnostics(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Count(d => d.Id.Value == id);
    }
}