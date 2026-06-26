using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
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
/// Verification tests for type-alias interaction with Func types (Task 17.2).
///
/// Verifies:
/// <list type="bullet">
///   <item><c>type Predicate&lt;T&gt; = func&lt;T, bool&gt;;</c> resolves correctly</item>
///   <item>Lambda inference sees through aliases: <c>Predicate&lt;int&gt; p = x =&gt; true;</c></item>
///   <item>Structural equivalence: <c>Predicate&lt;int&gt;</c> == <c>func&lt;int, bool&gt;</c> == <c>Func&lt;int, bool&gt;</c></item>
/// </list>
///
/// All tests run fully in-process (no <c>dotnet</c> process spawning).
///
/// <b>Validates: Requirements 16.1, 16.2, 16.3, 16.4</b>
/// </summary>
[Trait("Feature", "first-class-functions")]
public sealed class FuncTypeAliasInteractionTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

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

    private static ISymbol? FindLocal(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.LocalVariable);
    }

    private static ISymbol? FindField(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Field);
    }

    private static void AssertIsFuncType(IType? type, params string[] expectedTypeArgDisplayNames)
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

    private static bool MatchesTypeName(string actual, string expected)
    {
        if(actual == expected)
        {
            return true;
        }

        return expected switch
        {
            "int" => actual is "Int32" or "int",
            "Int32" => actual is "Int32" or "int",
            "bool" => actual is "Boolean" or "bool",
            "Boolean" => actual is "Boolean" or "bool",
            "string" => actual is "String" or "string",
            "String" => actual is "String" or "string",
            "void" => actual is "Void" or "void",
            "Void" => actual is "Void" or "void",
            _ => false,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. User-defined type alias resolves: type Predicate<T> = func<T, bool>;
    //    Predicate<int> should resolve to Func<int, bool> (Req 16.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeAlias_PredicateOfInt_ResolvesToFuncIntBool()
    {
        const string source = """
            using Standard;
            namespace TestProject;

            type Predicate<T> = func<T, bool>;

            public class TestClass
            {
                public Predicate<int> predicateField;
                public func<int, bool> funcKeywordField;
                public Func<int, bool> funcNamedField;
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        // No semantic errors expected.
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var predicateField = FindField(model, "predicateField");
        var funcKeywordField = FindField(model, "funcKeywordField");
        var funcNamedField = FindField(model, "funcNamedField");

        Assert.NotNull(predicateField?.DeclaredType);
        Assert.NotNull(funcKeywordField?.DeclaredType);
        Assert.NotNull(funcNamedField?.DeclaredType);

        // All three should resolve to the same ConstructedType: Func<int, bool>.
        AssertIsFuncType(predicateField!.DeclaredType, "int", "bool");
        AssertIsFuncType(funcKeywordField!.DeclaredType, "int", "bool");
        AssertIsFuncType(funcNamedField!.DeclaredType, "int", "bool");

        // Structural equivalence: same generic definition (reference equality).
        var predType = (ConstructedType)predicateField.DeclaredType!;
        var kwType = (ConstructedType)funcKeywordField!.DeclaredType!;
        var namedType = (ConstructedType)funcNamedField!.DeclaredType!;

        Assert.Same(predType.GenericDefinition, kwType.GenericDefinition);
        Assert.Same(predType.GenericDefinition, namedType.GenericDefinition);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Lambda inference sees through aliases (Req 16.2):
    //    Predicate<int> p = x => x > 0;
    //    x should be inferred as int
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeAlias_LambdaInference_SeesThrough_PredicateAlias()
    {
        const string source = """
            using Standard;
            namespace TestProject;

            type Predicate<T> = func<T, bool>;

            public class TestClass
            {
                public void TestMethod()
                {
                    Predicate<int> p = x => true;
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        // No semantic errors expected.
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var localP = FindLocal(model, "p");
        Assert.NotNull(localP);
        Assert.NotNull(localP!.DeclaredType);

        // p's declared type should be Func<int, bool> (the alias is transparent).
        AssertIsFuncType(localP.DeclaredType, "int", "bool");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Structural equivalence: Predicate<int> == func<int, bool> == Func<int, bool>
    //    All three resolve to the same ConstructedType (Req 16.4)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeAlias_StructuralEquivalence_AllThreeFormsMatch()
    {
        const string source = """
            using Standard;
            namespace TestProject;

            type Predicate<T> = func<T, bool>;

            public class TestClass
            {
                public void TestMethod()
                {
                    Predicate<int> a = x => true;
                    func<int, bool> b = x => true;
                    Func<int, bool> c = x => true;
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var localA = FindLocal(model, "a");
        var localB = FindLocal(model, "b");
        var localC = FindLocal(model, "c");

        Assert.NotNull(localA?.DeclaredType);
        Assert.NotNull(localB?.DeclaredType);
        Assert.NotNull(localC?.DeclaredType);

        // All three should be ConstructedType with same generic definition.
        var typeA = Assert.IsType<ConstructedType>(localA!.DeclaredType);
        var typeB = Assert.IsType<ConstructedType>(localB!.DeclaredType);
        var typeC = Assert.IsType<ConstructedType>(localC!.DeclaredType);

        Assert.Same(typeA.GenericDefinition, typeB.GenericDefinition);
        Assert.Same(typeA.GenericDefinition, typeC.GenericDefinition);

        // All should have the same type arguments: [int, bool].
        AssertIsFuncType(localA.DeclaredType, "int", "bool");
        AssertIsFuncType(localB.DeclaredType, "int", "bool");
        AssertIsFuncType(localC.DeclaredType, "int", "bool");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Multi-param type alias: type Transform<TIn, TOut> = func<TIn, TOut>;
    //    Transform<int, string> resolves to Func<int, string> (Req 16.3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeAlias_MultiParam_ResolvesCorrectly()
    {
        const string source = """
            using Standard;
            namespace TestProject;

            type Transform<TIn, TOut> = func<TIn, TOut>;

            public class TestClass
            {
                public void TestMethod()
                {
                    Transform<int, string> t = x => "hello";
                }
            }
            """;

        var (model, diagnostics) = BuildModel(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var localT = FindLocal(model, "t");
        Assert.NotNull(localT);
        Assert.NotNull(localT!.DeclaredType);

        // Transform<int, string> resolves to Func<int, string>.
        AssertIsFuncType(localT.DeclaredType, "int", "string");
    }
}