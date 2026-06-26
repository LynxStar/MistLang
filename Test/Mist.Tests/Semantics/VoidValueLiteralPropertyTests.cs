// Feature: void-value-literal, Property 1: Target-type-driven void resolution
// Feature: void-value-literal, Property 2: Implicit Void → Type conversion acceptance
// Feature: void-value-literal, Property 3: Void.Void deprecation warning count
// **Validates: Requirements 1.1, 1.6, 2.1, 3.1, 4.2, 4.3, 5.2, 5.3**

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
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for Property 1: Target-type-driven void resolution.
/// A void literal in expression position with a specific target type context.
/// </summary>
public sealed class VoidResolutionScenario
{
    public string Source { get; }
    public string TargetType { get; }
    public bool ExpectVoidType { get; }
    public string Description { get; }

    public VoidResolutionScenario(string source, string targetType, bool expectVoidType, string description)
    {
        Source = source;
        TargetType = targetType;
        ExpectVoidType = expectVoidType;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for Property 2: Implicit Void → Type conversion acceptance.
/// A void-typed variable assigned to a Type-typed target in various positions.
/// </summary>
public sealed class VoidToTypeConversionScenario
{
    public string Source { get; }
    public string Description { get; }

    public VoidToTypeConversionScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for Property 3: Void.Void deprecation warning count.
/// A program with N occurrences of Void.Void.
/// </summary>
public sealed class VoidDotVoidCountScenario
{
    public string Source { get; }
    public int ExpectedWarningCount { get; }
    public string Description { get; }

    public VoidDotVoidCountScenario(string source, int expectedWarningCount, string description)
    {
        Source = source;
        ExpectedWarningCount = expectedWarningCount;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class VoidValueLiteralGenerators
{
    private static readonly string[] VarNames = ["x", "y", "result", "value", "temp", "unit", "v"];
    private static readonly string[] MethodNames = ["DoWork", "Process", "Execute", "Run", "Handle"];

    // ─── Property 1: Target-type-driven void resolution ─────────────────

    /// <summary>
    /// Generates scenarios where void literal appears with various target types.
    /// When target is void → expression type should be VoidType.
    /// When target is Type, int, string, or var → expression type should be Standard.Type.
    /// </summary>
    public static Gen<VoidResolutionScenario> GenVoidResolution()
    {
        return Gen.OneOf(
            GenVoidTargetVoid(),
            GenVoidTargetType(),
            GenVoidTargetVar(),
            GenVoidReturnVoid(),
            GenVoidArgumentVoid());
    }

    private static Gen<VoidResolutionScenario> GenVoidTargetVoid()
    {
        return from varName in Gen.Elements(VarNames)
               let source = $@"class Program {{
    static void Main() {{
        void {varName} = void;
    }}
}}"
               select new VoidResolutionScenario(source, "void", true,
                   $"void {varName} = void; → target is void, expect VoidType");
    }

    private static Gen<VoidResolutionScenario> GenVoidTargetType()
    {
        return from varName in Gen.Elements(VarNames)
               let source = $@"using Standard;

class Program {{
    static void Main() {{
        Type {varName} = void;
    }}
}}"
               select new VoidResolutionScenario(source, "Type", false,
                   $"Type {varName} = void; → target is Type, expect Standard.Type");
    }

    private static Gen<VoidResolutionScenario> GenVoidTargetVar()
    {
        return from varName in Gen.Elements(VarNames)
               let source = $@"class Program {{
    static void Main() {{
        var {varName} = void;
    }}
}}"
               select new VoidResolutionScenario(source, "var", false,
                   $"var {varName} = void; → no target type, expect Standard.Type");
    }

    private static Gen<VoidResolutionScenario> GenVoidReturnVoid()
    {
        return from methodName in Gen.Elements(MethodNames)
               from varName in Gen.Elements(VarNames)
               let source = $@"class Program {{
    static void Main() {{
        void {varName} = void;
    }}

    void {methodName}() {{
        return void;
    }}
}}"
               select new VoidResolutionScenario(source, "void", true,
                   $"void {varName} = void; (with void-returning method '{methodName}') → target is void, expect VoidType");
    }

    private static Gen<VoidResolutionScenario> GenVoidArgumentVoid()
    {
        return from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(VarNames)
               from varName in Gen.Elements(VarNames)
               where varName != paramName
               let source = $@"class Program {{
    static void Main() {{
        void {varName} = void;
    }}

    static void {methodName}(void {paramName}) {{ }}
}}"
               select new VoidResolutionScenario(source, "void", true,
                   $"void {varName} = void; (with void-param method '{methodName}') → target is void, expect VoidType");
    }

    // ─── Property 2: Implicit Void → Type conversion acceptance ─────────

    /// <summary>
    /// Generates scenarios where a void-typed variable is assigned to a Type target.
    /// All should compile without errors via the implicit conversion operator.
    /// </summary>
    public static Gen<VoidToTypeConversionScenario> GenVoidToTypeConversion()
    {
        return Gen.OneOf(
            GenConversionVariableAssignment(),
            GenConversionMethodArgument(),
            GenConversionReturnPosition());
    }

    private static Gen<VoidToTypeConversionScenario> GenConversionVariableAssignment()
    {
        return from voidVar in Gen.Elements(VarNames)
               from typeVar in Gen.Elements(["t", "typeVal", "result", "output", "target"])
               where voidVar != typeVar
               let source = $@"using Standard;

class Program {{
    static void Main() {{
        void {voidVar} = void;
        Type {typeVar} = {voidVar};
    }}
}}"
               select new VoidToTypeConversionScenario(source,
                   $"void {voidVar} = void; Type {typeVar} = {voidVar}; → implicit conversion in variable assignment");
    }

    private static Gen<VoidToTypeConversionScenario> GenConversionMethodArgument()
    {
        return from voidVar in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"using Standard;

class Program {{
    static void Main() {{
        void {voidVar} = void;
        {methodName}({voidVar});
    }}

    static void {methodName}(Type t) {{ }}
}}"
               select new VoidToTypeConversionScenario(source,
                   $"void {voidVar} passed to Type param in {methodName}() → implicit conversion in argument");
    }

    private static Gen<VoidToTypeConversionScenario> GenConversionReturnPosition()
    {
        return from methodName in Gen.Elements(MethodNames)
               from voidVar in Gen.Elements(VarNames)
               let source = $@"using Standard;

class Program {{
    static void Main() {{ }}

    static Type {methodName}() {{
        void {voidVar} = void;
        return {voidVar};
    }}
}}"
               select new VoidToTypeConversionScenario(source,
                   $"return {voidVar}; in Type-returning method '{methodName}' → implicit conversion in return");
    }

    // ─── Property 3: Void.Void deprecation warning count ────────────────

    /// <summary>
    /// Generates programs with 1–5 Void.Void occurrences in random positions.
    /// The compiler should emit exactly N warnings with ID MIST_SM_0023.
    /// </summary>
    public static Gen<VoidDotVoidCountScenario> GenVoidDotVoidCount()
    {
        return from count in Gen.Choose(1, 5)
               from scenario in GenVoidDotVoidWithCount(count)
               select scenario;
    }

    private static Gen<VoidDotVoidCountScenario> GenVoidDotVoidWithCount(int count)
    {
        // Generate `count` statements using Void.Void in various positions
        return from varNames in Gen.Elements(VarNames).ArrayOf(count)
               let statements = BuildVoidDotVoidStatements(varNames)
               let source = $@"class Program {{
    static void Main() {{
{statements}
    }}
}}"
               select new VoidDotVoidCountScenario(source, count,
                   $"{count} Void.Void occurrence(s) → expect {count} MIST_SM_0023 warning(s)");
    }

    private static string BuildVoidDotVoidStatements(string[] varNames)
    {
        var lines = new List<string>();
        for(var i = 0; i < varNames.Length; i++)
        {
            // Use unique variable names to avoid redeclaration errors
            var uniqueName = $"{varNames[i]}{i}";
            lines.Add($"        void {uniqueName} = Void.Void;");
        }
        return string.Join("\n", lines);
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for void value literal resolution, implicit conversion,
/// and deprecation warning count.
///
/// Properties 1, 2, and 3 from the void-value-literal design document.
/// Uses in-process compilation via CompilerDriver with a CapturingSemanticModelBuilder
/// to access the semantic model and verify expression types. Zero process spawns.
/// </summary>
public sealed class VoidValueLiteralPropertyTests : IDisposable
{
    private readonly string outputDir;

    public VoidValueLiteralPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVoidValLitPBT_" + Guid.NewGuid().ToString("N"));
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

    public static Arbitrary<VoidResolutionScenario> ArbitraryVoidResolution() =>
        VoidValueLiteralGenerators.GenVoidResolution().ToArbitrary();

    public static Arbitrary<VoidToTypeConversionScenario> ArbitraryVoidToTypeConversion() =>
        VoidValueLiteralGenerators.GenVoidToTypeConversion().ToArbitrary();

    public static Arbitrary<VoidDotVoidCountScenario> ArbitraryVoidDotVoidCount() =>
        VoidValueLiteralGenerators.GenVoidDotVoidCount().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    private (CompilerResult Result, ISemanticModel? Model) CompileAndCapture(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = driver.Compile(options);
        return (result, capture.Captured);
    }

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return driver.Compile(options);
    }

    /// <summary>
    /// Recursively finds all TypeLiteralExpressionPrimaryNode instances in the AST
    /// whose TypeRef is a VoidTypeRefTypeReferenceNode.
    /// </summary>
    private static List<TypeLiteralExpressionPrimaryNode> FindVoidTypeLiterals(Storm.Runtime.SyntaxNode node)
    {
        var results = new List<TypeLiteralExpressionPrimaryNode>();
        CollectVoidTypeLiterals(node, results);
        return results;
    }

    private static void CollectVoidTypeLiterals(Storm.Runtime.SyntaxNode? node, List<TypeLiteralExpressionPrimaryNode> results)
    {
        if(node is null)
        {
            return;
        }

        if(node is TypeLiteralExpressionPrimaryNode typeLiteral
            && typeLiteral.TypeRef is { SingleTypeRefs.Length: 0, First: VoidSingleTypeRefSingleTypeRefNode })
        {
            results.Add(typeLiteral);
        }

        try
        {
            foreach(var child in node.Children())
            {
                CollectVoidTypeLiterals(child, results);
            }
        }
        catch(NullReferenceException)
        {
            // Some AST nodes may have null children that Children() doesn't guard against.
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Target-type-driven void resolution
    //
    // **Validates: Requirements 1.1, 1.6, 2.1, 3.1**
    //
    // For any program containing the `void` keyword in expression position
    // with a known target type, the semantic model SHALL resolve the expression
    // type as VoidType if and only if the target type is VoidType. When the
    // target type is not VoidType or when no target type is available, the
    // expression type SHALL be Standard.Type.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(VoidValueLiteralPropertyTests) })]
    public Property TargetTypeDrivenVoidResolution(VoidResolutionScenario scenario)
    {
        var (result, model) = CompileAndCapture(scenario.Source);

        // Should compile without errors
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Compilation failed with errors:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}\nSource:\n{scenario.Source}");
        }

        if(model is null)
        {
            return false.ToProperty()
                .Label($"Semantic model was not captured.\nSource:\n{scenario.Source}");
        }

        // Find void type literals in the user AST (last AST, after stdlib)
        var userAst = model.Asts[^1];
        var voidLiterals = FindVoidTypeLiterals(userAst);

        if(voidLiterals.Count == 0)
        {
            return false.ToProperty()
                .Label($"No void type literals found in user AST.\nSource:\n{scenario.Source}");
        }

        // Check the last void literal (the one in expression position, not type annotations)
        var exprLiteral = voidLiterals.Last();
        var exprType = model.GetTypeOf(exprLiteral);

        if(exprType is null)
        {
            return false.ToProperty()
                .Label($"Expression type is null for void literal.\nSource:\n{scenario.Source}");
        }

        var isVoidType = exprType.IsVoid();

        if(scenario.ExpectVoidType)
        {
            return isVoidType.ToProperty()
                .Label($"Expected VoidType but got {exprType.DisplayName} (target={scenario.TargetType}).\nSource:\n{scenario.Source}");
        }
        else
        {
            return (!isVoidType).ToProperty()
                .Label($"Expected Standard.Type but got VoidType (target={scenario.TargetType}).\nSource:\n{scenario.Source}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Implicit Void → Type conversion acceptance
    //
    // **Validates: Requirements 4.2, 4.3**
    //
    // For any program where a void-typed expression is assigned to a
    // Type-typed target, the type checker SHALL accept the assignment
    // without error via the implicit conversion operator.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(VoidValueLiteralPropertyTests) })]
    public Property ImplicitVoidToTypeConversion_NoErrors(VoidToTypeConversionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var noErrors = errors.Count == 0;

        return noErrors.ToProperty()
            .Label($"Expected no errors but got:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Void.Void deprecation warning count
    //
    // **Validates: Requirements 5.2, 5.3**
    //
    // For any program containing N occurrences of the Void.Void member
    // access expression, the compiler SHALL emit exactly N warning
    // diagnostics with ID MIST_SM_0023.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(VoidValueLiteralPropertyTests) })]
    public Property VoidDotVoidDeprecationWarningCount(VoidDotVoidCountScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // Should compile without errors (warnings are expected)
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Compilation failed with errors:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}\nSource:\n{scenario.Source}");
        }

        var deprecationWarnings = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning && d.Id.Value == "MIST_SM_0023")
            .ToList();

        var countMatches = deprecationWarnings.Count == scenario.ExpectedWarningCount;

        return countMatches.ToProperty()
            .Label($"Expected {scenario.ExpectedWarningCount} MIST_SM_0023 warnings but got {deprecationWarnings.Count}.\nSource:\n{scenario.Source}");
    }
}