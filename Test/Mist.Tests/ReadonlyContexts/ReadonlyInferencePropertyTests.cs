// Feature: readonly-contexts, Property 11: Readonly Inference Locality
// Feature: readonly-contexts, Property 12: Readonly Inference Classification

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
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.ReadonlyContexts;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for Property 11: Readonly Inference Locality.
/// A method calls another method that is explicitly readonly; the calling method
/// should be inferred as readonly based solely on the called method's status.
/// </summary>
public sealed class InferenceLocalityScenario
{
    public string Source { get; }
    public string CallingMethodName { get; }
    public string Description { get; }

    public InferenceLocalityScenario(string source, string callingMethodName, string description)
    {
        Source = source;
        CallingMethodName = callingMethodName;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for Property 12: Readonly Inference Classification.
/// A member whose body contains only readonly-safe operations should be
/// classified as InferredReadonly.
/// </summary>
public sealed class InferenceClassificationScenario
{
    public string Source { get; }
    public string TargetMemberName { get; }
    public string Description { get; }

    public InferenceClassificationScenario(string source, string targetMemberName, string description)
    {
        Source = source;
        TargetMemberName = targetMemberName;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class ReadonlyInferenceGenerators
{
    private static readonly string[] ClassNames = ["Foo", "Bar", "Baz", "Widget"];
    private static readonly string[] FieldNames = ["x", "y", "count", "name"];
    private static readonly string[] MethodNames = ["Get", "Compute", "Process", "Calculate"];

    // ─── Property 11: Readonly Inference Locality ────────────────────────

    /// <summary>
    /// Generates scenarios where a method calls an explicitly readonly method.
    /// The calling method should be inferred as readonly based solely on the
    /// called method's readonly status, not by traversing into its body.
    /// The called method's body may contain complex logic (field reads, locals,
    /// arithmetic) but is explicitly marked readonly — the inference engine
    /// should only check the status, not re-analyze the body.
    /// </summary>
    public static Gen<InferenceLocalityScenario> GenInferenceLocality()
    {
        return Gen.OneOf(
            GenLocalityCallsExplicitReadonly(),
            GenLocalityCallsExplicitReadonlyWithComplexBody(),
            GenLocalityChainedReadonlyCalls());
    }

    private static Gen<InferenceLocalityScenario> GenLocalityCallsExplicitReadonly()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from callerName in Gen.Elements(MethodNames)
               let calleeName = "Helper"
               let source = $@"class {className} {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    readonly int {calleeName}() {{
        return this.{fieldName};
    }}
    int {callerName}() {{
        return this.{calleeName}();
    }}
}}"
               select new InferenceLocalityScenario(source, callerName,
                   $"'{callerName}' calls explicit readonly '{calleeName}' in '{className}' — inferred readonly by locality");
    }

    private static Gen<InferenceLocalityScenario> GenLocalityCallsExplicitReadonlyWithComplexBody()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from callerName in Gen.Elements(MethodNames)
               let calleeName = "ComplexHelper"
               // The callee has a complex body with locals and arithmetic,
               // but is explicitly readonly. The inference engine should NOT
               // traverse into this body — it should only check the status.
               let source = $@"class {className} {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    readonly int {calleeName}() {{
        var a = this.{fieldName};
        var b = a + 1;
        var c = b + a;
        return c;
    }}
    int {callerName}() {{
        var result = this.{calleeName}();
        return result;
    }}
}}"
               select new InferenceLocalityScenario(source, callerName,
                   $"'{callerName}' calls explicit readonly '{calleeName}' (complex body) in '{className}' — locality principle");
    }

    private static Gen<InferenceLocalityScenario> GenLocalityChainedReadonlyCalls()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from callerName in Gen.Elements(MethodNames)
               let source = $@"class {className} {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    readonly int First() {{
        return this.{fieldName};
    }}
    readonly int Second() {{
        return this.First() + 1;
    }}
    int {callerName}() {{
        return this.Second() + this.First();
    }}
}}"
               select new InferenceLocalityScenario(source, callerName,
                   $"'{callerName}' calls multiple explicit readonly methods in '{className}' — locality principle");
    }

    // ─── Property 12: Readonly Inference Classification ─────────────────

    /// <summary>
    /// Generates members whose bodies contain only readonly-safe operations.
    /// These should be classified as InferredReadonly by the inference engine.
    /// </summary>
    public static Gen<InferenceClassificationScenario> GenInferenceClassification()
    {
        return Gen.OneOf(
            GenClassificationLiteralReturn(),
            GenClassificationFieldRead(),
            GenClassificationLocalVariables(),
            GenClassificationCallsReadonlyMethod(),
            GenClassificationReadonlyConstructor());
    }

    private static Gen<InferenceClassificationScenario> GenClassificationLiteralReturn()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from literal in Gen.Elements("0", "1", "42", "100")
               let source = $@"class {className} {{
    int {methodName}() {{
        return {literal};
    }}
}}"
               select new InferenceClassificationScenario(source, methodName,
                   $"'{methodName}' returns literal {literal} in '{className}' — inferred readonly");
    }

    private static Gen<InferenceClassificationScenario> GenClassificationFieldRead()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"class {className} {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    int {methodName}() {{
        return this.{fieldName};
    }}
}}"
               select new InferenceClassificationScenario(source, methodName,
                   $"'{methodName}' reads field '{fieldName}' in '{className}' — inferred readonly");
    }

    private static Gen<InferenceClassificationScenario> GenClassificationLocalVariables()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"class {className} {{
    int {methodName}() {{
        var a = 1;
        var b = 2;
        var c = a + b;
        return c;
    }}
}}"
               select new InferenceClassificationScenario(source, methodName,
                   $"'{methodName}' uses only local variables in '{className}' — inferred readonly");
    }

    private static Gen<InferenceClassificationScenario> GenClassificationCallsReadonlyMethod()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"class {className} {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    readonly int Helper() {{
        return this.{fieldName};
    }}
    int {methodName}() {{
        return this.Helper();
    }}
}}"
               select new InferenceClassificationScenario(source, methodName,
                   $"'{methodName}' calls readonly method in '{className}' — inferred readonly");
    }

    private static Gen<InferenceClassificationScenario> GenClassificationReadonlyConstructor()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               let ctorName = className
               let source = $@"readonly class Other {{
    int val;
    Other() {{
        this.val = 0;
    }}
}}
class {className} {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    int MakeOther() {{
        var o = new Other();
        return 1;
    }}
}}"
               select new InferenceClassificationScenario(source, "MakeOther",
                   $"'MakeOther' allocates readonly type 'Other' in '{className}' — inferred readonly");
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for the readonly inference engine.
///
/// Properties 11 and 12 from the readonly-contexts design document.
/// Uses in-process compilation via a shared CompilerDriver instance with a
/// CapturingSemanticModelBuilder to access the semantic model and verify
/// ReadonlyStatus on symbols after inference runs.
/// </summary>
public sealed class ReadonlyInferencePropertyTests : IDisposable
{
    private readonly string outputDir;

