// Feature: variadics, task 5.7 - property test for one sugared pack per list.
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
// This test drives program-model construction entirely in-process via
// ProgramModelBuilder.Build (parse -> build program model -> inspect the MIST_VAR_0001
// diagnostics collected in the construction DiagnosticBag and the resulting slot
// classification), exactly as the task 4.5 pack-classification tests, the task 5.5
// method-list smoke (VariadicsMethodPackEnforcementTests), and the task 5.2 value-list
// smoke (VariadicsValueParamsEnforcementTests) do. There are ZERO external process
// spawns: the builder runs directly, emit is never invoked, and no compiled program is
// ever run.
//
// ── How the "sugared params count" is kept aligned with the model's classification ──
// The enforcement (built in tasks 4.2 / 5.2 / 5.5) reports MIST_VAR_0001 when a list has
// more than one Unbracketed_Pack (type side) or more than one value-side `params`
// parameter (value side). On the type side a `params` pack is ALWAYS Unbracketed, while a
// `Type[]:` pack is Unbracketed only when it is the lone pack in the list (inverse sugar)
// and otherwise stays Bracketed. Every generated list here carries at least one `params`
// pack, so any `Type[]:` filler is never the lone pack and therefore stays Bracketed --
// i.e. the generator's "sugared params count" equals the number of Unbracketed packs the
// model classifies, by construction. Bracketed `Type[]:` packs, fixed-size `Type[N]:`
// packs, regular/dependent type parameters, fixed value parameters, and explicit plain
// `(TArgs...)` tuple parameters are all non-counting filler. The property cross-checks the
// model's own classification against the generated count so it can never silently drift
// from the enforcement it validates.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
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
/// Which declared list a generated scenario exercises: a type-parameter list on a class,
/// a type-parameter list on a method, or a value-parameter list on a method. Property 9
/// must hold uniformly across all three.
/// </summary>
public enum PackListKind
{
    TypeParamsOnClass,
    TypeParamsOnMethod,
    ValueParamsOnMethod,
}

