// variadics, task 5.6: property test for arity-in-slots.
//
// Property 8: Arity is slot count.
// **Validates: Requirements 3.6, 7.2**
//
// For any type-parameter list and any value-parameter list, the declared arity equals
// the number of declared slots, where each pack -- a type-array pack spelled `params`
// or `Type[]:` / `Type[N]:`, or a value pack `params TArgs args` / `params T[] xs` --
// counts as exactly ONE slot, independent of the concrete pack length it later absorbs.
//
// This test drives program-model construction entirely in-process via
// ProgramModelBuilder.Build (parse -> build program model -> inspect the resulting
// TypeParametersList.Count / ParametersList.Count), exactly as the task 4.5 pack
// classification tests and the task 5.1 value-pack smoke tests do. There are ZERO
// external process spawns: the builder runs directly, emit is never invoked, and no
// compiled program is ever run.
//
// How the expected slot count is known: each scenario is generated from a list of slot
// descriptors whose LENGTH is the intended arity. Every descriptor renders to exactly
// one declared slot -- a regular type parameter (T{i}), a dependent parameter
// (D{i}:int), a bracketed pack (Type[]:P{i}), a fixed-size pack (Type[N]:P{i}), the
// single unbracketed pack (params U{i}), a fixed value parameter (T a{i}), a value pack
// (params TArgs pk), or a homogeneous value-level params (params T[] xs). Each generated
// list carries AT MOST ONE unbracketed pack, so it never trips MIST_VAR_0001, keeping
// the property focused on "arity == slot count". The single pack is placed in leading,
// middle, and trailing positions across the generated space.

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
/// a type-parameter list on a method, or a value-parameter list on a method.
/// </summary>
public enum ArityListKind
{
    TypeParamsOnClass,
    TypeParamsOnMethod,
    ValueParamsOnMethod,
}

/// <summary>
/// A generated arity scenario: a complete, parseable Mist declaration whose declared
/// list has a KNOWN slot count (<see cref="ExpectedArity"/>), together with which list to
/// inspect (<see cref="Kind"/>). The slot count is known by construction because every
/// generated descriptor renders to exactly one declared slot.
/// </summary>
public sealed record AritySlotScenario(
    string Source,
    int ExpectedArity,
    ArityListKind Kind,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property test for <b>Property 8 (Arity is slot count)</b>. Builds the program model
/// in-process and asserts that the declared arity of a type-parameter or value-parameter
/// list equals the number of declared slots, counting each pack as exactly one slot.
/// <b>Validates: Requirements 3.6, 7.2.</b>
/// </summary>
public sealed class VariadicsAritySlotCountPropertyTests : IDisposable
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    private const int UnbracketedPackKind = 4;

    // Parseable primitive types drawn for fixed value parameters and value-level params,
    // mirroring the pool used by MistVariadicsGenerator.
    private static readonly string[] ElementTypes =
        ["int", "string", "bool", "double", "long", "char", "float", "byte"];

    private readonly string outputDir;

    public VariadicsAritySlotCountPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsArity_" + Guid.NewGuid().ToString("N"));
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
    // Property 8: declared arity == number of declared slots (each pack = one slot).
    // **Validates: Requirements 3.6, 7.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsAritySlotCountPropertyTests) }, MaxTest = 200)]
    public bool DeclaredArity_EqualsDeclaredSlotCount(AritySlotScenario scenario)
    {
        var (model, diagnostics) = BuildModel(scenario.Source);

        // Every generated list has at most one Unbracketed_Pack, so the
        // one-`params`-per-list rule must not fire; a MIST_VAR_0001 here would signal a
        // malformed scenario rather than a genuine arity result.
        Assert.Equal(0, diagnostics.Count(d => d.Id.Value == MultipleParamsPacksId));

        var actualArity = scenario.Kind switch
        {
            ArityListKind.TypeParamsOnClass =>
                FindType(model, "ClassUnderTest").TypeParametersList.Count,
            ArityListKind.TypeParamsOnMethod =>
                FindMethod(model, "HostUnderTest", "M").TypeParametersList.Count,
            ArityListKind.ValueParamsOnMethod =>
                FindMethod(model, "HostUnderTest", "M").ParametersList.Count,
            _ => throw new InvalidOperationException($"Unhandled kind: {scenario.Kind}"),
        };

        Assert.Equal(scenario.ExpectedArity, actualArity);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<AritySlotScenario> AritySlotScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<AritySlotScenario> GenScenario()
    {
        return Gen.OneOf(
            GenTypeParamsOnClass(),
            GenTypeParamsOnMethod(),
            GenValueParamsOnMethod());
    }

    // ─── Type-parameter list scenarios (class and method) ────────────────────

    /// <summary>
    /// Generates a rendered type-parameter slot list together with its intended slot
    /// count. Slots are drawn from regular, dependent, bracketed-pack, and fixed-size-pack
    /// descriptors; an optional single unbracketed <c>params</c> pack replaces one slot at
    /// a random position (leading/middle/trailing). At most one unbracketed pack is ever
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

    private static Gen<AritySlotScenario> GenTypeParamsOnClass()
    {
        return
            from slots in GenTypeParamSlots()
            let source = $"public class ClassUnderTest<{slots.Slots}> {{ }}"
            select new AritySlotScenario(
                source, slots.Count, ArityListKind.TypeParamsOnClass,
                $"type-params-on-class (arity {slots.Count}): {source}");
    }

    private static Gen<AritySlotScenario> GenTypeParamsOnMethod()
    {
        return
            from slots in GenTypeParamSlots()
            let source = $"public class HostUnderTest {{ public void M<{slots.Slots}>() {{ }} }}"
            select new AritySlotScenario(
                source, slots.Count, ArityListKind.TypeParamsOnMethod,
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
    private static Gen<AritySlotScenario> GenValueParamsOnMethod()
    {
        return
            from fixedCount in Gen.Choose(0, 5)
            from mode in Gen.Elements(0, 1, 2)
            from fixedTypes in Gen.ArrayOf(Gen.Elements(ElementTypes), fixedCount)
            from valuePos in Gen.Choose(0, fixedCount)
            from elemType in Gen.Elements(ElementTypes)
            let built = RenderValueParams(fixedCount, mode, fixedTypes, valuePos, elemType)
            select new AritySlotScenario(
                built.Source, built.Count, ArityListKind.ValueParamsOnMethod,
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
    // In-process program-model construction and lookup helpers
    // (mirrors the task 4.5 / 5.1 pattern; ZERO external process spawns).
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