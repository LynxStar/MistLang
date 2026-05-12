// Feature: deep-ownership-types, Property 3: Ownership inference for structs

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for ownership inference on structs.
///
/// Property 3: Ownership Inference for Structs
/// For any struct declaration without an explicit 'owned' modifier, the struct SHALL be
/// inferred as an owned type if and only if all of its fields have types that are owned
/// types (explicitly declared or inferred). Structs with zero fields are vacuously owned.
/// Classes are never inferred.
///
/// Uses a CapturingSemanticModelBuilder to access the semantic model after compilation
/// and directly verify IsOwnedType on struct/class types.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.5, 3.6**
/// </summary>
public sealed class OwnershipInferencePropertyTests : IDisposable
{
    private readonly string _outputDir;

    public OwnershipInferencePropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistOwnershipInferencePBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Capturing semantic model builder ────────────────────────────────

    /// <summary>
    /// Wraps the real SemanticModelBuilder to capture the constructed model
    /// so tests can inspect IsOwnedType on types after inference runs.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder _inner = new();
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
            var model = _inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            Captured = model;
            return model;
        }
    }

    // ─── Compile helper ──────────────────────────────────────────────────

    private (CompilerResult Result, ISemanticModel? Model) CompileAndCapture(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
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
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = driver.Compile(options);
        return (result, capture.Captured);
    }

    /// <summary>
    /// Finds a struct type by name in the semantic model (user-code only).
    /// </summary>
    private static StructType? FindStructType(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .Where(s => s.Origin == SymbolOrigin.UserCode && s.Kind == SymbolKind.Struct && s.Name == name)
            .Select(s => s.DeclaredType)
            .OfType<StructType>()
            .FirstOrDefault();
    }

    /// <summary>
    /// Finds a class type by name in the semantic model (user-code only).
    /// </summary>
    private static ClassType? FindClassType(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .Where(s => s.Origin == SymbolOrigin.UserCode && s.Kind == SymbolKind.Class && s.Name == name)
            .Select(s => s.DeclaredType)
            .OfType<ClassType>()
            .FirstOrDefault();
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario for a struct that should be inferred as owned (all fields are owned types).
    /// </summary>
    public sealed record InferredOwnedStructScenario(string Source, string StructName, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for a struct that should NOT be inferred as owned (has non-owned fields).
    /// </summary>
    public sealed record NonInferredStructScenario(string Source, string StructName, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for a zero-field struct (vacuously owned).
    /// </summary>
    public sealed record ZeroFieldStructScenario(string Source, string StructName, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for a class with all-owned fields but no explicit 'owned' modifier.
    /// Classes are never inferred.
    /// </summary>
    public sealed record ClassNeverInferredScenario(string Source, string ClassName, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] StructNames =
        ["Point", "Vector", "Pair", "Coord", "Dims", "Range", "Span", "Rect"];

    private static readonly string[] FieldNames =
        ["x", "y", "z", "width", "height", "depth", "start", "end"];

    private static readonly string[] ValueTypes =
        ["int", "bool", "float", "double", "long", "byte", "char"];

    private static readonly string[] RefTypeNames =
        ["Widget", "Handler", "Service", "Processor", "Manager", "Controller"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates structs with 1–3 fields, all of which are primitive (owned) types.
    /// These structs should be inferred as owned.
    /// </summary>
    private static Gen<InferredOwnedStructScenario> GenInferredOwnedStructScenario()
    {
        return from structName in Gen.Elements(StructNames)
               from fieldCount in Gen.Choose(1, 3)
               from fieldTypes in Gen.ArrayOf(Gen.Elements(ValueTypes), fieldCount)
               let fields = BuildFields(fieldTypes)
               let source = $$"""
                   struct {{structName}} {
                       {{fields}}
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new InferredOwnedStructScenario(source, structName,
                   $"struct {structName} with {fieldCount} owned field(s)");
    }

    /// <summary>
    /// Generates structs with at least one non-owned reference-type field.
    /// These structs should NOT be inferred as owned.
    /// </summary>
    private static Gen<NonInferredStructScenario> GenNonInferredStructScenario()
    {
        return from structName in Gen.Elements(StructNames)
               from refTypeName in Gen.Elements(RefTypeNames)
               from hasValueField in Gen.Elements(true, false)
               from valueType in Gen.Elements(ValueTypes)
               let valueFieldDecl = hasValueField ? $"\n    {valueType} extra;" : ""
               let source = $$"""
                   class {{refTypeName}} {
                   }
                   struct {{structName}} {
                       {{refTypeName}} item;{{valueFieldDecl}}
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               let label = hasValueField
                   ? $"struct {structName} with non-owned field {refTypeName} + value field"
                   : $"struct {structName} with non-owned field {refTypeName}"
               select new NonInferredStructScenario(source, structName, label);
    }

    /// <summary>
    /// Generates zero-field structs which should be vacuously inferred as owned.
    /// </summary>
    private static Gen<ZeroFieldStructScenario> GenZeroFieldStructScenario()
    {
        return from structName in Gen.Elements(StructNames)
               let source = $$"""
                   struct {{structName}} {
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new ZeroFieldStructScenario(source, structName,
                   $"zero-field struct {structName}");
    }

    /// <summary>
    /// Generates classes with all-owned fields but no explicit 'owned' modifier.
    /// Classes are never inferred as owned (Requirement 3.5).
    /// </summary>
    private static Gen<ClassNeverInferredScenario> GenClassNeverInferredScenario()
    {
        return from className in Gen.Elements(RefTypeNames)
               from fieldCount in Gen.Choose(0, 2)
               from fieldTypes in Gen.ArrayOf(Gen.Elements(ValueTypes), fieldCount)
               let fields = fieldCount > 0 ? BuildFields(fieldTypes) : ""
               let source = $$"""
                   class {{className}} {
                       {{fields}}
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new ClassNeverInferredScenario(source, className,
                   $"class {className} with {fieldCount} owned field(s) (never inferred)");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string BuildFields(string[] fieldTypes)
    {
        var lines = new List<string>();
        for (var i = 0; i < fieldTypes.Length; i++)
        {
            var fieldName = i < FieldNames.Length ? FieldNames[i] : $"f{i}";
            lines.Add($"{fieldTypes[i]} {fieldName};");
        }
        return string.Join("\n    ", lines);
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<InferredOwnedStructScenario> ArbitraryInferredOwnedStructScenario() =>
        GenInferredOwnedStructScenario().ToArbitrary();

    public static Arbitrary<NonInferredStructScenario> ArbitraryNonInferredStructScenario() =>
        GenNonInferredStructScenario().ToArbitrary();

    public static Arbitrary<ZeroFieldStructScenario> ArbitraryZeroFieldStructScenario() =>
        GenZeroFieldStructScenario().ToArbitrary();

    public static Arbitrary<ClassNeverInferredScenario> ArbitraryClassNeverInferredScenario() =>
        GenClassNeverInferredScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3a: Structs with all-owned fields are inferred as owned
    //
    // A struct whose fields are all owned types (primitives) should be inferred
    // as an owned type. After compilation, IsOwnedType should be true.
    //
    // **Validates: Requirements 3.1, 3.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipInferencePropertyTests) })]
    public Property AllOwnedFieldStruct_IsInferredAsOwned(InferredOwnedStructScenario scenario)
    {
        var (result, model) = CompileAndCapture(scenario.Source);

        if (model is null)
            return false.ToProperty().Label("Semantic model was null (compilation failed early)");

        var structType = FindStructType(model, scenario.StructName);
        if (structType is null)
            return false.ToProperty().Label($"Struct '{scenario.StructName}' not found in model");

        return structType.IsOwnedType.ToProperty()
            .Label($"Expected IsOwnedType=true for struct '{scenario.StructName}' with all-owned fields");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3b: Structs with non-owned fields are NOT inferred as owned
    //
    // A struct that has at least one field whose type is not an owned type
    // should NOT be inferred as owned. After compilation, IsOwnedType should
    // be false.
    //
    // **Validates: Requirement 3.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipInferencePropertyTests) })]
    public Property NonOwnedFieldStruct_IsNotInferredAsOwned(NonInferredStructScenario scenario)
    {
        var (result, model) = CompileAndCapture(scenario.Source);

        if (model is null)
            return false.ToProperty().Label("Semantic model was null (compilation failed early)");

        var structType = FindStructType(model, scenario.StructName);
        if (structType is null)
            return false.ToProperty().Label($"Struct '{scenario.StructName}' not found in model");

        return (!structType.IsOwnedType).ToProperty()
            .Label($"Expected IsOwnedType=false for struct '{scenario.StructName}' with non-owned field");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3c: Zero-field structs are vacuously inferred as owned
    //
    // A struct with zero fields trivially satisfies the owned-field requirement
    // and should be inferred as owned.
    //
    // **Validates: Requirement 3.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipInferencePropertyTests) })]
    public Property ZeroFieldStruct_IsInferredAsOwned(ZeroFieldStructScenario scenario)
    {
        var (result, model) = CompileAndCapture(scenario.Source);

        if (model is null)
            return false.ToProperty().Label("Semantic model was null (compilation failed early)");

        var structType = FindStructType(model, scenario.StructName);
        if (structType is null)
            return false.ToProperty().Label($"Struct '{scenario.StructName}' not found in model");

        return structType.IsOwnedType.ToProperty()
            .Label($"Expected IsOwnedType=true for zero-field struct '{scenario.StructName}'");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3d: Classes are never inferred as owned
    //
    // Even when a class has all-owned fields (or zero fields), it should NOT
    // be inferred as owned. Only structs are eligible for inference.
    //
    // **Validates: Requirement 3.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipInferencePropertyTests) })]
    public Property ClassWithOwnedFields_IsNeverInferred(ClassNeverInferredScenario scenario)
    {
        var (result, model) = CompileAndCapture(scenario.Source);

        if (model is null)
            return false.ToProperty().Label("Semantic model was null (compilation failed early)");

        var classType = FindClassType(model, scenario.ClassName);
        if (classType is null)
            return false.ToProperty().Label($"Class '{scenario.ClassName}' not found in model");

        return (!classType.IsOwnedType).ToProperty()
            .Label($"Expected IsOwnedType=false for class '{scenario.ClassName}' (classes are never inferred)");
    }
}
