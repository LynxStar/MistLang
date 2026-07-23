// Feature: variadics - Smoke test for the classic (legacy Mist.Semantics) value-pack
// parameter representation (Task 10.1). Requirements: 7.1, 7.2, 7.7.
//
// Task 10.1 adds the value-side pack representation to the LEGACY Mist.Semantics model
// (the classic, emitting pipeline), mirroring the completed ProgramModel track
// (task 5.1). Where the ProgramModel records the value pack as a Parameter whose Type is
// the tuple (TArgs...) with a HasFlattenedCallConvention flag, the legacy model records
// the same state on the parameter Symbol: DeclaredType is a TupleType carrying the single
// pack element, and Symbol.HasFlattenedCallConvention marks the Flattened_Call_Convention.
//
// This drives SemanticModelBuilder entirely in-process (ZERO external process spawns: no
// dotnet run, no compiled DLL) and asserts, over the declared parameter Symbols:
//   - `params TArgs args`            -> single-slot tuple (TArgs...), flattened          (7.1)
//   - a value pack is exactly ONE slot; `Mixed(int, params TArgs, bool)` has arity 3     (7.2)
//   - a plain `(TArgs...) whole`     -> same single-slot tuple, but NOT flattened        (7.7)
//
// This is the minimal smoke check for the classic-track value-side representation; the
// exhaustive value-side unit suite over the legacy model is task 10.9.

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
/// Smoke tests asserting that the classic <see cref="SemanticModelBuilder"/> gives a
/// value pack <c>params TArgs args</c> the single-slot tuple type <c>(TArgs...)</c> and
/// marks its parameter <see cref="Symbol"/> with
/// <see cref="Symbol.HasFlattenedCallConvention"/>, counts a value pack as exactly one
/// parameter slot, and represents a plain <c>(TArgs...) args</c> parameter as an ordinary
/// (unflattened) single-tuple parameter (Requirements 7.1, 7.2, 7.7).
/// </summary>
public sealed class VariadicsSemanticModelValuePackSmokeTests
{
    // A single variadic class exercising all three shapes over the legacy model: a value
    // pack parameter, a plain tuple parameter, and a mixed value-parameter list.
    private const string Source =
        "public class Bundle<params TArgs>\n"
        + "{\n"
        + "    public void TakePack(params TArgs args) { }\n"
        + "\n"
        + "    public void TakeTuple((TArgs...) whole) { }\n"
        + "\n"
        + "    public void Mixed(int number, params TArgs args, bool enabled) { }\n"
        + "}";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.1 / 7.2: `params TArgs args` is a single flattened (TArgs...) slot
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValuePack_IsSingleTupleSlot_MarkedFlattened_OverLegacyModel()
    {
        var parameters = ParametersOf(BuildModel(Source), "TakePack");

        // Requirement 7.2: a value pack is exactly ONE parameter slot.
        var args = Assert.Single(parameters);
        Assert.Equal("args", args.Name);

        // Requirement 7.1: `params TArgs args` has the single-slot tuple type (TArgs...)
        // whose sole element is the declared pack type parameter.
        var tuple = Assert.IsType<TupleType>(args.DeclaredType);
        var pack = Assert.IsType<TypeParameterType>(Assert.Single(tuple.ElementTypes));
        Assert.Equal("TArgs", pack.Name);
        Assert.True(pack.IsPack);

        // ... and its parameter symbol carries the Flattened_Call_Convention.
        Assert.True(args.HasFlattenedCallConvention);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.7: a plain `(TArgs...) whole` is an ordinary, UNflattened tuple slot
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PlainTupleParameter_IsOrdinary_NotFlattened_OverLegacyModel()
    {
        var model = BuildModel(Source);

        var whole = Assert.Single(ParametersOf(model, "TakeTuple"));

        // Requirement 7.7: a plain `(TArgs...) whole` parameter (no `params`) is an ordinary
        // single-tuple parameter that is NOT flattened.
        var tuple = Assert.IsType<TupleType>(whole.DeclaredType);
        var pack = Assert.IsType<TypeParameterType>(Assert.Single(tuple.ElementTypes));
        Assert.True(pack.IsPack);
        Assert.False(whole.HasFlattenedCallConvention);

        // Both the value pack and the plain tuple parameter resolve to a single-slot tuple
        // over the SAME declared pack type parameter; only the `params` marker distinguishes
        // them (the flattened-call convention).
        var packArgs = Assert.Single(ParametersOf(model, "TakePack"));
        var packTuple = Assert.IsType<TupleType>(packArgs.DeclaredType);
        Assert.Same(packTuple.ElementTypes[0], tuple.ElementTypes[0]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.2: arity counts a value pack as one slot (mirrors the type side)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MixedList_CountsValuePackAsOneSlot_OverLegacyModel()
    {
        var parameters = ParametersOf(BuildModel(Source), "Mixed");

        // Requirement 7.2: `Mixed(int number, params TArgs args, bool enabled)` has arity 3
        // (the value pack counts as one slot, mirroring the type-parameter side where
        // `class Foo<TValue, params TArgs, TResult>` has arity 3).
        Assert.Equal(3, parameters.Count);

        Assert.Equal("number", parameters[0].Name);
        Assert.False(parameters[0].HasFlattenedCallConvention);

        // The middle value pack is the single flattened tuple slot.
        Assert.Equal("args", parameters[1].Name);
        _ = Assert.IsType<TupleType>(parameters[1].DeclaredType);
        Assert.True(parameters[1].HasFlattenedCallConvention);

        Assert.Equal("enabled", parameters[2].Name);
        Assert.False(parameters[2].HasFlattenedCallConvention);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns). Parameter type
    /// resolution and the value-pack representation run during model construction, so
    /// inspecting the declared parameter <see cref="Symbol"/> instances is sufficient.
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
}