    public ReadonlyInferencePropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistReadonlyInferencePBT_" + Guid.NewGuid().ToString("N"));
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
    /// Wraps the real SemanticModelBuilder to capture the constructed model
    /// so tests can inspect symbol ReadonlyStatus after inference runs.
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

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<InferenceLocalityScenario> ArbitraryInferenceLocality() =>
        ReadonlyInferenceGenerators.GenInferenceLocality().ToArbitrary();

    public static Arbitrary<InferenceClassificationScenario> ArbitraryInferenceClassification() =>
        ReadonlyInferenceGenerators.GenInferenceClassification().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    private (CompilerResult Result, ISemanticModel? Model) CompileAndCapture(string source)
    {
        var fullSource = "namespace TestProject;\n" + source;
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, fullSource);

        var capture = new CapturingSemanticModelBuilder();
        var driver = new CompilerDriver(
            new MistLexer(),
            new MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer(), new ReadonlyAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray.Create("readonly-context"),
            EmitToDisk: false);

        var result = driver.Compile(options);
        return (result, capture.Captured);
    }

    /// <summary>
    /// Finds a method symbol by name within the captured semantic model.
    /// Searches all symbols for a method with the given name.
    /// </summary>
    private static Symbol? FindMethodSymbol(ISemanticModel model, string methodName)
    {
        return model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == methodName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Readonly Inference Locality
    //
    // **Validates: Requirements 10.1, 10.8**
    //
    // For any method that calls another method, the inference engine SHALL
    // classify the calling method based solely on the called method's
    // readonly status (not by traversing into the called method's body).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyInferencePropertyTests) })]
    public Property InferenceLocality_CallingReadonlyMethod_InferredAsReadonly(
        InferenceLocalityScenario scenario)
    {
        var (result, model) = CompileAndCapture(scenario.Source);

        if(model is null)
        {
            return false.ToProperty()
                .Label($"Semantic model was not captured (compilation may have failed early).\nDiagnostics: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }

        var methodSymbol = FindMethodSymbol(model, scenario.CallingMethodName);
        if(methodSymbol is null)
        {
            return false.ToProperty()
                .Label($"Could not find method '{scenario.CallingMethodName}' in semantic model.\nSource:\n{scenario.Source}");
        }

        var isInferredReadonly = methodSymbol.ReadonlyStatus == ReadonlyStatus.InferredReadonly;

        return isInferredReadonly.ToProperty()
            .Label($"Expected '{scenario.CallingMethodName}' to be InferredReadonly but was {methodSymbol.ReadonlyStatus}.\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Readonly Inference Classification
    //
    // **Validates: Requirements 10.2, 10.3, 10.4, 10.5, 10.6, 10.7**
    //
    // For any member whose body contains only literal expressions, field
    // reads, local variable operations, calls to readonly methods/getters,
    // and allocations via readonly constructors, the inference engine SHALL
    // classify that member as InferredReadonly.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyInferencePropertyTests) })]
    public Property InferenceClassification_ReadonlySafeBody_InferredAsReadonly(
        InferenceClassificationScenario scenario)
    {
        var (result, model) = CompileAndCapture(scenario.Source);

        if(model is null)
        {
            return false.ToProperty()
                .Label($"Semantic model was not captured (compilation may have failed early).\nDiagnostics: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }

        var methodSymbol = FindMethodSymbol(model, scenario.TargetMemberName);
        if(methodSymbol is null)
        {
            return false.ToProperty()
                .Label($"Could not find method '{scenario.TargetMemberName}' in semantic model.\nSource:\n{scenario.Source}");
        }

        var isInferredReadonly = methodSymbol.ReadonlyStatus == ReadonlyStatus.InferredReadonly;

        return isInferredReadonly.ToProperty()
            .Label($"Expected '{scenario.TargetMemberName}' to be InferredReadonly but was {methodSymbol.ReadonlyStatus}.\nSource:\n{scenario.Source}");
    }
}