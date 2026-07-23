// Feature: variadics, task 10.6: property test for arity-in-slots over the CLASSIC
// (legacy Mist.Semantics) pipeline.
//
// Property 8: Arity is slot count.
// **Validates: Requirements 3.6, 7.2**
//
// For any type-parameter list and any value-parameter list, the declared arity equals
// the number of declared slots, where each pack -- a type-array pack spelled `params`
// or `Type[]:` / `Type[N]:`, or a value pack `params TArgs args` / `params T[] xs` --
// counts as exactly ONE slot, independent of the concrete pack length it later absorbs.
//
// This is the CLASSIC-track counterpart of the ProgramModel-track property test
// (VariadicsAritySlotCountPropertyTests, task 5.6). Where that test drives
// ProgramModelBuilder.Build and reads TypeParametersList.Count / ParametersList.Count,
// this one drives the legacy SemanticModelBuilder.Build -- the default, emitting pipeline
// -- and reads the declared arity back off the legacy semantic model:
//   * a type-parameter list on a type is TypeParameters.Count on the ClassType (the shared
//     ExtractTypeParameters populates it for class/struct/abstract alike);
//   * a type-parameter list on a method is the method Symbol.TypeParameters count;
//   * a value-parameter list is the count of declared parameter Symbols the canonical
//     RegisterParameters pass produced (a value pack `params TArgs` is one tuple slot and
//     a homogeneous `params T[]` is one array slot, mirroring the type side).
// It mirrors the construction/lookup pattern of the classic smoke tests
// (VariadicsSemanticModelValuePackSmokeTests task 10.1,
// VariadicsSemanticModelValueLevelParamsSmokeTests task 10.3, and
// VariadicsSemanticModelPackClassificationTests task 9.5) and generalizes their fixed
// examples into a property over generated slot lists.
//
// Everything runs entirely IN-PROCESS via the shared VariadicsCompilerFixture.Parse helper
// plus SemanticModelBuilder.Build; nothing is emitted or run, so there are ZERO external
// process spawns.
//
// How the expected slot count is known: each scenario is generated from a list of slot
// descriptors whose LENGTH is the intended arity. Every descriptor renders to exactly one
// declared slot -- a regular type parameter (T{i}), a dependent parameter (D{i}:int), a
// bracketed pack (Type[]:P{i}), a fixed-size pack (Type[N]:P{i}), the single unbracketed
// pack (params U{i}), a fixed value parameter (T a{i}), a value pack (params TArgs pk), or
// a homogeneous value-level params (params T[] xs). Each generated list carries AT MOST ONE
// unbracketed pack, so it never trips MIST_VAR_0001, keeping the property focused on
// "arity == slot count". The single pack is placed in leading, middle, and trailing
// positions across the generated space.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
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
/// Which declared list a generated scenario exercises over the classic model: a
/// type-parameter list on a class, a type-parameter list on a method, or a value-parameter
/// list on a method.
/// </summary>
public enum SemanticArityListKind
{
    TypeParamsOnClass,
    TypeParamsOnMethod,
    ValueParamsOnMethod,
}

