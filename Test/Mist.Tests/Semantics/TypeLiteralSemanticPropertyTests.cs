// Feature: type-literals, Property 4: All type literal expressions have TypeType as their expression type
// Feature: type-literals, Property 5: Variable names shadow type names in expression position
// Feature: type-literals, Property 6: Unresolved type literals produce diagnostics

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for type literal semantic analysis.
///
/// Property 4: All type literal expressions have TypeType as their expression type.
/// For any valid TypeLiteralExpression, the semantic model SHALL record TypeType as
/// the expression type; var declarations initialized with a type literal SHALL infer TypeType.
///
/// Property 5: Variable names shadow type names in expression position.
/// When an identifier is both a user-defined type name and a local variable name,
/// expression position SHALL resolve to the variable (with the variable's declared type),
/// not as a type literal.
///
/// Property 6: Unresolved type literals produce diagnostics.
/// A type name in TypeLiteralExpression that cannot be resolved SHALL emit MIST_UNDEFINED_TYPE.
///
/// **Validates: Requirements 2.6, 3.2, 3.3, 4.1**
/// </summary>
public class TypeLiteralSemanticPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private readonly SemanticModelBuilder builder = new();

    // ─── Primitive type keyword set ──────────────────────────────────────

    private static readonly string[] PrimitiveTypeKeywords =
    [
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char", "string"
    ];

    // ─── Test input model ────────────────────────────────────────────────

    /// <summary>
    /// Represents a test case: a primitive type keyword used as a type literal
    /// in a var initializer position within a complete Mist program.
    /// </summary>
#pragma warning disable CA1034
    public record TypeLiteralSemanticTestCase(string Keyword, string Source)
    {
        public override string ToString() => $"var x = {Keyword};";
    }
