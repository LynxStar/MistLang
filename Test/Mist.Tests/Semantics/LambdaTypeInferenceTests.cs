using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for lambda type inference in the semantic model.
///
/// Covers:
/// <list type="bullet">
///   <item>Target-type inference (Req 6.1, 6.2)</item>
///   <item><c>var</c> + explicit-param inference (Req 6.3, 6.4)</item>
///   <item>Block-body common-type inference (Req 6.5)</item>
///   <item>Async lambda wrapping</item>
///   <item>Multi-param and zero-param lambdas</item>
///   <item>Void block lambdas</item>
/// </list>
///
/// All tests run fully in-process via the <see cref="SemanticModelBuilder"/> with
/// stdlib loaded (no <c>dotnet</c> process spawning).
///
/// <b>Property 2: Lambda Type Inference Completeness</b>
/// <b>Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5</b>
/// </summary>
[Trait("Feature", "first-class-functions")]
[Trait("Property", "2: Lambda Type Inference Completeness")]
public sealed class LambdaTypeInferenceTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a Mist source string and builds a semantic model with stdlib loaded.
    /// Returns the model and diagnostics for assertion.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var (stdlibUnits, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            throw new InvalidOperationException($"Failed to parse user source:\n{source}");
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var model = Builder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Finds a local variable symbol by name in the semantic model.
    /// </summary>
    private static ISymbol? FindLocal(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.LocalVariable);
    }

    /// <summary>
    /// Asserts that a type is a Func constructed type with the given type argument display names.
    /// </summary>
    private static void AssertFuncType(IType? type, params string[] expectedTypeArgDisplayNames)
    {
        Assert.NotNull(type);
        var constructedType = Assert.IsType<ConstructedType>(type);
        var funcDef = Assert.IsType<ClassType>(constructedType.GenericDefinition);
        Assert.Equal("Func", funcDef.DisplayName);
        Assert.Equal("Standard", funcDef.ContainingNamespace);
        Assert.Equal(expectedTypeArgDisplayNames.Length, constructedType.TypeArguments.Count);

        for(var i = 0; i < expectedTypeArgDisplayNames.Length; i++)
        {
            var expected = expectedTypeArgDisplayNames[i];
            var actual = constructedType.TypeArguments[i].DisplayName;
            Assert.True(
                MatchesTypeName(actual, expected),
                $"Type argument [{i}]: expected '{expected}', got '{actual}'");
        }
    }

    /// <summary>
    /// Matches a type display name against an expected name, accounting for
    /// keyword vs. CLR name variants (e.g., "int" / "Int32", "string" / "String").
    /// </summary>
    private static bool MatchesTypeName(string actual, string expected)
    {
        if(actual == expected)
        {
            return true;
        }

        // Common keyword ↔ CLR name mappings.
        return expected switch
        {
            "int" => actual is "Int32" or "int",
            "Int32" => actual is "int" or "Int32",
            "string" => actual is "String" or "string",
            "String" => actual is "string" or "String",
            "bool" => actual is "Boolean" or "bool",
            "Boolean" => actual is "bool" or "Boolean",
            "void" => actual is "Void" or "void",
            "Void" => actual is "void" or "Void",
            _ => false,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Target-type inference (Req 6.1, 6.2):
    //    func<int, string> f = x => "hello";
    //    The lambda's param `x` should be inferred as `int`.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TargetTypeInference_ImplicitParam_InferredFromFuncType()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public void TestMethod()
                {
                    func<int, string> f = x => "hello";
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var localF = FindLocal(model, "f");
        Assert.NotNull(localF);
        Assert.NotNull(localF!.DeclaredType);

        // The declared type should be func<int, string>.
        AssertFuncType(localF.DeclaredType, "int", "string");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Var + explicit params (Req 6.3):
    //    var f = (int x) => x + 1;
    //    f should have type func<int, int>
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VarWithExplicitParams_IntToInt_InfersCorrectFuncType()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public void TestMethod()
                {
                    var f = (int x) => x + 1;
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var localF = FindLocal(model, "f");
        Assert.NotNull(localF);
        Assert.NotNull(localF!.DeclaredType);

        // var infers the type from the lambda: func<int, int>.
        AssertFuncType(localF.DeclaredType, "int", "int");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Var + explicit params, string return (Req 6.4):
    //    var f = (int x) => "hello";
    //    f should have type func<int, string>
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VarWithExplicitParams_IntToString_InfersCorrectFuncType()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public void TestMethod()
                {
                    var f = (int x) => "hello";
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var localF = FindLocal(model, "f");
        Assert.NotNull(localF);
        Assert.NotNull(localF!.DeclaredType);

        // var infers the type from the lambda: func<int, string>.
        AssertFuncType(localF.DeclaredType, "int", "string");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Block body common type (Req 6.5):
    //    var f = (int x) => { return x + 1; };
    //    f should have type func<int, int>
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VarWithBlockBody_ReturnIntExpression_InfersCorrectFuncType()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public void TestMethod()
                {
                    var f = (int x) => { return x + 1; };
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var localF = FindLocal(model, "f");
        Assert.NotNull(localF);
        Assert.NotNull(localF!.DeclaredType);

        // Block body with return x + 1 → func<int, int>.
        AssertFuncType(localF.DeclaredType, "int", "int");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Void block lambda:
    //    var f = (int x) => { };
    //    f should have type func<int, void>
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VarWithEmptyBlockBody_InfersVoidReturnType()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public void TestMethod()
                {
                    var f = (int x) => { };
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var localF = FindLocal(model, "f");
        Assert.NotNull(localF);
        Assert.NotNull(localF!.DeclaredType);

        // Empty block body → func<int, void>.
        AssertFuncType(localF.DeclaredType, "int", "void");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Async lambda wrapping:
    //    var f = async (int x) => x;
    //    f should have type func<int, Task<int>>
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AsyncLambda_WrapsReturnTypeInTask()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public void TestMethod()
                {
                    var f = async (int x) => x;
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var localF = FindLocal(model, "f");
        Assert.NotNull(localF);
        Assert.NotNull(localF!.DeclaredType);

        // Async lambda: func<int, Task<int>>.
        var constructedType = Assert.IsType<ConstructedType>(localF.DeclaredType);
        var funcDef = Assert.IsType<ClassType>(constructedType.GenericDefinition);
        Assert.Equal("Func", funcDef.DisplayName);
        Assert.Equal("Standard", funcDef.ContainingNamespace);
        Assert.Equal(2, constructedType.TypeArguments.Count);

        // First type arg: int (the parameter type).
        Assert.True(
            MatchesTypeName(constructedType.TypeArguments[0].DisplayName, "int"),
            $"Expected first type arg 'int', got '{constructedType.TypeArguments[0].DisplayName}'");

        // Second type arg: Task<int> (the async-wrapped return type).
        var returnType = constructedType.TypeArguments[1];
        var taskType = Assert.IsType<ConstructedType>(returnType);
        var taskDef = Assert.IsType<ClassType>(taskType.GenericDefinition);
        Assert.Equal("Task", taskDef.DisplayName);
        _ = Assert.Single(taskType.TypeArguments);
        Assert.True(
            MatchesTypeName(taskType.TypeArguments[0].DisplayName, "int"),
            $"Expected Task inner type 'int', got '{taskType.TypeArguments[0].DisplayName}'");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Multi-param lambda:
    //    var f = (int x, string y) => x;
    //    f should have type func<int, string, int>
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultiParamLambda_InfersCorrectFuncType()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public void TestMethod()
                {
                    var f = (int x, string y) => x;
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var localF = FindLocal(model, "f");
        Assert.NotNull(localF);
        Assert.NotNull(localF!.DeclaredType);

        // Multi-param: func<int, string, int>.
        AssertFuncType(localF.DeclaredType, "int", "string", "int");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. Zero-param lambda:
    //    var f = () => 42;
    //    f should have type func<int>
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ZeroParamLambda_InfersCorrectFuncType()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public void TestMethod()
                {
                    var f = () => 42;
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var localF = FindLocal(model, "f");
        Assert.NotNull(localF);
        Assert.NotNull(localF!.DeclaredType);

        // Zero params: func<int> (just the return type).
        AssertFuncType(localF.DeclaredType, "int");
    }
}