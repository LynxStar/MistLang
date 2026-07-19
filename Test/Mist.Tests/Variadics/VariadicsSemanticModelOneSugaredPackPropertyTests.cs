// Feature: variadics, task 10.7: property test for one sugared pack per list over the
// CLASSIC (legacy Mist.Semantics) pipeline.
//
// Property 9: One sugared pack per list.
// **Validates: Requirements 3.4, 3.5, 7.8, 7.9, 12.4, 12.5, 13.5**
//
// For any type-parameter or value-parameter list, a list containing exactly one sugared
// `params` Unbracketed_Pack is accepted, and a list containing two or more raises
// MIST_VAR_0001. This holds uniformly for type-parameter lists (including method
// type-parameter lists) and value-parameter lists, counting a value pack
// `params TArgs args` and a homogeneous `params T[]` alike as one `params`.
//
// This is the CLASSIC-track counterpart of the ProgramModel-track property test
// (OneSugaredPackPerListPropertyTests, task 5.7). Where that test drives
// ProgramModelBuilder.Build and reads TypeParametersList[..].IsUnbracketedPack, this one
// drives the legacy SemanticModelBuilder.Build -- the default, emitting pipeline -- and
// reads the MIST_VAR_0001 diagnostics plus the classic type-parameter classification back
// off the legacy semantic model:
//   * a type-parameter list on a class is ClassType.TypeParameters;
//   * a type-parameter list on a method is the method Symbol.TypeParameters;
//   * a value-parameter list is characterized entirely by the MIST_VAR_0001 result.
// It mirrors the construction/lookup pattern of the classic arity property test
// (VariadicsSemanticModelAritySlotCountPropertyTests, task 10.6) and the MIST_VAR_0001
// assertions of the classic smoke tests (VariadicsSemanticModelValueParamsEnforcementSmokeTests
// task 10.2 and VariadicsSemanticModelDeclarationKindPackTests task 10.5).
//
// Everything runs entirely IN-PROCESS via the shared VariadicsCompilerFixture.Parse helper
// plus SemanticModelBuilder.Build; nothing is emitted or run, so there are ZERO external
// process spawns.
//
// -- How the "sugared params count" is kept aligned with the model's classification --
// The classic enforcement (SemanticModelBuilder.ExtractTypeParameters for the type side,
// task 9.2; SemanticModelBuilder.RegisterParameters / EnforceSingleValueParams for the
// value side, task 10.2) reports MIST_VAR_0001 when a list has more than one
// Unbracketed_Pack (type side) or more than one value-side `params` parameter (value side).
// On the type side a `params` pack is ALWAYS Unbracketed, while a `Type[]:` pack is
// Unbracketed only when it is the lone pack in the list (inverse sugar) and otherwise stays
// Bracketed. Every generated list here carries at least one `params` pack, so any `Type[]:`
// filler is never the lone pack and therefore stays Bracketed -- i.e. the generator's
// "sugared params count" equals the number of Unbracketed packs the model classifies, by
// construction. Bracketed `Type[]:` packs, fixed-size `Type[N]:` packs, regular/dependent
// type parameters, fixed value parameters, and explicit plain `(TArgs...)` tuple parameters
// are all non-counting filler. The property cross-checks the classic model's own
// classification against the generated count (type side) so it can never silently drift
// from the enforcement it validates.
//
// The value side is characterized entirely by the MIST_VAR_0001 result: the enforcement
// counts the `params` modifier on the parameter node itself, independent of type
// resolution, so both a Value_Pack (`params TArgs args`) and a homogeneous value-level
// `params T[]` count alike -- exactly as the classic smoke test proves.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Which declared list a generated scenario exercises over the classic model: a
/// type-parameter list on a class, a type-parameter list on a method, or a value-parameter
/// list on a method. Property 9 must hold uniformly across all three.
/// </summary>
public enum SemanticPackListKind
{
    TypeParamsOnClass,
    TypeParamsOnMethod,
    ValueParamsOnMethod,
}