#pragma warning restore CA1034

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    public static Arbitrary<TypeLiteralSemanticTestCase> ArbitraryTypeLiteralSemanticTestCase()
    {
        var gen =
            from keyword in Gen.Elements(PrimitiveTypeKeywords)
            let source = $"class Program {{ public static void Main() {{ var x = {keyword}; }} }}"
            select new TypeLiteralSemanticTestCase(keyword, source);

        return gen.ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);
        return result.Root as CompilationUnitNode;
    }

    private ISemanticModel BuildSemanticModel(Storm.Runtime.SyntaxNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    /// <summary>
    /// Recursively finds all TypeLiteralExpressionPrimaryNode instances in the AST.
    /// </summary>
    private static List<TypeLiteralExpressionPrimaryNode> FindTypeLiterals(Storm.Runtime.SyntaxNode node)
    {
        var results = new List<TypeLiteralExpressionPrimaryNode>();
        CollectTypeLiterals(node, results);
        return results;
    }

    private static void CollectTypeLiterals(Storm.Runtime.SyntaxNode? node, List<TypeLiteralExpressionPrimaryNode> results)
    {
        if(node is null)
        {
            return;
        }

        if(node is TypeLiteralExpressionPrimaryNode typeLiteral)
        {
            results.Add(typeLiteral);
        }

        try
        {
            foreach(var child in node.Children())
            {
                CollectTypeLiterals(child, results);
            }
        }
        catch(NullReferenceException)
        {
            // Some AST nodes may have null children that Children() doesn't guard against.
        }
    }

    /// <summary>
    /// Recursively finds all InferredVariableDeclarationVariableDeclarationStatementNode instances.
    /// </summary>
    private static List<InferredVariableDeclarationVariableDeclarationStatementNode> FindVarDeclarations(
        Storm.Runtime.SyntaxNode node)
    {
        var results = new List<InferredVariableDeclarationVariableDeclarationStatementNode>();
        CollectVarDeclarations(node, results);
        return results;
    }

    private static void CollectVarDeclarations(
        Storm.Runtime.SyntaxNode? node,
        List<InferredVariableDeclarationVariableDeclarationStatementNode> results)
    {
        if(node is null)
        {
            return;
        }

        if(node is InferredVariableDeclarationVariableDeclarationStatementNode varDecl)
        {
            results.Add(varDecl);
        }

        try
        {
            foreach(var child in node.Children())
            {
                CollectVarDeclarations(child, results);
            }
        }
        catch(NullReferenceException)
        {
            // Guard against null children.
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 4: All type literal expressions have TypeType as their
    //             expression type
    //
    // **Validates: Requirements 3.2, 3.3**
    //
    // For any valid TypeLiteralExpression, the semantic model SHALL record
    // TypeType as the expression type. var declarations initialized with
    // a type literal SHALL infer TypeType.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralSemanticPropertyTests) })]
    public bool TypeLiteralExpression_HasTypeType_AsExpressionType(
        TypeLiteralSemanticTestCase testCase)
    {
        var root = ParseSource(testCase.Source);
        if(root is null)
        {
            return false;
        }

        var model = BuildSemanticModel(root, testCase.Source);

        // Find all TypeLiteralExpression nodes in the AST
        var typeLiterals = FindTypeLiterals(root);
        if(typeLiterals.Count == 0)
        {
            return false;
        }

        // Every TypeLiteralExpression must have TypeType as its expression type
        foreach(var typeLiteral in typeLiterals)
        {
            var exprType = model.GetTypeOf(typeLiteral);
            if(exprType is null || !exprType.IsTypeType())
            {
                return false;
            }
        }

        return true;
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralSemanticPropertyTests) })]
    public bool VarDeclaration_WithTypeLiteralInitializer_InfersTypeType(
        TypeLiteralSemanticTestCase testCase)
    {
        var root = ParseSource(testCase.Source);
        if(root is null)
        {
            return false;
        }

        var model = BuildSemanticModel(root, testCase.Source);

        // Find var declarations — the variable should have TypeType as its declared type
        var varDecls = FindVarDeclarations(root);
        if(varDecls.Count == 0)
        {
            return false;
        }

        foreach(var varDecl in varDecls)
        {
            // The symbol for this var declaration should have TypeType as its declared type
            var symbol = model.GetDeclaredSymbol(varDecl);
            if(symbol is null)
            {
                return false;
            }

            if(symbol.DeclaredType is null || !symbol.DeclaredType.IsTypeType())
            {
                return false;
            }
        }

        return true;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 5: Variable names shadow type names in expression position
    //
    // **Validates: Requirements 2.6**
    //
    // For any identifier name that is both a user-defined type name and a
    // local variable name in the current scope, when that name appears in
    // expression position, the semantic model SHALL resolve it as the local
    // variable reference (with the variable's declared type), not as a
    // type literal (TypeType).
    // ═════════════════════════════════════════════════════════════════════

    // ─── Test input model for Property 5 ─────────────────────────────────

    /// <summary>
    /// Represents a test case where a class defines a user-defined type and a
    /// local variable with the same name exists in scope. The variable's type
    /// is a primitive type keyword.
    /// </summary>
#pragma warning disable CA1034
    public record VariableShadowsTypeTestCase(
        string TypeName,
        string VariableType,
        string Source)
    {
        public override string ToString() =>
            $"class {TypeName} {{}} ... {VariableType} {TypeName} = ...; var x = {TypeName};";
    }
#pragma warning restore CA1034

    // ─── Valid identifier names for user-defined types ────────────────────

    private static readonly string[] UserDefinedTypeNames =
    [
        "Foo", "Bar", "Baz", "MyType", "Widget", "Item",
        "Node", "Entry", "Record", "Value", "Data", "Info"
    ];

    /// <summary>
    /// Primitive types paired with a default literal value for typed variable declarations.
    /// </summary>
    private static readonly (string Keyword, string Literal)[] PrimitiveTypesWithLiterals =
    [
        ("int", "42"),
        ("uint", "42"),
        ("long", "42"),
        ("ulong", "42"),
        ("short", "42"),
        ("ushort", "42"),
        ("byte", "42"),
        ("sbyte", "42"),
        ("float", "3.14"),
        ("double", "3.14"),
        ("decimal", "3.14"),
        ("bool", "true"),
        ("char", "'a'"),
    ];

    // ─── FsCheck Arbitrary for Property 5 ────────────────────────────────

    public static Arbitrary<VariableShadowsTypeTestCase> ArbitraryVariableShadowsTypeTestCase()
    {
        var gen =
            from typeName in Gen.Elements(UserDefinedTypeNames)
            from primIdx in Gen.Choose(0, PrimitiveTypesWithLiterals.Length - 1)
            let prim = PrimitiveTypesWithLiterals[primIdx]
            let source = $"class {typeName} {{ }} class Program {{ public static void Main() {{ {prim.Keyword} {typeName} = {prim.Literal}; var x = {typeName}; }} }}"
            select new VariableShadowsTypeTestCase(typeName, prim.Keyword, source);

        return gen.ToArbitrary();
    }

    // ─── Helpers for Property 5 ──────────────────────────────────────────

    /// <summary>
    /// Finds the second var declaration in the AST (the one that reads the shadowed name).
    /// In our generated programs, the first var decl is "var x = TypeName;" which is the
    /// one we want to inspect.
    /// </summary>
    private static InferredVariableDeclarationVariableDeclarationStatementNode? FindInferredVarDeclByName(
        Storm.Runtime.SyntaxNode node, string varName)
    {
        var allVarDecls = FindVarDeclarations(node);
        return allVarDecls.FirstOrDefault(v => v.Name.Lexeme == varName);
    }

    /// <summary>
    /// Maps a primitive type keyword to its expected PrimitiveType instance.
    /// Uses the Legacy* singletons directly to guarantee stable DisplayName
    /// values regardless of whether PrimitiveTypeRegistry has been populated
    /// by a parallel test that loaded stdlib. The shim accessors
    /// (LegacyPrimitiveTypes.Int, etc.) may return stdlib StructType instances
    /// with different DisplayName values (e.g., "Int32" instead of "int").
    /// </summary>
    private static IType? ExpectedPrimitiveType(string keyword) => keyword switch
    {
        "int" => LegacyPrimitiveTypes.Int,
        "uint" => LegacyPrimitiveTypes.Uint,
        "long" => LegacyPrimitiveTypes.Long,
        "ulong" => LegacyPrimitiveTypes.Ulong,
        "short" => LegacyPrimitiveTypes.Short,
        "ushort" => LegacyPrimitiveTypes.Ushort,
        "byte" => LegacyPrimitiveTypes.Byte,
        "sbyte" => LegacyPrimitiveTypes.Sbyte,
        "float" => LegacyPrimitiveTypes.Float,
        "double" => LegacyPrimitiveTypes.Double,
        "decimal" => LegacyPrimitiveTypes.Decimal,
        "bool" => LegacyPrimitiveTypes.Bool,
        "char" => LegacyPrimitiveTypes.Char,
        _ => null
    };

    // ─── Property 5 tests ────────────────────────────────────────────────

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralSemanticPropertyTests) })]
    public bool VariableName_ShadowsTypeName_InExpressionPosition(
        VariableShadowsTypeTestCase testCase)
    {
        // Parse: class TypeName {} class Program { static void Main() { int TypeName = 42; var x = TypeName; } }
        var root = ParseSource(testCase.Source);
        if(root is null)
        {
            return false;
        }

        var model = BuildSemanticModel(root, testCase.Source);

        // Find the "var x = TypeName;" declaration — x should have the variable's
        // primitive type, NOT TypeType, because the local variable shadows the class name.
        var xDecl = FindInferredVarDeclByName(root, "x");
        if(xDecl is null)
        {
            return false;
        }

        var xSymbol = model.GetDeclaredSymbol(xDecl);
        if(xSymbol is null)
        {
            return false;
        }

        // The inferred type of x must be the primitive type of the shadowing variable,
        // not TypeType (which would mean it resolved as a type literal).
        // After Task 32's compat shim, PrimitiveType.* accessors may return
        // stdlib StructType instances whose DisplayName differs from the
        // keyword form. Compare by DisplayName to handle both cases.
        var expectedType = ExpectedPrimitiveType(testCase.VariableType);
        if(expectedType is null || xSymbol.DeclaredType is null)
        {
            return false;
        }

        return xSymbol.DeclaredType.DisplayName == expectedType.DisplayName;
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralSemanticPropertyTests) })]
    public bool VariableName_ShadowsTypeName_ExpressionTypeIsNotTypeType(
        VariableShadowsTypeTestCase testCase)
    {
        // Parse the program where a local variable shadows a user-defined type name.
        var root = ParseSource(testCase.Source);
        if(root is null)
        {
            return false;
        }

        var model = BuildSemanticModel(root, testCase.Source);

        // Find the "var x = TypeName;" declaration and check the initializer expression type.
        var xDecl = FindInferredVarDeclByName(root, "x");
        if(xDecl is null)
        {
            return false;
        }

        // The initializer expression (the IdentifierExpression for "TypeName") should
        // resolve to the variable's type, not TypeType.
        var initExprType = model.GetTypeOf(xDecl.Initializer);
        if(initExprType is not null && initExprType.IsTypeType())
        {
            return false;
        }

        // It should be the primitive type of the shadowing variable.
        // After Task 32's compat shim, PrimitiveType.* accessors may return
        // the stdlib StructType when the PrimitiveTypeRegistry is populated
        // by a parallel test that loaded stdlib. The test's own model (built
        // without stdlib) still uses legacy PrimitiveType singletons. Compare
        // by DisplayName to handle both cases.
        var expectedType = ExpectedPrimitiveType(testCase.VariableType);
        if(expectedType is null || initExprType is null)
        {
            return false;
        }

        return initExprType.DisplayName == expectedType.DisplayName;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 6: Unresolved type literals produce diagnostics
    //
    // **Validates: Requirements 4.1**
    //
    // A type name in TypeLiteralExpression that cannot be resolved SHALL
    // emit MIST_UNDEFINED_TYPE. Since primitive keywords always resolve
    // and user-defined names parse as IdentifierExpression (not
    // TypeLiteralExpression), this property is tested by constructing
    // AST nodes directly with a NamedTypeRef inside a
    // TypeLiteralExpressionPrimaryNode containing an unresolvable name.
    // ═════════════════════════════════════════════════════════════════════

    // ─── Test input model for Property 6 ─────────────────────────────────

    /// <summary>
    /// Represents a test case with a random non-existent type name placed
    /// inside a TypeLiteralExpression via direct AST construction.
    /// </summary>
#pragma warning disable CA1034
    public record UnresolvedTypeLiteralTestCase(string TypeName)
    {
        public override string ToString() => $"TypeLiteralExpression({TypeName})";
    }
#pragma warning restore CA1034

    // ─── Mist keywords and well-known types (excluded from random names) ─

    private static readonly HashSet<string> MistReservedNames = new(StringComparer.Ordinal)
    {
        "namespace", "using", "class", "struct", "public", "private", "static",
        "void", "new", "return", "if", "else", "while", "for", "foreach",
        "in", "break", "var", "true", "false", "this",
        "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte",
        "float", "double", "decimal", "bool", "char", "string",
        "Console", "String", "Int32", "Boolean", "Object", "Type",
    };

    // ─── FsCheck Arbitrary for Property 6 ────────────────────────────────

    public static Arbitrary<UnresolvedTypeLiteralTestCase> ArbitraryUnresolvedTypeLiteralTestCase()
    {
        var gen =
            from prefix in Gen.Elements("Xyz", "Qux", "Waldo", "Bogus", "Phantom", "Ghost")
            from suffix in Gen.Choose(1, 9999)
            let name = $"{prefix}{suffix}"
            where !MistReservedNames.Contains(name)
            select new UnresolvedTypeLiteralTestCase(name);

        return gen.ToArbitrary();
    }

    // ─── AST construction helpers for Property 6 ─────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Creates a TypeLiteralExpression wrapping a NamedTypeRef with the given type name.
    /// This directly constructs the AST node that would exist if the parser matched
    /// a named type in TypeLiteralExpression position.
    /// </summary>
    private static ExpressionNode MakeTypeLiteralExpression(string typeName)
    {
        var primary = new TypeLiteralExpressionPrimaryNode
        {
            TypeRef = new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken(typeName),
                        Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
                    },
                }
            },
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Wraps an expression in an expression statement inside a class method.
    /// </summary>
    private static CompilationUnitNode WrapInClassMethod(ImmutableArray<StatementNode> statements)
    {
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = statements } },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Property 6 tests ────────────────────────────────────────────────

    /// <summary>
    /// Property 6: For any type name in a TypeLiteralExpression that cannot be resolved,
    /// the semantic model builder SHALL emit a MIST_UNDEFINED_TYPE diagnostic.
    ///
    /// This test constructs AST nodes directly with a NamedTypeRef inside a
    /// TypeLiteralExpressionPrimaryNode, using a randomly generated non-existent
    /// type name. Since the name doesn't correspond to any declared type, the
    /// ResolveTypeReference → ResolveNamedType path emits MIST_UNDEFINED_TYPE.
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralSemanticPropertyTests) })]
    public bool UnresolvedTypeLiteral_EmitsMistUndefinedTypeDiagnostic(
        UnresolvedTypeLiteralTestCase testCase)
    {
        // Build AST: class Program { void Main() { <TypeLiteralExpr(testCase.TypeName)>; } }
        var typeLiteralStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = MakeTypeLiteralExpression(testCase.TypeName),
            },
        };

        var ast = WrapInClassMethod(ImmutableArray.Create<StatementNode>(typeLiteralStmt));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // The semantic model must emit MIST_UNDEFINED_TYPE for the unresolvable type name.
        return diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" &&
            d.Message.Contains(testCase.TypeName));
    }

    /// <summary>
    /// Targeted unit test: verifies that a specific unresolvable type name in a
    /// TypeLiteralExpression produces exactly one MIST_UNDEFINED_TYPE diagnostic.
    /// </summary>
    [Fact]
    public void UnresolvedTypeLiteral_ProducesExactDiagnostic()
    {
        // Build AST: class Program { void Main() { <TypeLiteralExpr("NonExistentType")>; } }
        var typeLiteralStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = MakeTypeLiteralExpression("NonExistentType"),
            },
        };

        var ast = WrapInClassMethod(ImmutableArray.Create<StatementNode>(typeLiteralStmt));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var undefinedTypeDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains("NonExistentType"))
            .ToList();

        Assert.Single(undefinedTypeDiags);
    }

    /// <summary>
    /// Negative test: a TypeLiteralExpression with a primitive type (which always resolves)
    /// should NOT produce a MIST_UNDEFINED_TYPE diagnostic.
    /// </summary>
    [Fact]
    public void ResolvedPrimitiveTypeLiteral_DoesNotProduceUndefinedTypeDiagnostic()
    {
        // Parse a real program: class Program { void Main() { var x = int; } }
        // Primitive type keywords always resolve, so no MIST_UNDEFINED_TYPE should appear.
        var source = "class Program { public static void Main() { var x = int; } }";
        var root = ParseSource(source);
        Assert.NotNull(root);

        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(root), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);

        Assert.DoesNotContain(diagnostics.ToImmutableArray(),
            d => d.Id.Value == "MIST_UNDEFINED_TYPE");
    }
}