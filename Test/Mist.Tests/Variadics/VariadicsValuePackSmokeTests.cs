// Feature: variadics - Smoke check for value-pack parameter representation (Task 5.1)
// Requirements: 7.1, 7.2, 7.7
//
// A minimal in-process smoke test confirming that ParameterBuilder:
//   * gives `params TArgs args` the tuple type (TArgs...) and marks it with the
//     Flattened_Call_Convention (7.1);
//   * counts a value pack as exactly one arity slot, so a mixed list
//     `Mixed(int, params TArgs, bool)` has arity 3 (7.2);
//   * represents a plain `(TArgs...) args` parameter (no `params`) as an ordinary
//     single-tuple parameter that is NOT flattened (7.7).
//
// The full value-side representation tests are Task 5.9. This drives
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
/// Task 5.1 smoke tests for the value-pack parameter and value-side arity. Each test
/// builds the program model in-process and inspects the resulting <c>Parameter</c>
/// slots on a variadic class's constructor and methods.
/// </summary>
public sealed class VariadicsValuePackSmokeTests : IDisposable
{
    private readonly string outputDir;

    public VariadicsValuePackSmokeTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsValuePack_" + Guid.NewGuid().ToString("N"));
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

    // A single variadic class exercising all three shapes: a value pack constructor
    // parameter, a plain tuple method parameter, and a mixed value-parameter list.
    private const string Source =
        "public class Bundle<params TArgs>\n"
        + "{\n"
        + "    public Bundle(params TArgs values) { }\n"
        + "\n"
        + "    public void Take((TArgs...) whole) { }\n"
        + "\n"
        + "    public void Mixed(int number, params TArgs args, bool enabled) { }\n"
        + "}";

    [Fact]
    public void ValuePack_IsSingleTupleSlot_MarkedFlattened()
    {
        var model = BuildModel(Source);
        var bundle = FindConcreteType(model, "Bundle");

        var ctor = Assert.Single(bundle.ConstructorsList);

        // Requirement 7.2: a value pack is exactly ONE parameter slot -> arity 1.
        var values = Assert.Single(ctor.ParametersList);
        Assert.Equal("values", values.Name);

        // Requirement 7.1: `params TArgs values` has the tuple type (TArgs...) ...
        _ = Assert.IsAssignableFrom<ITupleType>(values.Type);

        // ... and carries the Flattened_Call_Convention.
        Assert.True(values.HasFlattenedCallConvention);
    }

    [Fact]
    public void PlainTupleParameter_IsOrdinary_NotFlattened()
    {
        var model = BuildModel(Source);
        var bundle = FindConcreteType(model, "Bundle");

        var take = bundle.MethodsList.Single(m => m.Name == "Take");
        var whole = Assert.Single(take.ParametersList);

        // Requirement 7.7: a plain `(TArgs...) whole` parameter (no `params`) is an
        // ordinary single-tuple parameter that is NOT flattened.
        _ = Assert.IsAssignableFrom<ITupleType>(whole.Type);
        Assert.False(whole.HasFlattenedCallConvention);

        // Both the value pack and the plain tuple parameter resolve to the SAME interned
        // (TArgs...) tuple; only the `params` marker distinguishes them.
        var ctor = Assert.Single(bundle.ConstructorsList);
        var ctorValues = Assert.Single(ctor.ParametersList);
        Assert.Same(ctorValues.Type, whole.Type);
    }

    [Fact]
    public void MixedList_CountsValuePackAsOneSlot()
    {
        var model = BuildModel(Source);
        var bundle = FindConcreteType(model, "Bundle");

        var mixed = bundle.MethodsList.Single(m => m.Name == "Mixed");

        // Requirement 7.2: `Mixed(int number, params TArgs args, bool enabled)` has arity 3
        // (the value pack counts as one slot, mirroring the type-parameter side).
        Assert.Equal(3, mixed.ParametersList.Count);

        Assert.Equal("number", mixed.ParametersList[0].Name);
        Assert.False(mixed.ParametersList[0].HasFlattenedCallConvention);

        // The middle value pack is the single flattened tuple slot.
        Assert.Equal("args", mixed.ParametersList[1].Name);
        _ = Assert.IsAssignableFrom<ITupleType>(mixed.ParametersList[1].Type);
        Assert.True(mixed.ParametersList[1].HasFlattenedCallConvention);

        Assert.Equal("enabled", mixed.ParametersList[2].Name);
        Assert.False(mixed.ParametersList[2].HasFlattenedCallConvention);
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