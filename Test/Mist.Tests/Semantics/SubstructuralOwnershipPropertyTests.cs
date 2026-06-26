// Feature: drop-semantics, Property 1: Substructural modifier implies correct ownership
// **Validates: Requirements 1.3, 1.4, 1.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for the ownership implication of substructural modifiers.
///
/// Feature: drop-semantics, Property 1: Substructural modifier implies correct ownership
/// **Validates: Requirements 1.3, 1.4, 1.5**
///
/// For any type declared with <c>affine</c> or <c>linear</c>, <see cref="ClassType.IsOwnedType"/>
/// (or <see cref="StructType.IsOwnedType"/>) SHALL be <c>true</c>. For any type declared with
/// <c>relevant</c>, <c>IsOwnedType</c> SHALL be <c>false</c>.
///
/// The model is built in-process via a shared <see cref="CompilerDriver"/> and the type's
/// <c>IsOwnedType</c> flag is inspected directly. Spawns 0 external processes.
/// </summary>
public sealed class SubstructuralOwnershipPropertyTests : IDisposable
{
    private readonly string outputDir;

    public SubstructuralOwnershipPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistSubstructuralOwnershipPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Capturing semantic model builder ────────────────────────────────

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so tests can inspect the
    /// constructed type model (e.g. <c>IsOwnedType</c>, <c>Discipline</c>) after build.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();
        public ISemanticModel? Captured { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            Captured = model;
            return model;
        }
    }

    // ─── Compile helper (shared driver per instance) ─────────────────────

    private ISemanticModel? CompileAndCapture(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(
                new TypeCheckerAnalyzer(),
                new ReadonlyAnalyzer(),
                new OwnershipAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        _ = driver.Compile(options);
        return capture.Captured;
    }

    /// <summary>Finds a user-code class or struct type by name and returns its IsOwnedType flag.</summary>
    private static bool? FindIsOwnedType(ISemanticModel model, string name)
    {
        var declaredType = model.AllSymbols
            .Where(s => s.Origin == SymbolOrigin.UserCode
                && (s.Kind == SymbolKind.Class || s.Kind == SymbolKind.Struct)
                && s.Name == name)
            .Select(s => s.DeclaredType)
            .FirstOrDefault();

        return declaredType switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            _ => null,
        };
    }

    // ─── Scenario ────────────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated substructural type declaration together with the IsOwnedType value
    /// that Property 1 requires for its discipline.
    /// </summary>
    public sealed record SubstructuralOwnershipScenario(
        string Source, string TypeName, string Discipline, bool ExpectedOwned, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

#pragma warning restore CA1034

    private static readonly string[] TypeNames =
        ["Token", "Handle", "Resource", "Box", "Guard", "Slot", "Cell", "Lease"];

    private static readonly string[] ValueTypes =
        ["int", "bool", "float", "double", "long", "byte", "char"];

    private static readonly string[] FieldNames =
        ["a", "b", "c", "x", "y", "z"];

    // ─── Generator ───────────────────────────────────────────────────────
    //
    // affine/linear → IsOwnedType must be true (discipline write-through during
    //   construction, independent of field composition).
    // relevant → IsOwnedType must be false (relevant does NOT imply owned).
    //
    // To keep the relevant assertion robust against struct ownership inference
    // (which can infer all-owned-field / zero-field structs as owned), relevant
    // types are generated as classes (never inferred) or as structs carrying a
    // non-owned reference-type field, so inference never flips IsOwnedType.

    private static Gen<SubstructuralOwnershipScenario> GenScenario()
    {
        return from discipline in Gen.Elements("affine", "linear", "relevant")
               from typeName in Gen.Elements(TypeNames)
               from useStruct in Gen.Elements(true, false)
               from fieldCount in Gen.Choose(0, 2)
               from fieldTypes in Gen.ArrayOf(Gen.Elements(ValueTypes), fieldCount)
               select Build(discipline, typeName, useStruct, fieldTypes);
    }

    private static SubstructuralOwnershipScenario Build(
        string discipline, string typeName, bool useStruct, string[] fieldTypes)
    {
        var expectedOwned = discipline is "affine" or "linear";
        var kind = useStruct ? "struct" : "class";

        // For relevant structs, append a non-owned (class) field so ownership
        // inference does not promote the struct to owned and confound the property.
        var extraField = "";
        if(discipline == "relevant" && useStruct)
        {
            extraField = "\n    NonOwnedRef item;";
        }

        var fields = BuildFields(fieldTypes);
        var body = (fields + extraField).Trim();

        var source = $$"""
            class NonOwnedRef
            {
            }
            {{discipline}} {{kind}} {{typeName}}
            {
                {{body}}
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var label = $"{discipline} {kind} {typeName} ({fieldTypes.Length} value field(s))"
            + (extraField.Length > 0 ? " + non-owned field" : "");

        return new SubstructuralOwnershipScenario(source, typeName, discipline, expectedOwned, label);
    }

    private static string BuildFields(string[] fieldTypes)
    {
        if(fieldTypes.Length == 0)
        {
            return "";
        }

        var lines = new List<string>();
        for(var i = 0; i < fieldTypes.Length; i++)
        {
            var fieldName = i < FieldNames.Length ? FieldNames[i] : $"f{i}";
            lines.Add($"{fieldTypes[i]} {fieldName};");
        }

        return string.Join("\n    ", lines);
    }

    public static Arbitrary<SubstructuralOwnershipScenario> ArbitrarySubstructuralOwnershipScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Substructural modifier implies correct ownership
    //
    // affine/linear ⇒ IsOwnedType == true ; relevant ⇒ IsOwnedType == false.
    //
    // **Validates: Requirements 1.3, 1.4, 1.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SubstructuralOwnershipPropertyTests) })]
    public Property SubstructuralModifier_ImpliesCorrectOwnership(SubstructuralOwnershipScenario scenario)
    {
        var model = CompileAndCapture(scenario.Source);

        if(model is null)
        {
            return false.ToProperty().Label("Semantic model was null (compilation failed early)");
        }

        var isOwned = FindIsOwnedType(model, scenario.TypeName);
        if(isOwned is null)
        {
            return false.ToProperty().Label($"Type '{scenario.TypeName}' not found in model.\nSource:\n{scenario.Source}");
        }

        return (isOwned.Value == scenario.ExpectedOwned).ToProperty()
            .Label($"Expected IsOwnedType={scenario.ExpectedOwned} for {scenario.Discipline} type "
                + $"'{scenario.TypeName}', got {isOwned.Value}.\nSource:\n{scenario.Source}");
    }

    // ─── Example-based sanity checks ──────────────────────────────────────

    [Theory]
    [InlineData("affine", "class", true)]
    [InlineData("linear", "class", true)]
    [InlineData("relevant", "class", false)]
    [InlineData("affine", "struct", true)]
    [InlineData("linear", "struct", true)]
    public void Discipline_SetsExpectedOwnership(string discipline, string kind, bool expectedOwned)
    {
        var source = $$"""
            {{discipline}} {{kind}} Sample
            {
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var isOwned = FindIsOwnedType(model!, "Sample");
        _ = Assert.NotNull(isOwned);
        Assert.Equal(expectedOwned, isOwned!.Value);
    }
}