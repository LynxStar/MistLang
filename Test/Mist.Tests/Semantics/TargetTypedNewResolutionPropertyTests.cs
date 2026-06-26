// Feature: target-typed-new, Property 3: Target type resolution from context
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

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

/// <summary>
/// The kind of typed context in which the target-typed new expression appears.
/// </summary>
public enum TargetTypedNewContext
{
    /// <summary>Field initializer with declared type.</summary>
    FieldInitializer,

    /// <summary>Local variable with explicit type annotation.</summary>
    TypedLocal,

    /// <summary>Return statement in a method with declared return type.</summary>
    ReturnStatement,

    /// <summary>Argument to a method call where the parameter type is known.</summary>
    MethodArgument,

    /// <summary>Assignment to an existing typed variable.</summary>
    Assignment,
}

/// <summary>
/// A test scenario for Property 3: target type resolution from context.
/// Contains the generated source, the expected type name, and the context kind.
/// </summary>
public sealed class TargetTypedNewResolutionScenario
{
    public string Source { get; }
    public string ExpectedTypeName { get; }
    public TargetTypedNewContext Context { get; }
    public string Description { get; }

    public TargetTypedNewResolutionScenario(
        string source, string expectedTypeName, TargetTypedNewContext context, string description)
    {
        Source = source;
        ExpectedTypeName = expectedTypeName;
        Context = context;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Generators for target-typed new resolution scenarios covering all typed contexts.
/// </summary>
public static class TargetTypedNewResolutionGenerators
{
    private static readonly string[] ClassNames = ["Point", "Config", "State", "Item", "Node"];
    private static readonly string[] FieldNames = ["_value", "_data", "_item", "_node", "_state"];
    private static readonly string[] VarNames = ["x", "y", "result", "temp", "val"];
    private static readonly string[] MethodNames = ["Create", "Make", "Build", "Get", "Produce"];

    public static Gen<TargetTypedNewResolutionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenFieldInitializer(),
            GenTypedLocal(),
            GenReturnStatement(),
            GenMethodArgument(),
            GenAssignment());
    }