/// <summary>
/// A generated one-sugared-pack scenario: a complete, parseable Mist declaration whose
/// declared list contains a KNOWN number of sugared <c>params</c> packs
/// (<see cref="SugaredParamsCount"/>), together with which list to inspect
/// (<see cref="Kind"/>). A count of 1 is the accepted case; a count of 2 or more is the
/// MIST_VAR_0001 violation case. The count is known by construction because every sugared
/// slot is rendered with the <c>params</c> keyword and all other slots are non-counting
/// filler.
/// </summary>
public sealed record OneSugaredPackScenario(
    string Source,
    int SugaredParamsCount,
    PackListKind Kind,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property test for <b>Property 9 (One sugared pack per list)</b>. Builds the program
/// model in-process and asserts that a list with exactly one sugared <c>params</c> pack is
/// accepted (no <c>MIST_VAR_0001</c>) while a list with two or more raises exactly one
/// <c>MIST_VAR_0001</c>, uniformly across class type-parameter lists, method
/// type-parameter lists, and value-parameter lists, counting a value pack
/// (<c>params TArgs args</c>) and a homogeneous value-level <c>params T[]</c> alike.
/// <b>Validates: Requirements 3.4, 3.5, 7.8, 7.9, 12.4, 12.5, 13.5.</b>
/// </summary>
public sealed class OneSugaredPackPerListPropertyTests : IDisposable
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    // Parseable primitive types drawn for value-level `params T[]` and fixed value
    // parameters, mirroring the pool used by the sibling arity property test.
    private static readonly string[] ElementTypes =
        ["int", "string", "bool", "double", "long", "char"];

    private readonly string outputDir;

    public OneSugaredPackPerListPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsOneParams_" + Guid.NewGuid().ToString("N"));
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
    // Property 9: exactly one sugared `params` pack is accepted; two or more raise a
    // single MIST_VAR_0001. Holds for class type-parameter lists, method type-parameter
    // lists, and value-parameter lists (value packs and homogeneous `params T[]` alike).
    //
    // **Validates: Requirements 3.4, 3.5, 7.8, 7.9, 12.4, 12.5, 13.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(OneSugaredPackPerListPropertyTests) }, MaxTest = 200)]
    public bool OneSugaredPack_Accepted_TwoOrMore_RaiseMistVar0001(OneSugaredPackScenario scenario)
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

        // On the type side, additionally confirm the model classified exactly
        // SugaredParamsCount slots as the Unbracketed (`params`) pack. This pins down that a
        // bracketed `Type[]:` filler is NOT counted (it is never the lone pack, so it stays
        // Bracketed) and that the generator's sugared count matches the model's own
        // classification. The classification is purely syntactic (no stdlib needed), so it is
        // exact here.
        //
        // The value side is characterized entirely by the MIST_VAR_0001 result above: the
        // enforcement counts the `params` modifier on the parameter node itself
        // (ParameterBuilder.EnforceSingleValueParams), independent of type resolution. The
        // representation flag HasFlattenedCallConvention is deliberately NOT used as a proxy
        // here: recognizing a homogeneous `params T[]` as an array requires the Standard.Array
        // definition, which this minimal program-model harness does not load, so that flag
        // undercounts value-level arrays even though they still count toward the rule.
        if(scenario.Kind != PackListKind.ValueParamsOnMethod)
        {
            Assert.Equal(scenario.SugaredParamsCount, CountUnbracketedTypePacks(model, scenario.Kind));
        }

        return true;
    }

    /// <summary>
    /// Reads back how many type-parameter slots the built model classified as the
    /// Unbracketed (<c>params</c>) pack for the scenario's list. A <c>params</c> pack is
    /// always Unbracketed, and because every generated list carries a <c>params</c> pack,
    /// each <c>Type[]:</c> filler is never the lone pack and stays Bracketed -- so this count
    /// equals the generated sugared count. Applies to the two type-list kinds only.
    /// </summary>
    private static int CountUnbracketedTypePacks(IProgramModel model, PackListKind kind)
    {
        return kind switch
        {
            PackListKind.TypeParamsOnClass =>
                FindType(model, "ClassUnderTest").TypeParametersList.Count(p => p.IsUnbracketedPack),
            PackListKind.TypeParamsOnMethod =>
                FindMethod(model, "HostUnderTest", "M").TypeParametersList.Count(p => p.IsUnbracketedPack),
            _ => throw new InvalidOperationException($"Unhandled type-list kind: {kind}"),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<OneSugaredPackScenario> OneSugaredPackScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<OneSugaredPackScenario> GenScenario()
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

    private static Gen<OneSugaredPackScenario> GenTypeParamsOnClass()
    {
        return
            from slots in GenTypeParamSlots()
            let source = $"public class ClassUnderTest<{slots.Slots}> {{ }}"
            select new OneSugaredPackScenario(
                source, slots.SugaredCount, PackListKind.TypeParamsOnClass,
                $"type-params-on-class (sugared {slots.SugaredCount}): {source}");
    }

    private static Gen<OneSugaredPackScenario> GenTypeParamsOnMethod()
    {
        return
            from slots in GenTypeParamSlots()
            let source = $"public class HostUnderTest {{ public void M<{slots.Slots}>() {{ }} }}"
            select new OneSugaredPackScenario(
                source, slots.SugaredCount, PackListKind.TypeParamsOnMethod,
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
    private static Gen<OneSugaredPackScenario> GenValueParamsOnMethod()
    {
        return
            from sugaredCount in GenSugaredCount()
            from sugared in Gen.ArrayOf(GenValueSugared(), sugaredCount)
            from fillerCount in Gen.Choose(0, 3)
            from fillers in Gen.ArrayOf(GenValueFiller(), fillerCount)
            from seed in Gen.Choose(0, int.MaxValue)
            let valueParams = RenderValueParams(sugared, fillers, seed)
            let source = $"public class HostUnderTest<params TArgs> {{ public void M({valueParams}) {{ }} }}"
            select new OneSugaredPackScenario(
                source, sugaredCount, PackListKind.ValueParamsOnMethod,
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
    // In-process program-model construction and lookup helpers
    // (mirrors the task 4.5 / 5.5 / 5.2 pattern; ZERO external process spawns).
    // ═══════════════════════════════════════════════════════════════════════

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
            $"Unexpected parse errors for source <{source}>: "
                + string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}")));

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

    private static MemberType FindType(IProgramModel model, string simpleName)
    {
        var matches = model.TypesByFQN.Values
            .OfType<MemberType>()
            .Where(t => t.Origin == TypeOrigin.Source && SimpleName(t.Name) == simpleName)
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"Expected exactly one source type named '{simpleName}', found {matches.Count}.");
        return matches[0];
    }

    private static Method FindMethod(IProgramModel model, string typeName, string methodName)
    {
        var type = FindType(model, typeName);
        var matches = type.MethodsList.Where(m => m.Name == methodName).ToList();

        Assert.True(
            matches.Count == 1,
            $"Expected exactly one method '{methodName}' on '{typeName}', found {matches.Count}.");
        return matches[0];
    }

    private static string SimpleName(string typeName)
    {
        var angle = typeName.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? typeName : typeName[..angle];
    }
}