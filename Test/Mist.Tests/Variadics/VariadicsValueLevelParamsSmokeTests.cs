// Feature: variadics - Smoke check for the value-level `params T[]` representation (Task 5.3)
// Requirements: 13.1, 13.4, 13.5
//
// A minimal in-process smoke test confirming that ParameterBuilder represents a
// homogeneous value-level params `params T[] xs` as:
//   * a parameter whose Type is the REAL array type `T[]` (a constructed
//     Standard.Array<,>), NOT the tuple (TArgs...) used for a value pack (13.1);
//   * marked with the Flattened_Call_Convention (13.1) -- and so no longer "falling
//     through as ordinary" (the gap noted by the Task 5.2 report);
//   * distinguished from a Type_Parameter_Pack purely by position: this `params` sits in
//     the VALUE-parameter list (13.4).
// It also confirms the value-level params still counts as one arity slot (mirroring the
// value pack) and that a plain `T[]` parameter (no `params`) keeps the identical array
// type but is NOT flattened -- the `params` modifier is the sole marker of the convention.
//
// The one-`params`-per-list counting (13.5) is exercised by the Task 5.2 enforcement
// tests (VariadicsValueParamsEnforcementTests) and is unaffected by this representation
// change, because EnforceSingleValueParams keys on the `params` modifier on the node.
//
// The full value-side representation tests are Task 5.9 / property Task 12.6. This drives
// ProgramModelBuilder.Build directly: there are ZERO external process spawns.

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
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Task 5.3 smoke tests for the homogeneous value-level <c>params T[]</c>
/// (Requirements 13.1, 13.4). Each test builds the program model in-process and inspects
/// the resulting <c>Parameter</c> slots on a variadic class's constructor and methods.
/// A minimal <c>Standard.Array&lt;,&gt;</c> is declared in-source so that the array
/// surface type <c>T[]</c> resolves without loading the full standard library.
/// </summary>
public sealed class VariadicsValueLevelParamsSmokeTests : IDisposable
{
    private readonly string outputDir;

    public VariadicsValueLevelParamsSmokeTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsValueLevelParams_" + Guid.NewGuid().ToString("N"));
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

    // A single variadic class exercising all three shapes against a homogeneous element
    // type parameter `T`: a value-level params constructor parameter (`params T[]`), a
    // plain array method parameter (`T[]`, no `params`), and a mixed value-parameter list.
    // The in-source `Standard.Array<,>` backs every `T[]` surface type.
    private const string Source =
        "namespace Standard\n"
        + "{\n"
        + "    public class Array<TValue, TLength> { }\n"
        + "}\n"
        + "\n"
        + "public class Log<T>\n"
        + "{\n"
        + "    public Log(params T[] xs) { }\n"
        + "\n"
        + "    public void Plain(T[] items) { }\n"
        + "\n"
        + "    public void Mixed(T first, params T[] xs) { }\n"
        + "}";

    [Fact]
    public void ValueLevelParams_KeepsRealArrayType_MarkedFlattened()
    {
        var model = BuildModel(Source);
        var log = FindConcreteType(model, "Log");

        var ctor = Assert.Single(log.ConstructorsList);

        // Requirement 13.4 / arity: a value-level params is exactly ONE parameter slot.
        var xs = Assert.Single(ctor.ParametersList);
        Assert.Equal("xs", xs.Name);

        // Requirement 13.1: `params T[] xs` keeps the REAL array type -- a constructed
        // Standard.Array<,> -- and is NOT represented as the tuple (TArgs...) that a value
        // pack uses.
        _ = Assert.IsAssignableFrom<IConstructedType>(xs.Type);
        Assert.False(xs.Type is ITupleType);

        // ... and carries the Flattened_Call_Convention, so it no longer falls through as
        // an ordinary (unflattened) parameter.
        Assert.True(xs.HasFlattenedCallConvention);
    }

    [Fact]
    public void PlainArrayParameter_IsOrdinary_NotFlattened_SameArrayType()
    {
        var model = BuildModel(Source);
        var log = FindConcreteType(model, "Log");

        var plain = log.MethodsList.Single(m => m.Name == "Plain");
        var items = Assert.Single(plain.ParametersList);

        // A plain `T[] items` parameter (no `params`) is an ordinary array parameter that
        // is NOT flattened -- the `params` modifier is the sole marker of the convention.
        _ = Assert.IsAssignableFrom<IConstructedType>(items.Type);
        Assert.False(items.HasFlattenedCallConvention);

        // The value-level params and the plain array parameter resolve to the SAME interned
        // array type `T[]`; only the `params` marker distinguishes the flattened-call form.
        var ctor = Assert.Single(log.ConstructorsList);
        var ctorXs = Assert.Single(ctor.ParametersList);
        Assert.Same(ctorXs.Type, items.Type);
        Assert.True(ctorXs.HasFlattenedCallConvention);
    }

    [Fact]
    public void MixedList_CountsValueLevelParamsAsOneSlot()
    {
        var model = BuildModel(Source);
        var log = FindConcreteType(model, "Log");

        var mixed = log.MethodsList.Single(m => m.Name == "Mixed");

        // A value-level params counts as one slot, so `Mixed(T first, params T[] xs)` has
        // arity 2 (mirroring the value pack and the type-parameter side).
        Assert.Equal(2, mixed.ParametersList.Count);

        Assert.Equal("first", mixed.ParametersList[0].Name);
        Assert.False(mixed.ParametersList[0].HasFlattenedCallConvention);

        // The trailing value-level params is the single flattened array slot.
        Assert.Equal("xs", mixed.ParametersList[1].Name);
        _ = Assert.IsAssignableFrom<IConstructedType>(mixed.ParametersList[1].Type);
        Assert.False(mixed.ParametersList[1].Type is ITupleType);
        Assert.True(mixed.ParametersList[1].HasFlattenedCallConvention);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private IProgramModel BuildModel(string source)
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
        return model!;
    }

    private static ConcreteType FindConcreteType(IProgramModel model, string simpleName)
    {
        var matches = model.TypesByFQN.Values
            .OfType<ConcreteType>()
            .Where(t => t.Origin == TypeOrigin.Source && SimpleName(t.Name) == simpleName)
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"Expected exactly one source type named '{simpleName}', found {matches.Count}.");
        return matches[0];
    }

    private static string SimpleName(string typeName)
    {
        var angle = typeName.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? typeName : typeName[..angle];
    }
}