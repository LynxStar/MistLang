// Feature: target-typed-new, Property 4: Generic type preservation
// **Validates: Requirements 5.3**

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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for Property 4: Generic type preservation.
/// A target-typed new expression where the target type is a fully-constructed generic type.
/// </summary>
#pragma warning disable CA1034
public sealed class GenericTargetTypedNewScenario
{
    public string GenericClassName { get; }
    public string TypeArgPrimitive { get; }
    public string VarName { get; }
    public string Source { get; }
    public string Description { get; }

    public GenericTargetTypedNewScenario(
        string genericClassName,
        string typeArgPrimitive,
        string varName,
        string source,
        string description)
    {
        GenericClassName = genericClassName;
        TypeArgPrimitive = typeArgPrimitive;
        VarName = varName;
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Scenario for Property 4b: Multi-type-argument generic type preservation.
/// A target-typed new expression where the target type is a generic with 2 type parameters.
/// </summary>
public sealed class MultiArgGenericScenario
{
    public string GenericClassName { get; }
    public string FirstTypeArg { get; }
    public string SecondTypeArg { get; }
    public string VarName { get; }
    public string Source { get; }
    public string Description { get; }

    public MultiArgGenericScenario(
        string genericClassName,
        string firstTypeArg,
        string secondTypeArg,
        string varName,
        string source,
        string description)
    {
        GenericClassName = genericClassName;
        FirstTypeArg = firstTypeArg;
        SecondTypeArg = secondTypeArg;
        VarName = varName;
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}
#pragma warning restore CA1034

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests verifying generic type preservation for target-typed new expressions.
///
/// Property 4: For any generic type G&lt;T1, T2, ...&gt; used as a target type, the
/// target-typed new expression SHALL resolve to the fully-constructed generic type including
/// all type arguments, not the open generic definition.
///
/// Uses in-process compilation via CompilerDriver with a CapturingSemanticModelBuilder
/// to access the semantic model and verify expression types. Zero process spawns.
/// </summary>
public sealed class TargetTypedNewGenericPropertyTests : IDisposable
{
    private readonly string outputDir;

    public TargetTypedNewGenericPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistTargetNewGenericPBT_" + Guid.NewGuid().ToString("N"));
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

    // ─── Generators ─────────────────────────────────────────────────────

    private static readonly string[] GenericClassNames = ["Pair", "Wrapper", "Container", "Holder", "Box"];
    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];
    private static readonly string[] VarNames = ["x", "y", "result", "value", "item"];

    public static Gen<GenericTargetTypedNewScenario> GenSingleArgScenario()
    {
        return from className in Gen.Elements(GenericClassNames)
               from typeArg in Gen.Elements(PrimitiveTypes)
               from varName in Gen.Elements(VarNames)
               let source = BuildSingleArgSource(className, typeArg, varName)
               select new GenericTargetTypedNewScenario(
                   className, typeArg, varName, source,
                   $"{className}<{typeArg}> {varName} = new(...);");
    }

    public static Gen<MultiArgGenericScenario> GenMultiArgScenario()
    {
        return from className in Gen.Elements(GenericClassNames)
               from firstArg in Gen.Elements(PrimitiveTypes)
               from secondArg in Gen.Elements(PrimitiveTypes)
               from varName in Gen.Elements(VarNames)
               let source = BuildMultiArgSource(className, firstArg, secondArg, varName)
               select new MultiArgGenericScenario(
                   className, firstArg, secondArg, varName, source,
                   $"{className}<{firstArg}, {secondArg}> {varName} = new(...);");
    }

    private static string BuildSingleArgSource(string className, string typeArg, string varName)
    {
        var ctorArg = GetDefaultLiteral(typeArg);
        return $@"class {className}<T>
{{
    public T Value;

    public {className}(T val)
    {{
        Value = val;
    }}
}}

class Program
{{
    static void Main()
    {{
        {className}<{typeArg}> {varName} = new({ctorArg});
    }}
}}";
    }

    private static string BuildMultiArgSource(string className, string firstArg, string secondArg, string varName)
    {
        var firstLiteral = GetDefaultLiteral(firstArg);
        var secondLiteral = GetDefaultLiteral(secondArg);
        return $@"class {className}<T, U>
{{
    public T First;
    public U Second;

    public {className}(T first, U second)
    {{
        First = first;
        Second = second;
    }}
}}

class Program
{{
    static void Main()
    {{
        {className}<{firstArg}, {secondArg}> {varName} = new({firstLiteral}, {secondLiteral});
    }}
}}";
    }

    private static string GetDefaultLiteral(string primitiveType)
    {
        return primitiveType switch
        {
            "int" => "42",
            "string" => "\"hello\"",
            "bool" => "true",
            _ => "0",
        };
    }

    /// <summary>
    /// The semantic model may represent primitive types by their keyword name (int, string, bool)
    /// or by their CLR name (Int32, String, Boolean) depending on whether stdlib is loaded.
    /// This method checks if the resolved DisplayName matches either form.
    /// </summary>
    private static bool MatchesPrimitiveType(string resolvedDisplayName, string keywordName)
    {
        return keywordName switch
        {
            "int" => resolvedDisplayName is "int" or "Int32",
            "string" => resolvedDisplayName is "string" or "String",
            "bool" => resolvedDisplayName is "bool" or "Boolean",
            _ => resolvedDisplayName == keywordName,
        };
    }

    /// <summary>
    /// Returns the CLR name equivalent for a primitive keyword, for display name comparison.
    /// </summary>
    private static string GetClrName(string keywordName)
    {
        return keywordName switch
        {
            "int" => "Int32",
            "string" => "String",
            "bool" => "Boolean",
            _ => keywordName,
        };
    }

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<GenericTargetTypedNewScenario> ArbitraryGenericTargetTypedNewScenario()
    {
        return GenSingleArgScenario().ToArbitrary();
    }

    public static Arbitrary<MultiArgGenericScenario> ArbitraryMultiArgGenericScenario()
    {
        return GenMultiArgScenario().ToArbitrary();
    }

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
    // Property 4a: Single type argument preservation
    //
    // **Validates: Requirements 5.3**
    //
    // For any generic type G<T> used as a target type with a concrete type
    // argument, the target-typed new expression SHALL resolve to a
    // ConstructedType whose GenericDefinition is G and whose TypeArguments
    // contain the concrete type.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TargetTypedNewGenericPropertyTests) })]
    public Property SingleTypeArg_ResolvesToFullyConstructedType(GenericTargetTypedNewScenario scenario)
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

        // Find TargetTypedNewExpression nodes in the user AST (last AST)
        var userAst = model.Asts[^1];
        var targetNewNodes = FindTargetTypedNewNodes(userAst);

        if(targetNewNodes.Count == 0)
        {
            return false.ToProperty()
                .Label($"No TargetTypedNewExpression nodes found in user AST.\nSource:\n{scenario.Source}");
        }

        var targetNewNode = targetNewNodes[0];
        var exprType = model.GetTypeOf(targetNewNode);

        if(exprType is null)
        {
            return false.ToProperty()
                .Label($"Expression type is null for target-typed new node.\nSource:\n{scenario.Source}");
        }

        // Must be a ConstructedType (fully-constructed generic, not the open definition)
        if(exprType is not ConstructedType constructedType)
        {
            return false.ToProperty()
                .Label($"Expected ConstructedType but got {exprType.GetType().Name} ({exprType.DisplayName}).\nSource:\n{scenario.Source}");
        }

        // GenericDefinition should be a ClassType with the correct name
        if(constructedType.GenericDefinition is not ClassType genericDef)
        {
            return false.ToProperty()
                .Label($"GenericDefinition is not a ClassType, got {constructedType.GenericDefinition.GetType().Name}.\nSource:\n{scenario.Source}");
        }

        if(genericDef.DisplayName != scenario.GenericClassName)
        {
            return false.ToProperty()
                .Label($"Expected GenericDefinition name '{scenario.GenericClassName}' but got '{genericDef.DisplayName}'.\nSource:\n{scenario.Source}");
        }

        // Should have exactly 1 type argument
        if(constructedType.TypeArguments.Count != 1)
        {
            return false.ToProperty()
                .Label($"Expected 1 type argument but got {constructedType.TypeArguments.Count}.\nSource:\n{scenario.Source}");
        }

        // Type argument should match the expected primitive type
        var typeArgName = constructedType.TypeArguments[0].DisplayName;
        if(!MatchesPrimitiveType(typeArgName, scenario.TypeArgPrimitive))
        {
            return false.ToProperty()
                .Label($"Expected type argument '{scenario.TypeArgPrimitive}' (or '{GetClrName(scenario.TypeArgPrimitive)}') but got '{typeArgName}'.\nSource:\n{scenario.Source}");
        }

        // DisplayName should be the fully-constructed form (either keyword or CLR name)
        var expectedKeyword = $"{scenario.GenericClassName}<{scenario.TypeArgPrimitive}>";
        var expectedClr = $"{scenario.GenericClassName}<{GetClrName(scenario.TypeArgPrimitive)}>";
        if(constructedType.DisplayName != expectedKeyword && constructedType.DisplayName != expectedClr)
        {
            return false.ToProperty()
                .Label($"Expected DisplayName '{expectedKeyword}' or '{expectedClr}' but got '{constructedType.DisplayName}'.\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4b: Multiple type argument preservation
    //
    // **Validates: Requirements 5.3**
    //
    // For any generic type G<T, U> used as a target type with concrete type
    // arguments, the target-typed new expression SHALL resolve to a
    // ConstructedType whose TypeArguments contain ALL concrete types in order.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TargetTypedNewGenericPropertyTests) })]
    public Property MultipleTypeArgs_ResolvesToFullyConstructedType(MultiArgGenericScenario scenario)
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

        // Find TargetTypedNewExpression nodes in the user AST (last AST)
        var userAst = model.Asts[^1];
        var targetNewNodes = FindTargetTypedNewNodes(userAst);

        if(targetNewNodes.Count == 0)
        {
            return false.ToProperty()
                .Label($"No TargetTypedNewExpression nodes found in user AST.\nSource:\n{scenario.Source}");
        }

        var targetNewNode = targetNewNodes[0];
        var exprType = model.GetTypeOf(targetNewNode);

        if(exprType is null)
        {
            return false.ToProperty()
                .Label($"Expression type is null for target-typed new node.\nSource:\n{scenario.Source}");
        }

        // Must be a ConstructedType
        if(exprType is not ConstructedType constructedType)
        {
            return false.ToProperty()
                .Label($"Expected ConstructedType but got {exprType.GetType().Name} ({exprType.DisplayName}).\nSource:\n{scenario.Source}");
        }

        // GenericDefinition should be a ClassType with the correct name
        if(constructedType.GenericDefinition is not ClassType genericDef)
        {
            return false.ToProperty()
                .Label($"GenericDefinition is not a ClassType, got {constructedType.GenericDefinition.GetType().Name}.\nSource:\n{scenario.Source}");
        }

        if(genericDef.DisplayName != scenario.GenericClassName)
        {
            return false.ToProperty()
                .Label($"Expected GenericDefinition name '{scenario.GenericClassName}' but got '{genericDef.DisplayName}'.\nSource:\n{scenario.Source}");
        }

        // Should have exactly 2 type arguments
        if(constructedType.TypeArguments.Count != 2)
        {
            return false.ToProperty()
                .Label($"Expected 2 type arguments but got {constructedType.TypeArguments.Count}.\nSource:\n{scenario.Source}");
        }

        // First type argument should match
        var firstArgName = constructedType.TypeArguments[0].DisplayName;
        if(!MatchesPrimitiveType(firstArgName, scenario.FirstTypeArg))
        {
            return false.ToProperty()
                .Label($"Expected first type argument '{scenario.FirstTypeArg}' (or '{GetClrName(scenario.FirstTypeArg)}') but got '{firstArgName}'.\nSource:\n{scenario.Source}");
        }

        // Second type argument should match
        var secondArgName = constructedType.TypeArguments[1].DisplayName;
        if(!MatchesPrimitiveType(secondArgName, scenario.SecondTypeArg))
        {
            return false.ToProperty()
                .Label($"Expected second type argument '{scenario.SecondTypeArg}' (or '{GetClrName(scenario.SecondTypeArg)}') but got '{secondArgName}'.\nSource:\n{scenario.Source}");
        }

        // DisplayName should be the fully-constructed form (either keyword or CLR name)
        var expectedKeyword = $"{scenario.GenericClassName}<{scenario.FirstTypeArg}, {scenario.SecondTypeArg}>";
        var expectedClr = $"{scenario.GenericClassName}<{GetClrName(scenario.FirstTypeArg)}, {GetClrName(scenario.SecondTypeArg)}>";
        if(constructedType.DisplayName != expectedKeyword && constructedType.DisplayName != expectedClr)
        {
            return false.ToProperty()
                .Label($"Expected DisplayName '{expectedKeyword}' or '{expectedClr}' but got '{constructedType.DisplayName}'.\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}