    /// <summary>
    /// Generates a field initializer context: <c>public ClassName Field = new(args);</c>
    /// Validates: Requirement 3.1
    /// </summary>
    private static Gen<TargetTypedNewResolutionScenario> GenFieldInitializer()
    {
        return from classIdx in Gen.Choose(0, ClassNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               from argCount in Gen.Choose(0, 2)
               let className = ClassNames[classIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = BuildFieldInitializerSource(className, fieldName, argCount)
               select new TargetTypedNewResolutionScenario(
                   source, className, TargetTypedNewContext.FieldInitializer,
                   $"Field initializer: {className} {fieldName} = new({BuildCtorArgs(argCount)})");
    }

    /// <summary>
    /// Generates a typed local variable context: <c>ClassName x = new(args);</c>
    /// Validates: Requirement 3.2
    /// </summary>
    private static Gen<TargetTypedNewResolutionScenario> GenTypedLocal()
    {
        return from classIdx in Gen.Choose(0, ClassNames.Length - 1)
               from varIdx in Gen.Choose(0, VarNames.Length - 1)
               from argCount in Gen.Choose(0, 2)
               let className = ClassNames[classIdx]
               let varName = VarNames[varIdx]
               let source = BuildTypedLocalSource(className, varName, argCount)
               select new TargetTypedNewResolutionScenario(
                   source, className, TargetTypedNewContext.TypedLocal,
                   $"Typed local: {className} {varName} = new({BuildCtorArgs(argCount)})");
    }

    /// <summary>
    /// Generates a return statement context: method returning ClassName -> <c>return new(args);</c>
    /// Validates: Requirement 3.3
    /// </summary>
    private static Gen<TargetTypedNewResolutionScenario> GenReturnStatement()
    {
        return from classIdx in Gen.Choose(0, ClassNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from argCount in Gen.Choose(0, 2)
               let className = ClassNames[classIdx]
               let methodName = MethodNames[methodIdx]
               let source = BuildReturnStatementSource(className, methodName, argCount)
               select new TargetTypedNewResolutionScenario(
                   source, className, TargetTypedNewContext.ReturnStatement,
                   $"Return: {className} {methodName}() => return new({BuildCtorArgs(argCount)})");
    }

    /// <summary>
    /// Generates a method argument context: <c>Consume(new(args))</c>
    /// Validates: Requirement 3.4
    /// </summary>
    private static Gen<TargetTypedNewResolutionScenario> GenMethodArgument()
    {
        return from classIdx in Gen.Choose(0, ClassNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from argCount in Gen.Choose(0, 2)
               let className = ClassNames[classIdx]
               let methodName = MethodNames[methodIdx]
               let source = BuildMethodArgumentSource(className, methodName, argCount)
               select new TargetTypedNewResolutionScenario(
                   source, className, TargetTypedNewContext.MethodArgument,
                   $"Argument: {methodName}(new({BuildCtorArgs(argCount)}))");
    }

    /// <summary>
    /// Generates an assignment context: <c>x = new(args);</c>
    /// Validates: Requirement 3.5
    /// </summary>
    private static Gen<TargetTypedNewResolutionScenario> GenAssignment()
    {
        return from classIdx in Gen.Choose(0, ClassNames.Length - 1)
               from varIdx in Gen.Choose(0, VarNames.Length - 1)
               from argCount in Gen.Choose(0, 2)
               let className = ClassNames[classIdx]
               let varName = VarNames[varIdx]
               let source = BuildAssignmentSource(className, varName, argCount)
               select new TargetTypedNewResolutionScenario(
                   source, className, TargetTypedNewContext.Assignment,
                   $"Assignment: {varName} = new({BuildCtorArgs(argCount)})");
    }

    // ─── Source builders ────────────────────────────────────────────────────

    private static string BuildCtorParams(int argCount)
    {
        return argCount switch
        {
            1 => "int a",
            2 => "int a, int b",
            _ => "",
        };
    }

    private static string BuildCtorArgs(int argCount)
    {
        return argCount switch
        {
            1 => "1",
            2 => "1, 2",
            _ => "",
        };
    }

    private static string BuildCtorDecl(string className, int argCount)
    {
        if(argCount == 0)
        {
            return "";
        }

        var parms = BuildCtorParams(argCount);
        return "    public " + className + "(" + parms + ") { }\n";
    }

    private static string BuildFieldInitializerSource(string className, string fieldName, int argCount)
    {
        var args = BuildCtorArgs(argCount);
        return "class " + className + " {\n" +
               BuildCtorDecl(className, argCount) +
               "}\n\n" +
               "class Container {\n" +
               "    public " + className + " " + fieldName + " = new(" + args + ");\n" +
               "}";
    }

    private static string BuildTypedLocalSource(string className, string varName, int argCount)
    {
        var args = BuildCtorArgs(argCount);
        return "class " + className + " {\n" +
               BuildCtorDecl(className, argCount) +
               "}\n\n" +
               "class Program {\n" +
               "    void Run() {\n" +
               "        " + className + " " + varName + " = new(" + args + ");\n" +
               "    }\n" +
               "}";
    }

    private static string BuildReturnStatementSource(string className, string methodName, int argCount)
    {
        var args = BuildCtorArgs(argCount);
        return "class " + className + " {\n" +
               BuildCtorDecl(className, argCount) +
               "}\n\n" +
               "class Program {\n" +
               "    " + className + " " + methodName + "() {\n" +
               "        return new(" + args + ");\n" +
               "    }\n" +
               "}";
    }

    private static string BuildMethodArgumentSource(string className, string methodName, int argCount)
    {
        var args = BuildCtorArgs(argCount);
        return "class " + className + " {\n" +
               BuildCtorDecl(className, argCount) +
               "}\n\n" +
               "class Program {\n" +
               "    static void " + methodName + "(" + className + " p) { }\n\n" +
               "    void Run() {\n" +
               "        " + methodName + "(new(" + args + "));\n" +
               "    }\n" +
               "}";
    }

    private static string BuildAssignmentSource(string className, string varName, int argCount)
    {
        var args = BuildCtorArgs(argCount);
        return "class " + className + " {\n" +
               BuildCtorDecl(className, argCount) +
               "}\n\n" +
               "class Program {\n" +
               "    void Run() {\n" +
               "        " + className + " " + varName + " = new(" + args + ");\n" +
               "        " + varName + " = new(" + args + ");\n" +
               "    }\n" +
               "}";
    }
}

/// <summary>
/// Property-based tests for target type resolution from context.
///
/// Property 3: For any typed context (field initializer with declared type T, local variable
/// with declared type T, return statement in a method returning T, argument to a parameter
/// of type T, or assignment to a variable of type T), a target-typed new expression SHALL
/// resolve its expression type to T.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
/// </summary>
public sealed class TargetTypedNewResolutionPropertyTests : IDisposable
{
    private readonly string outputDir;

    public TargetTypedNewResolutionPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistTargetNewRes_" + Guid.NewGuid().ToString("N"));
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

    public static Arbitrary<TargetTypedNewResolutionScenario> ArbitraryScenario()
    {
        return TargetTypedNewResolutionGenerators.GenScenario().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private (CompilerResult Result, ISemanticModel? Model) CompileAndCapture(string source)
    {
        var filePath = Path.Combine(outputDir, "Test_" + Guid.NewGuid().ToString("N") + ".mist");
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

    /// <summary>
    /// Recursively finds all TargetTypedNewExpressionPrimaryNode instances in the AST.
    /// </summary>
    private static List<TargetTypedNewExpressionPrimaryNode> FindTargetTypedNewNodes(Storm.Runtime.SyntaxNode node)
    {
        var results = new List<TargetTypedNewExpressionPrimaryNode>();
        CollectTargetTypedNewNodes(node, results);
        return results;
    }

    private static void CollectTargetTypedNewNodes(Storm.Runtime.SyntaxNode? node, List<TargetTypedNewExpressionPrimaryNode> results)
    {
        if(node is null)
        {
            return;
        }

        if(node is TargetTypedNewExpressionPrimaryNode targetNew)
        {
            results.Add(targetNew);
        }

        try
        {
            foreach(var child in node.Children())
            {
                CollectTargetTypedNewNodes(child, results);
            }
        }
        catch(NullReferenceException)
        {
            // Some AST nodes may have null children that Children() doesn't guard against.
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Target type resolution from context
    //
    // For any typed context (field initializer with declared type T, local
    // variable with declared type T, return statement in a method returning T,
    // argument to a parameter of type T, or assignment to a variable of type T),
    // a target-typed new expression SHALL resolve its expression type to T.
    //
    // **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TargetTypedNewResolutionPropertyTests) })]
    public Property TargetTypedNew_ResolvesTypeFromContext(TargetTypedNewResolutionScenario scenario)
    {
        var (result, model) = CompileAndCapture(scenario.Source);

        // Should compile without errors (the type checker may emit warnings, but no errors).
        // Filter out MIST_TC_0017 (entry point not found) since we're testing type resolution,
        // not program structure.
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        if(errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Compilation failed with errors:\n" +
                       string.Join("\n", errors.Select(e => "  " + e.Id.Value + ": " + e.Message)) +
                       "\nContext: " + scenario.Context +
                       "\nSource:\n" + scenario.Source);
        }

        if(model is null)
        {
            return false.ToProperty()
                .Label("Semantic model was not captured.\nSource:\n" + scenario.Source);
        }

        // Find target-typed new expressions in the user AST (last AST, after stdlib)
        var userAst = model.Asts[^1];
        var targetNewNodes = FindTargetTypedNewNodes(userAst);

        if(targetNewNodes.Count == 0)
        {
            return false.ToProperty()
                .Label("No TargetTypedNewExpressionPrimaryNode found in user AST.\nContext: " +
                       scenario.Context + "\nSource:\n" + scenario.Source);
        }

        // For assignment scenarios, the second target-typed new (the reassignment) is the
        // one we primarily test. For other scenarios, use the last one found.
        var nodeToCheck = targetNewNodes[^1];
        var resolvedType = model.GetTypeOf(nodeToCheck);

        if(resolvedType is null)
        {
            return false.ToProperty()
                .Label("Expression type is null for target-typed new node.\nContext: " +
                       scenario.Context + "\nSource:\n" + scenario.Source);
        }

        var matchesExpected = resolvedType.DisplayName == scenario.ExpectedTypeName;

        return matchesExpected
            .ToProperty()
            .Label("Expected type '" + scenario.ExpectedTypeName + "' but got '" +
                   resolvedType.DisplayName + "'.\nContext: " + scenario.Context +
                   "\nSource:\n" + scenario.Source);
    }
}