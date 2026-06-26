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
/// Unit tests for Func type resolution and invocation in the semantic model.
///
/// Covers:
/// <list type="bullet">
///   <item><c>func&lt;int, string&gt;</c> / <c>Func&lt;int, string&gt;</c> structural equality (Property 1)</item>
///   <item>Func-typed invocation return types</item>
///   <item>Method group conversion</item>
///   <item>Arity variants (<c>func&lt;bool&gt;</c>, <c>func&lt;int, int, int&gt;</c>)</item>
/// </list>
///
/// All tests run fully in-process via the <see cref="SemanticModelBuilder"/> with
/// stdlib loaded (no <c>dotnet</c> process spawning).
///
/// <b>Property 1: Func Type Structural Equality</b>
/// <b>Validates: Requirements 1.6, 2.3, 7.6, 7.7, 16.4</b>
/// </summary>
public sealed class FuncTypeResolutionTests
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
    /// Finds a field symbol by name in the semantic model.
    /// </summary>
    private static ISymbol? FindField(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Field);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Structural equality: func<int, string> and Func<int, string>
    //    resolve to the same type (reference equality of the generic definition)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FuncType_KeywordAndNamedForm_ResolveToSameGenericDefinition()
    {
        // Both `func<int, string>` and `Func<int, string>` should resolve to
        // the same Standard.Func generic definition (reference equality).
        // Note: `Func` is in the Standard namespace, which is auto-imported
        // for stdlib types. We use `using Standard;` to make it visible.
        const string source = """
            using Standard;
            namespace TestProject;
            public class TestClass
            {
                public func<int, string> keywordField;
                public Func<int, string> namedField;
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        // No fatal semantic errors expected.
        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var keywordField = FindField(model, "keywordField");
        Assert.NotNull(keywordField);
        Assert.NotNull(keywordField!.DeclaredType);

        var namedField = FindField(model, "namedField");
        Assert.NotNull(namedField);
        Assert.NotNull(namedField!.DeclaredType);

        var keywordType = Assert.IsType<ConstructedType>(keywordField.DeclaredType);
        var namedType = Assert.IsType<ConstructedType>(namedField.DeclaredType);

        // The generic definitions must be reference-equal (same Standard.Func class).
        Assert.Same(keywordType.GenericDefinition, namedType.GenericDefinition);

        // Both should be Standard.Func with IsBackendBacked = true.
        var funcDef = Assert.IsType<ClassType>(keywordType.GenericDefinition);
        Assert.Equal("Func", funcDef.DisplayName);
        Assert.Equal("Standard", funcDef.ContainingNamespace);
        Assert.True(funcDef.IsBackendBacked);
    }

    [Fact]
    public void FuncType_KeywordAndNamedForm_HaveSameTypeArguments()
    {
        const string source = """
            using Standard;
            namespace TestProject;
            public class TestClass
            {
                public func<int, string> keywordField;
                public Func<int, string> namedField;
            }
            """;

        var (model, _) = BuildModel(source);

        var keywordField = FindField(model, "keywordField");
        var namedField = FindField(model, "namedField");

        Assert.NotNull(keywordField?.DeclaredType);
        Assert.NotNull(namedField?.DeclaredType);

        var keywordType = Assert.IsType<ConstructedType>(keywordField!.DeclaredType);
        var namedType = Assert.IsType<ConstructedType>(namedField!.DeclaredType);

        // Type arguments should match: [int, string].
        Assert.Equal(keywordType.TypeArguments.Count, namedType.TypeArguments.Count);
        Assert.Equal(2, keywordType.TypeArguments.Count);

        // First type arg is int (Int32).
        Assert.Equal(keywordType.TypeArguments[0].DisplayName, namedType.TypeArguments[0].DisplayName);
        // Second type arg is string (String).
        Assert.Equal(keywordType.TypeArguments[1].DisplayName, namedType.TypeArguments[1].DisplayName);
    }

    [Fact]
    public void FuncType_SameArityKeywordForms_ShareGenericDefinition()
    {
        // Two different `func` fields with the same arity but different type args
        // should share the same generic definition.
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public func<int, string> field1;
                public func<bool, int> field2;
            }
            """;

        var (model, _) = BuildModel(source);

        var field1 = FindField(model, "field1");
        var field2 = FindField(model, "field2");

        Assert.NotNull(field1?.DeclaredType);
        Assert.NotNull(field2?.DeclaredType);

        var type1 = Assert.IsType<ConstructedType>(field1!.DeclaredType);
        var type2 = Assert.IsType<ConstructedType>(field2!.DeclaredType);

        // Same arity → same generic definition.
        Assert.Same(type1.GenericDefinition, type2.GenericDefinition);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Func invocation: a variable of type func<int, string> invoked
    //    with f(42) resolves to type string
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FuncType_Invocation_ResolvesToReturnType()
    {
        // When a Func-typed variable is invoked, the expression type should
        // be the return type (last type argument).
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public func<int, string> converter;
                public void TestMethod()
                {
                    string result = converter(42);
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        // No fatal semantic errors.
        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var result = FindLocal(model, "result");
        Assert.NotNull(result);
        Assert.NotNull(result!.DeclaredType);

        // The declared type of `result` should be string.
        Assert.True(
            result.DeclaredType!.DisplayName == "string" ||
            result.DeclaredType!.DisplayName == "String",
            $"Expected result type to be string/String, got '{result.DeclaredType.DisplayName}'");
    }

    [Fact]
    public void FuncType_InvocationWithNamedForm_ResolvesToReturnType()
    {
        // Same test but using the `Func<int, string>` named form.
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public Func<int, string> converter;
                public void TestMethod()
                {
                    string result = converter(42);
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var result = FindLocal(model, "result");
        Assert.NotNull(result);
        Assert.NotNull(result!.DeclaredType);

        Assert.True(
            result.DeclaredType!.DisplayName == "string" ||
            result.DeclaredType!.DisplayName == "String",
            $"Expected result type to be string/String, got '{result.DeclaredType.DisplayName}'");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Method group: an identifier referencing a method used as a value
    //    resolves to the equivalent func type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FuncType_MethodGroup_ResolvesToEquivalentFuncType()
    {
        // When a method identifier is used as a value (not invoked), its type
        // should be the equivalent Func type constructed from the method's
        // parameter types + return type.
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public string Foo(int x)
                {
                    return "hello";
                }
                public void TestMethod()
                {
                    func<int, string> f = Foo;
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        // No fatal semantic errors.
        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var localF = FindLocal(model, "f");
        Assert.NotNull(localF);
        Assert.NotNull(localF!.DeclaredType);

        // The declared type of `f` should be a Func constructed type.
        var funcType = Assert.IsType<ConstructedType>(localF.DeclaredType);
        var funcDef = Assert.IsType<ClassType>(funcType.GenericDefinition);
        Assert.Equal("Func", funcDef.DisplayName);
        Assert.Equal("Standard", funcDef.ContainingNamespace);

        // Type arguments: [int, string] (param type, return type).
        Assert.Equal(2, funcType.TypeArguments.Count);
    }

    [Fact]
    public void FuncType_MethodGroupConversion_MatchesExplicitFuncType()
    {
        // The method group conversion should produce a type that matches
        // the explicitly declared func type.
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public int Double(int x)
                {
                    return x * 2;
                }
                public func<int, int> explicitField;
                public void TestMethod()
                {
                    func<int, int> f = Double;
                }
            }
            """;

        var (model, _) = BuildModel(source);

        var localF = FindLocal(model, "f");
        var explicitField = FindField(model, "explicitField");

        Assert.NotNull(localF?.DeclaredType);
        Assert.NotNull(explicitField?.DeclaredType);

        var localType = Assert.IsType<ConstructedType>(localF!.DeclaredType);
        var fieldType = Assert.IsType<ConstructedType>(explicitField!.DeclaredType);

        // Both should share the same generic definition (same arity Func).
        Assert.Same(localType.GenericDefinition, fieldType.GenericDefinition);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Arity variants: func<bool> (no params, returns bool),
    //    func<int, int, int> (two params, returns int)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FuncType_ArityOne_NoParamsReturnsBool()
    {
        // func<bool> = zero parameters, returns bool.
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public func<bool> predicate;
            }
            """;

        var (model, _) = BuildModel(source);

        var field = FindField(model, "predicate");
        Assert.NotNull(field?.DeclaredType);

        var funcType = Assert.IsType<ConstructedType>(field!.DeclaredType);
        var funcDef = Assert.IsType<ClassType>(funcType.GenericDefinition);
        Assert.Equal("Func", funcDef.DisplayName);
        Assert.Equal("Standard", funcDef.ContainingNamespace);

        // Single type argument: the return type (bool).
        var singleArg = Assert.Single(funcType.TypeArguments);
        Assert.True(
            singleArg.DisplayName == "bool" ||
            singleArg.DisplayName == "Boolean",
            $"Expected return type bool/Boolean, got '{singleArg.DisplayName}'");
    }

    [Fact]
    public void FuncType_ArityThree_TwoParamsReturnsInt()
    {
        // func<int, int, int> = two int parameters, returns int.
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public func<int, int, int> adder;
            }
            """;

        var (model, _) = BuildModel(source);

        var field = FindField(model, "adder");
        Assert.NotNull(field?.DeclaredType);

        var funcType = Assert.IsType<ConstructedType>(field!.DeclaredType);
        var funcDef = Assert.IsType<ClassType>(funcType.GenericDefinition);
        Assert.Equal("Func", funcDef.DisplayName);
        Assert.Equal("Standard", funcDef.ContainingNamespace);

        // Three type arguments: [int, int, int].
        Assert.Equal(3, funcType.TypeArguments.Count);
        foreach(var typeArg in funcType.TypeArguments)
        {
            Assert.True(
                typeArg.DisplayName == "int" || typeArg.DisplayName == "Int32",
                $"Expected int/Int32, got '{typeArg.DisplayName}'");
        }
    }

    [Fact]
    public void FuncType_DifferentArities_HaveDifferentGenericDefinitions()
    {
        // func<bool> (arity 1) and func<int, string> (arity 2) should have
        // different generic definitions (different Func class declarations).
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public func<bool> arity1;
                public func<int, string> arity2;
            }
            """;

        var (model, _) = BuildModel(source);

        var arity1Field = FindField(model, "arity1");
        var arity2Field = FindField(model, "arity2");

        Assert.NotNull(arity1Field?.DeclaredType);
        Assert.NotNull(arity2Field?.DeclaredType);

        var type1 = Assert.IsType<ConstructedType>(arity1Field!.DeclaredType);
        var type2 = Assert.IsType<ConstructedType>(arity2Field!.DeclaredType);

        // Different arities → different generic definitions.
        Assert.NotSame(type1.GenericDefinition, type2.GenericDefinition);
    }

    [Fact]
    public void FuncType_ArityOne_InvocationReturnsCorrectType()
    {
        // Invoking a func<bool> with no arguments should resolve to bool.
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public func<bool> predicate;
                public void TestMethod()
                {
                    bool result = predicate();
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var result = FindLocal(model, "result");
        Assert.NotNull(result);
        Assert.NotNull(result!.DeclaredType);

        Assert.True(
            result.DeclaredType!.DisplayName == "bool" ||
            result.DeclaredType!.DisplayName == "Boolean",
            $"Expected result type bool/Boolean, got '{result.DeclaredType.DisplayName}'");
    }

    [Fact]
    public void FuncType_ArityThree_InvocationReturnsLastTypeArg()
    {
        // Invoking a func<int, int, int> with two args should resolve to int (last type arg).
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public func<int, int, int> adder;
                public void TestMethod()
                {
                    int result = adder(1, 2);
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_SEMANTIC_ERROR");

        var result = FindLocal(model, "result");
        Assert.NotNull(result);
        Assert.NotNull(result!.DeclaredType);

        Assert.True(
            result.DeclaredType!.DisplayName == "int" ||
            result.DeclaredType!.DisplayName == "Int32",
            $"Expected result type int/Int32, got '{result.DeclaredType.DisplayName}'");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. IsFuncType helper validation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsFuncType_ReturnsTrueForFuncConstructedType()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public func<int, string> myField;
            }
            """;

        var (model, _) = BuildModel(source);

        var myField = FindField(model, "myField");
        Assert.NotNull(myField?.DeclaredType);

        Assert.True(SemanticModelBuilder.IsFuncType(myField!.DeclaredType!));
    }

    [Fact]
    public void IsFuncType_ReturnsFalseForNonFuncType()
    {
        const string source = """
            namespace TestProject;
            public class TestClass
            {
                public int myField;
            }
            """;

        var (model, _) = BuildModel(source);

        var myField = FindField(model, "myField");
        Assert.NotNull(myField?.DeclaredType);

        Assert.False(SemanticModelBuilder.IsFuncType(myField!.DeclaredType!));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. GetFuncReturnType helper validation
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("func<string>", "string", "String")]
    [InlineData("func<int, string>", "string", "String")]
    [InlineData("func<int, int, bool>", "bool", "Boolean")]
    public void GetFuncReturnType_ExtractsLastTypeArgument(
        string funcTypeSource, string expectedLower, string expectedUpper)
    {
        var source = $$"""
            namespace TestProject;
            public class TestClass
            {
                public {{funcTypeSource}} myField;
            }
            """;

        var (model, _) = BuildModel(source);

        var myField = FindField(model, "myField");
        Assert.NotNull(myField?.DeclaredType);

        var funcType = Assert.IsType<ConstructedType>(myField!.DeclaredType);
        var returnType = SemanticModelBuilder.GetFuncReturnType(funcType);

        Assert.True(
            returnType.DisplayName == expectedLower || returnType.DisplayName == expectedUpper,
            $"Expected return type {expectedLower}/{expectedUpper}, got '{returnType.DisplayName}'");
    }
}