/// <summary>
/// A generated one-sugared-pack scenario for the classic pipeline: a complete, parseable
/// Mist declaration whose declared list contains a KNOWN number of sugared <c>params</c>
/// packs (<see cref="SugaredParamsCount"/>), together with which list to inspect
/// (<see cref="Kind"/>). A count of 1 is the accepted case; a count of 2 or more is the
/// MIST_VAR_0001 violation case. The count is known by construction because every sugared
/// slot is rendered with the <c>params</c> keyword and all other slots are non-counting
/// filler.
/// </summary>
public sealed record SemanticOneSugaredPackScenario(
    string Source,
    int SugaredParamsCount,
    SemanticPackListKind Kind,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property test for <b>Property 9 (One sugared pack per list)</b> over the classic (legacy
/// <c>Mist.Semantics</c>) pipeline. Builds the semantic model in-process via
/// <see cref="SemanticModelBuilder"/> and asserts that a list with exactly one sugared
/// <c>params</c> pack is accepted (no <c>MIST_VAR_0001</c>) while a list with two or more
/// raises exactly one <c>MIST_VAR_0001</c>, uniformly across class type-parameter lists,
/// method type-parameter lists, and value-parameter lists, counting a value pack
/// (<c>params TArgs args</c>) and a homogeneous value-level <c>params T[]</c> alike.
/// <b>Validates: Requirements 3.4, 3.5, 7.8, 7.9, 12.4, 12.5, 13.5.</b>
/// </summary>
public sealed class VariadicsSemanticModelOneSugaredPackPropertyTests
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    // Parseable primitive types drawn for value-level `params T[]` and fixed value
    // parameters, mirroring the pool used by the sibling ProgramModel-track property test
    // (OneSugaredPackPerListPropertyTests, task 5.7). Each resolves in the legacy model
    // without the stdlib, but the MIST_VAR_0001 count is purely syntactic, so type
    // resolution never affects the result.
    private static readonly string[] ElementTypes =
        ["int", "string", "bool", "double", "long", "char"];

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: exactly one sugared `params` pack is accepted; two or more raise a
    // single MIST_VAR_0001. Holds for class type-parameter lists, method type-parameter
    // lists, and value-parameter lists (value packs and homogeneous `params T[]` alike),
    // over the classic (legacy Mist.Semantics) pipeline.
    //
    // **Validates: Requirements 3.4, 3.5, 7.8, 7.9, 12.4, 12.5, 13.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsSemanticModelOneSugaredPackPropertyTests) }, MaxTest = 200)]
    public bool OneSugaredPack_Accepted_TwoOrMore_RaiseMistVar0001_OverLegacyModel(
        SemanticOneSugaredPackScenario scenario)
    {
        var (model, diagnostics) = BuildModel(scenario.Source);

        var violationCount = diagnostics.Count(d => d.Id.Value == MultipleParamsPacksId);

        if(scenario.SugaredParamsCount >= 2)
        {
            // Two or more sugared `params` packs -> exactly one MIST_VAR_0001, reported
            // once at the second sugared pack (not once per surplus).
            Assert.Equal(1, violationCount);
        }
        else
        {
            // Exactly one sugared `params` pack -> accepted, no MIST_VAR_0001.
            Assert.Equal(0, violationCount);
        }

        // On the type side, additionally confirm the classic model classified exactly
        // SugaredParamsCount slots as the Unbracketed (`params`) pack. This pins down that a
        // bracketed `Type[]:` filler is NOT counted (it is never the lone pack, so it stays
        // Bracketed) and that the generator's sugared count matches the model's own
        // classification. The classification is purely syntactic (no stdlib needed), so it is
        // exact here.
        //
        // The value side is characterized entirely by the MIST_VAR_0001 result above: the
        // classic enforcement (RegisterParameters / EnforceSingleValueParams) counts the
        // `params` modifier on the parameter node itself, independent of type resolution, so
        // a homogeneous value-level `params T[]` counts even before its array target is
        // resolved -- exactly as VariadicsSemanticModelValueParamsEnforcementSmokeTests proves.
        if(scenario.Kind != SemanticPackListKind.ValueParamsOnMethod)
        {
            Assert.Equal(scenario.SugaredParamsCount, CountUnbracketedTypePacks(model, scenario.Kind));
        }

        return true;
    }

    /// <summary>
    /// Reads back how many type-parameter slots the built classic model classified as the
    /// Unbracketed (<c>params</c>) pack for the scenario's list. A <c>params</c> pack is
    /// always Unbracketed, and because every generated list carries a <c>params</c> pack,
    /// each <c>Type[]:</c> filler is never the lone pack and stays Bracketed -- so this count
    /// equals the generated sugared count. Applies to the two type-list kinds only.
    /// </summary>
    private static int CountUnbracketedTypePacks(ISemanticModel model, SemanticPackListKind kind)
    {
        var typeParameters = kind switch
        {
            SemanticPackListKind.TypeParamsOnClass =>
                ClassTypeParameters(model, "ClassUnderTest"),
            SemanticPackListKind.TypeParamsOnMethod =>
                MethodTypeParameters(model, "M"),
            _ => throw new InvalidOperationException($"Unhandled type-list kind: {kind}"),
        };

        return typeParameters.Count(p => p is TypeParameterType { IsUnbracketedPack: true });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<SemanticOneSugaredPackScenario> OneSugaredPackScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<SemanticOneSugaredPackScenario> GenScenario()
    {
        return Gen.OneOf(
            GenTypeParamsOnClass(),
            GenTypeParamsOnMethod(),
            GenValueParamsOnMethod());
    }

    /// <summary>
    /// The number of sugared <c>params</c> packs to place in a list: exactly one (the
    /// accepted case) or two-to-four (the MIST_VAR_0001 violation case), split roughly
    /// 50/50 so both branches of Property 9 get ample coverage.
    /// </summary>
    private static Gen<int> GenSugaredCount()
    {
        return Gen.OneOf(Gen.Choose(1, 1), Gen.Choose(2, 4));
    }

    // ─── Type-parameter list scenarios (class and method) ────────────────────

    /// <summary>
    /// A single non-counting type-parameter filler slot: a kind selector (0 = regular
    /// <c>T</c>, 1 = dependent <c>D:int</c>, 2 = bracketed <c>Type[]:B</c>, 3 = fixed-size
    /// <c>Type[N]:F</c>) plus the fixed-size length used only by kind 3.
    /// </summary>
    private static Gen<(int Kind, int Size)> GenTypeFiller()
    {
        return
            from kind in Gen.Choose(0, 3)
            from size in Gen.Choose(1, 4)
            select (kind, size);
    }

    /// <summary>
    /// Generates a rendered type-parameter slot list together with its sugared
    /// <c>params</c> count. Exactly <c>SugaredCount</c> sugared <c>params</c> packs are
    /// placed among zero-to-three non-counting filler slots, then the whole list is
    /// shuffled so the sugared packs land in leading/middle/trailing positions.
    /// </summary>
    private static Gen<(string Slots, int SugaredCount)> GenTypeParamSlots()
    {
        return
            from sugaredCount in GenSugaredCount()
            from fillerCount in Gen.Choose(0, 3)
            from fillers in Gen.ArrayOf(GenTypeFiller(), fillerCount)
            from seed in Gen.Choose(0, int.MaxValue)
            let slots = RenderTypeSlots(sugaredCount, fillers, seed)
            select (Slots: slots, SugaredCount: sugaredCount);
    }

    private static Gen<SemanticOneSugaredPackScenario> GenTypeParamsOnClass()
    {
        return
            from slots in GenTypeParamSlots()
            let source = $"public class ClassUnderTest<{slots.Slots}> {{ }}"
            select new SemanticOneSugaredPackScenario(
                source, slots.SugaredCount, SemanticPackListKind.TypeParamsOnClass,
                $"type-params-on-class (sugared {slots.SugaredCount}): {source}");
    }

    private static Gen<SemanticOneSugaredPackScenario> GenTypeParamsOnMethod()
    {
        return
            from slots in GenTypeParamSlots()
            let source = $"public class HostUnderTest {{ public void M<{slots.Slots}>() {{ }} }}"
            select new SemanticOneSugaredPackScenario(
                source, slots.SugaredCount, SemanticPackListKind.TypeParamsOnMethod,
                $"type-params-on-method (sugared {slots.SugaredCount}): {source}");
    }

    // ─── Value-parameter list scenario (method) ──────────────────────────────

    /// <summary>
    /// A single sugared value-side <c>params</c> parameter: kind 0 is a Value_Pack
    /// (<c>params TArgs u</c>, referencing the host class pack), kind 1 is a
    /// Value_Level_Params (<c>params Elem[] u</c>). Both count as one <c>params</c> under
    /// the one-sugared-pack rule.
    /// </summary>
    private static Gen<(int Kind, int Elem)> GenValueSugared()
    {
        return
            from kind in Gen.Choose(0, 1)
            from elem in Gen.Choose(0, ElementTypes.Length - 1)
            select (kind, elem);
    }

    /// <summary>
    /// A single non-counting value-parameter filler: kind 0 is a fixed value parameter
    /// (<c>Elem a</c>), kind 1 is an explicit plain tuple parameter (<c>(TArgs...) t</c>,
    /// no <c>params</c>). Neither counts toward the one-sugared-pack rule.
    /// </summary>
    private static Gen<(int Kind, int Elem)> GenValueFiller()
    {
        return
            from kind in Gen.Choose(0, 1)
            from elem in Gen.Choose(0, ElementTypes.Length - 1)
            select (kind, elem);
    }

    /// <summary>
    /// Generates a value-parameter list on a method: <c>SugaredCount</c> value-side
    /// <c>params</c> parameters (a mix of value packs and homogeneous <c>params T[]</c>)
    /// among zero-to-three non-counting filler parameters, shuffled so the <c>params</c>
    /// parameters land in leading/middle/trailing positions. The host class always
    /// declares <c>&lt;params TArgs&gt;</c> so value packs and plain tuple fillers can
    /// reference the pack; that lone class pack is itself valid and never contributes a
    /// MIST_VAR_0001.
    /// </summary>
    private static Gen<SemanticOneSugaredPackScenario> GenValueParamsOnMethod()
    {
        return
            from sugaredCount in GenSugaredCount()
            from sugared in Gen.ArrayOf(GenValueSugared(), sugaredCount)
            from fillerCount in Gen.Choose(0, 3)
            from fillers in Gen.ArrayOf(GenValueFiller(), fillerCount)
            from seed in Gen.Choose(0, int.MaxValue)
            let valueParams = RenderValueParams(sugared, fillers, seed)
            let source = $"public class HostUnderTest<params TArgs> {{ public void M({valueParams}) {{ }} }}"
            select new SemanticOneSugaredPackScenario(
                source, sugaredCount, SemanticPackListKind.ValueParamsOnMethod,
                $"value-params-on-method (sugared {sugaredCount}): {source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Rendering helpers (the sugared count is known by construction; distinct name
    // prefixes per slot kind keep every declared name unique regardless of shuffle order)
    // ═══════════════════════════════════════════════════════════════════════

    private static string RenderTypeSlots(int sugaredCount, (int Kind, int Size)[] fillers, int seed)
    {
        var slots = new List<string>(sugaredCount + fillers.Length);

        for(var k = 0; k < sugaredCount; k++)
        {
            slots.Add($"params U{k}");
        }

        var regularIndex = 0;
        var dependentIndex = 0;
        var bracketedIndex = 0;
        var fixedIndex = 0;
        foreach(var filler in fillers)
        {
            var slot = filler.Kind switch
            {
                0 => $"T{regularIndex++}",
                1 => $"D{dependentIndex++}:int",
                2 => $"Type[]:B{bracketedIndex++}",
                3 => $"Type[{filler.Size}]:F{fixedIndex++}",
                _ => throw new InvalidOperationException($"Unhandled type filler kind: {filler.Kind}"),
            };
            slots.Add(slot);
        }

        Shuffle(slots, seed);
        return string.Join(", ", slots);
    }

    private static string RenderValueParams((int Kind, int Elem)[] sugared, (int Kind, int Elem)[] fillers, int seed)
    {
        var slots = new List<string>(sugared.Length + fillers.Length);

        for(var k = 0; k < sugared.Length; k++)
        {
            var slot = sugared[k].Kind == 0
                ? $"params TArgs u{k}"
                : $"params {ElementTypes[sugared[k].Elem]}[] u{k}";
            slots.Add(slot);
        }

        var fixedIndex = 0;
        var tupleIndex = 0;
        foreach(var filler in fillers)
        {
            var slot = filler.Kind == 0
                ? $"{ElementTypes[filler.Elem]} a{fixedIndex++}"
                : $"(TArgs...) t{tupleIndex++}";
            slots.Add(slot);
        }

        Shuffle(slots, seed);
        return string.Join(", ", slots);
    }

    /// <summary>
    /// Deterministic in-place Fisher-Yates shuffle keyed on the generated
    /// <paramref name="seed"/>, so a failing scenario reproduces exactly. Slot ordering
    /// does not affect the classification (a <c>params</c> pack is Unbracketed in any
    /// position), so shuffling only broadens positional coverage.
    /// </summary>
    private static void Shuffle(List<string> items, int seed)
    {
        var rng = new Random(seed);
        for(var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // In-process legacy semantic-model construction and lookup helpers
    // (mirrors the task 10.6 / 10.2 / 10.5 pattern; ZERO external process spawns).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns), returning the
    /// model together with every construction-stage diagnostic. Pack classification, the
    /// one-Unbracketed_Pack rule, and the value-side one-<c>params</c> rule are all decided
    /// during model construction, so inspecting the resulting classification and the
    /// construction diagnostics is sufficient.
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
    /// Returns the declared type parameters of the source-declared class named
    /// <paramref name="typeName"/> (each pack counts as one slot). Read from the class's
    /// <c>DeclaredType</c>, populated by the shared <c>ExtractTypeParameters</c> pass.
    /// </summary>
    private static IReadOnlyList<IType> ClassTypeParameters(ISemanticModel model, string typeName)
    {
        var symbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == typeName && s.Kind == SymbolKind.Class);
        Assert.NotNull(symbol);

        var classType = Assert.IsType<ClassType>(symbol!.DeclaredType);
        return classType.TypeParameters;
    }

    /// <summary>
    /// Returns the declared type parameters of the method named <paramref name="methodName"/>.
    /// The method's type parameters are stored on the method <see cref="Symbol"/> by
    /// <c>RegisterMethodTypeParameters</c>, which shares <c>ExtractTypeParameters</c> with the
    /// type-level path, so pack classification is recorded identically.
    /// </summary>
    private static IReadOnlyList<IType> MethodTypeParameters(ISemanticModel model, string methodName)
    {
        var method = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == methodName && s.Kind == SymbolKind.Method);
        Assert.NotNull(method);

        return method!.TypeParameters;
    }
}