/// <summary>
/// A generated arity scenario for the classic pipeline: a complete, parseable Mist
/// declaration whose declared list has a KNOWN slot count (<see cref="ExpectedArity"/>),
/// together with which list to inspect (<see cref="Kind"/>). The slot count is known by
/// construction because every generated descriptor renders to exactly one declared slot.
/// </summary>
public sealed record SemanticAritySlotScenario(
    string Source,
    int ExpectedArity,
    SemanticArityListKind Kind,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property test for <b>Property 8 (Arity is slot count)</b> over the classic (legacy
/// <c>Mist.Semantics</c>) pipeline. Builds the semantic model in-process via
/// <see cref="SemanticModelBuilder"/> and asserts that the declared arity of a
/// type-parameter or value-parameter list equals the number of declared slots, counting
/// each pack as exactly one slot. <b>Validates: Requirements 3.6, 7.2.</b>
/// </summary>
public sealed class VariadicsSemanticModelAritySlotCountPropertyTests
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    private const int UnbracketedPackKind = 4;

    // Parseable primitive types drawn for fixed value parameters and value-level params,
    // mirroring the pool used by MistVariadicsGenerator and the ProgramModel-track arity
    // test. Each resolves in the legacy model without the stdlib (via the legacy primitive
    // fallback), but the arity assertion only counts declared slots, so type resolution
    // never affects the result.
    private static readonly string[] ElementTypes =
        ["int", "string", "bool", "double", "long", "char", "float", "byte"];

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: declared arity == number of declared slots (each pack = one slot).
    // **Validates: Requirements 3.6, 7.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsSemanticModelAritySlotCountPropertyTests) }, MaxTest = 200)]
    public bool DeclaredArity_EqualsDeclaredSlotCount_OverLegacyModel(SemanticAritySlotScenario scenario)
    {
        var (model, diagnostics) = BuildModel(scenario.Source);

        // Every generated list has at most one Unbracketed_Pack, so the
        // one-`params`-per-list rule must not fire; a MIST_VAR_0001 here would signal a
        // malformed scenario rather than a genuine arity result.
        Assert.Equal(0, diagnostics.Count(d => d.Id.Value == MultipleParamsPacksId));

        var actualArity = scenario.Kind switch
        {
            SemanticArityListKind.TypeParamsOnClass =>
                TypeParameterCount(model, "ClassUnderTest"),
            SemanticArityListKind.TypeParamsOnMethod =>
                MethodTypeParameterCount(model, "M"),
            SemanticArityListKind.ValueParamsOnMethod =>
                ValueParameterCount(model, "M"),
            _ => throw new InvalidOperationException($"Unhandled kind: {scenario.Kind}"),
        };

        Assert.Equal(scenario.ExpectedArity, actualArity);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<SemanticAritySlotScenario> AritySlotScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<SemanticAritySlotScenario> GenScenario()
    {
        return Gen.OneOf(
            GenTypeParamsOnClass(),
            GenTypeParamsOnMethod(),
            GenValueParamsOnMethod());
    }

    // ─── Type-parameter list scenarios (class and method) ────────────────────

    /// <summary>
    /// Generates a rendered type-parameter slot list together with its intended slot count.
    /// Slots are drawn from regular, dependent, bracketed-pack, and fixed-size-pack
    /// descriptors; an optional single unbracketed <c>params</c> pack replaces one slot at a
    /// random position (leading/middle/trailing). At most one unbracketed pack is ever
    /// produced, so the list is always a valid single-<c>params</c> list.
    /// </summary>
    private static Gen<(string Slots, int Count)> GenTypeParamSlots()
    {
        return
            from n in Gen.Choose(1, 6)
            from baseKinds in Gen.ArrayOf(Gen.Elements(0, 1, 2, 3), n)
            from sizes in Gen.ArrayOf(Gen.Choose(1, 4), n)
            from hasParams in Gen.Elements(true, false)
            from paramsPos in Gen.Choose(0, n - 1)
            let kinds = WithOptionalParamsPack(baseKinds, hasParams, paramsPos)
            let slots = RenderTypeSlots(kinds, sizes)
            select (Slots: slots, Count: n);
    }

    private static Gen<SemanticAritySlotScenario> GenTypeParamsOnClass()
    {
        return
            from slots in GenTypeParamSlots()
            let source = $"public class ClassUnderTest<{slots.Slots}> {{ }}"
            select new SemanticAritySlotScenario(
                source, slots.Count, SemanticArityListKind.TypeParamsOnClass,
                $"type-params-on-class (arity {slots.Count}): {source}");
    }

    private static Gen<SemanticAritySlotScenario> GenTypeParamsOnMethod()
    {
        return
            from slots in GenTypeParamSlots()
            let source = $"public class HostUnderTest {{ public void M<{slots.Slots}>() {{ }} }}"
            select new SemanticAritySlotScenario(
                source, slots.Count, SemanticArityListKind.TypeParamsOnMethod,
                $"type-params-on-method (arity {slots.Count}): {source}");
    }

    // ─── Value-parameter list scenario (method) ──────────────────────────────

    /// <summary>
    /// Generates a value-parameter list on a method: zero or more fixed scalar parameters
    /// plus at most one value-side <c>params</c> parameter (a value pack
    /// <c>params TArgs pk</c> or a homogeneous value-level <c>params T[] xs</c>). The value
    /// pack is placed in a leading/middle/trailing position; the value-level params is
    /// trailing (it collects the trailing arguments). The intended slot count is the number
    /// of fixed parameters plus one when a <c>params</c> parameter is present.
    /// </summary>
    private static Gen<SemanticAritySlotScenario> GenValueParamsOnMethod()
    {
        return
            from fixedCount in Gen.Choose(0, 5)
            from mode in Gen.Elements(0, 1, 2)
            from fixedTypes in Gen.ArrayOf(Gen.Elements(ElementTypes), fixedCount)
            from valuePos in Gen.Choose(0, fixedCount)
            from elemType in Gen.Elements(ElementTypes)
            let built = RenderValueParams(fixedCount, mode, fixedTypes, valuePos, elemType)
            select new SemanticAritySlotScenario(
                built.Source, built.Count, SemanticArityListKind.ValueParamsOnMethod,
                $"value-params-on-method (arity {built.Count}): {built.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Rendering helpers (the intended slot count is known by construction)
    // ═══════════════════════════════════════════════════════════════════════

    private static int[] WithOptionalParamsPack(int[] baseKinds, bool hasParams, int paramsPos)
    {
        var kinds = (int[])baseKinds.Clone();
        if(hasParams)
        {
            kinds[paramsPos] = UnbracketedPackKind;
        }

        return kinds;
    }

    private static string RenderTypeSlots(int[] kinds, int[] sizes)
    {
        var parts = new List<string>(kinds.Length);
        for(var i = 0; i < kinds.Length; i++)
        {
            var slot = kinds[i] switch
            {
                0 => $"T{i}",
                1 => $"D{i}:int",
                2 => $"Type[]:P{i}",
                3 => $"Type[{sizes[i]}]:P{i}",
                UnbracketedPackKind => $"params U{i}",
                _ => throw new InvalidOperationException($"Unhandled slot kind: {kinds[i]}"),
            };
            parts.Add(slot);
        }

        return string.Join(", ", parts);
    }

    private static (string Source, int Count) RenderValueParams(
        int fixedCount, int mode, string[] fixedTypes, int valuePos, string elemType)
    {
        var paramList = new List<string>(fixedCount + 1);
        for(var i = 0; i < fixedCount; i++)
        {
            paramList.Add($"{fixedTypes[i]} a{i}");
        }

        var typeParamsClause = string.Empty;
        var count = fixedCount;

        if(mode == 1)
        {
            // Value pack: exactly one (TArgs...) slot introduced by `params`, placed in a
            // leading/middle/trailing position. The pack is declared on the enclosing class
            // so the scenario relies only on the settled value-pack representation.
            typeParamsClause = "<params TArgs>";
            paramList.Insert(valuePos, "params TArgs pk");
            count = fixedCount + 1;
        }
        else if(mode == 2)
        {
            // Homogeneous value-level params: exactly one T[] slot introduced by `params`,
            // trailing (it collects the trailing arguments).
            paramList.Add($"params {elemType}[] xs");
            count = fixedCount + 1;
        }

        var valueParams = string.Join(", ", paramList);
        var source =
            $"public class HostUnderTest{typeParamsClause} {{ public void M({valueParams}) {{ }} }}";
        return (source, count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // In-process legacy semantic-model construction and lookup helpers
    // (mirrors the task 9.5 / 10.1 / 10.3 pattern; ZERO external process spawns).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns), returning the
    /// model together with every construction-stage diagnostic. Pack classification, the
    /// one-Unbracketed_Pack rule, and arity are all decided during model construction, so
    /// inspecting the resulting slot counts and the construction diagnostics is sufficient.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Generated source is expected to parse without lex/parse errors: {source}");

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
    /// Returns the declared type-parameter count of the source-declared class, struct, or
    /// abstract type named <paramref name="typeName"/> (each pack counts as one slot).
    /// </summary>
    private static int TypeParameterCount(ISemanticModel model, string typeName)
    {
        var symbol = FindType(model, typeName);

        var typeParameters = symbol.DeclaredType switch
        {
            ClassType classType => classType.TypeParameters,
            StructType structType => structType.TypeParameters,
            AbstractType abstractType => abstractType.TypeParameters,
            var other => throw new InvalidOperationException(
                $"Type '{typeName}' resolved to an unexpected declared type: {other?.GetType().Name ?? "null"}."),
        };

        return typeParameters.Count;
    }

    /// <summary>
    /// Returns the declared type-parameter count of the method named
    /// <paramref name="methodName"/>. The method's type parameters are stored on the method
    /// <see cref="Symbol"/> by <c>RegisterMethodTypeParameters</c>, which shares
    /// <c>ExtractTypeParameters</c> with the type-level path.
    /// </summary>
    private static int MethodTypeParameterCount(ISemanticModel model, string methodName)
    {
        var method = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == methodName && s.Kind == SymbolKind.Method);
        Assert.NotNull(method);

        return method!.TypeParameters.Count;
    }

    /// <summary>
    /// Returns the number of declared parameter <see cref="Symbol"/> slots of the method
    /// named <paramref name="methodName"/>, read from the declaring AST node so the count
    /// reflects exactly what the canonical <c>RegisterParameters</c> pass produced (a value
    /// pack or a homogeneous value-level params each count as one slot).
    /// </summary>
    private static int ValueParameterCount(ISemanticModel model, string methodName)
    {
        var methodSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == methodName && s.Kind == SymbolKind.Method);
        Assert.NotNull(methodSymbol);

        var paramList = model.GetDeclaringNode(methodSymbol!) is MethodDeclarationNode method
            ? method.Parameters
            : null;

        if(paramList is null)
        {
            return 0;
        }

        var count = 0;
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode && model.GetDeclaredSymbol(paramNode) is Symbol)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Finds the single source-declared class, struct, or abstract type symbol named
    /// <paramref name="typeName"/>.
    /// </summary>
    private static ISymbol FindType(ISemanticModel model, string typeName)
    {
        var symbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == typeName && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract);
        Assert.NotNull(symbol);

        return symbol!;
    }
}