// Feature: mist-language-v1, Property 8: Undefined identifiers produce diagnostics
// **Validates: Requirements 6.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that undefined identifiers produce diagnostics.
///
/// Property 8: For any Mist program containing a reference to an identifier that
/// is not declared in any enclosing scope or imported namespace, the semantic model
/// builder SHALL emit a Diagnostic identifying the undefined name.
/// </summary>
public class UndefinedIdentifierPropertyTests
{
    private readonly SemanticModelBuilder builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

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
        string source = "",
        IAmbientTypeResolver? ambientTypeResolver = null)
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Creates a minimal expression AST node (integer literal) suitable for
    /// use as a variable initializer.
    /// </summary>
    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
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
    /// Creates an identifier expression AST node for referencing a variable by name.
    /// </summary>
    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
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
    /// Creates an expression statement that references an identifier: <c>name;</c>
    /// </summary>
    private static StatementNode MakeIdentifierStatement(string name)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = MakeIdentifierExpression(name),
            },
        };
    }

    /// <summary>
    /// Creates a typed variable declaration statement: <c>int name = 0;</c>
    /// </summary>
    private static StatementNode MakeIntVarDecl(string name, string value = "0")
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
                Name = MakeToken(name),
                Initializer = MakeIntLiteralExpression(value),
            },
        };
    }

    /// <summary>
    /// Creates a variable declaration with a named type reference: <c>TypeName name = 0;</c>
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(string typeName, string varName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
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
                Name = MakeToken(varName),
                Initializer = MakeIntLiteralExpression("0"),
            },
        };
    }

    /// <summary>
    /// Wraps statements in a class with a void method.
    /// </summary>
    private static CompilationUnitNode WrapInClassMethod(
        string className,
        string methodName,
        ImmutableArray<StatementNode> statements)
    {
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken(methodName),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = statements,
                }
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Mist keywords and well-known types (excluded from random identifiers) ──

    private static readonly HashSet<string> MistKeywordsAndWellKnownTypes = new(StringComparer.Ordinal)
    {
        // Language keywords
        "namespace", "using", "class", "struct", "public", "private", "static",
        "void", "new", "return", "if", "else", "while", "for", "foreach",
        "in", "break", "var", "true", "false", "this",
        // Primitive type keywords
        "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte",
        "float", "double", "decimal", "bool", "char", "string",
    };

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates valid C# identifiers that are NOT Mist keywords or well-known types.
    /// These identifiers should always be undefined when referenced without declaration.
    /// </summary>
    public static Arbitrary<string> ArbitraryUndefinedIdentifier()
    {
        var gen = from prefix in Gen.Elements("x", "foo", "bar", "myVar", "temp", "val", "item", "data")
                  from suffix in Gen.Choose(1, 9999)
                  let name = $"{prefix}{suffix}"
                  where !MistKeywordsAndWellKnownTypes.Contains(name)
                  select name;
        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 8a: Referencing an undeclared variable in a method body produces
    /// a MIST_UNDEFINED_IDENTIFIER diagnostic.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UndefinedIdentifierPropertyTests) }, MaxTest = 100)]
    public bool UndeclaredVariable_InMethodBody_ProducesDiagnostic(string undefinedName)
    {
        // Build: class C { void M() { undefinedName; } }
        var statements = ImmutableArray.Create(
            MakeIdentifierStatement(undefinedName));

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Should produce a MIST_UNDEFINED_IDENTIFIER diagnostic mentioning the name.
        return diagnostics.Any(d =>
            d.Id.Value.Contains("MIST_UNDEFINED_IDENTIFIER") &&
            d.Message.Contains(undefinedName));
    }

    /// <summary>
    /// Property 8b: Referencing an undeclared type in a type reference produces
    /// a MIST_UNDEFINED_TYPE diagnostic.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UndefinedIdentifierPropertyTests) }, MaxTest = 100)]
    public bool UndeclaredType_InTypeReference_ProducesDiagnostic(string undefinedName)
    {
        // Build: class C { void M() { UndefinedType x = 0; } }
        var statements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(undefinedName, "x"));

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Should produce a MIST_UNDEFINED_TYPE diagnostic mentioning the type name.
        return diagnostics.Any(d =>
            d.Id.Value.Contains("MIST_UNDEFINED_TYPE") &&
            d.Message.Contains(undefinedName));
    }

    /// <summary>
    /// Property 8c: Using an undeclared namespace in a using declaration produces
    /// a MIST_UNDEFINED_NAMESPACE diagnostic.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UndefinedIdentifierPropertyTests) }, MaxTest = 100)]
    public bool UndeclaredNamespace_InUsingDeclaration_ProducesDiagnostic(string undefinedName)
    {
        // Build: using UndefinedNs; class C { void M() { } }
        var usingDecl = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(undefinedName),
                Span = new Storm.Runtime.SourceSpan(0, undefinedName.Length),
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode
                {
                    Method = new MethodDeclarationNode
                    {
                        Name = MakeToken("TestMethod"),
                        ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                        Parameters = null,
                        Body = new MethodBlockBodyMethodBodyNode
                        {
                            Block = new BlockNode
                            {
                                Statements = ImmutableArray<StatementNode>.Empty,
                            }
                        },
                    },
                }),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Should produce a MIST_UNDEFINED_NAMESPACE diagnostic mentioning the namespace name.
        return diagnostics.Any(d =>
            d.Id.Value.Contains("MIST_UNDEFINED_NAMESPACE") &&
            d.Message.Contains(undefinedName));
    }

    /// <summary>
    /// Property 8d (FsCheck property): For any randomly generated identifier name
    /// that is NOT a keyword or well-known type, referencing it in a method body
    /// (without declaring it) produces an undefined diagnostic.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UndefinedIdentifierPropertyTests) }, MaxTest = 100)]
    public bool AnyUndeclaredIdentifier_ProducesUndefinedDiagnostic(string undefinedName)
    {
        // Build: class C { void M() { undefinedName; } }
        // The identifier is never declared, so it must produce a diagnostic.
        var statements = ImmutableArray.Create(
            MakeIdentifierStatement(undefinedName));

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Must produce at least one diagnostic with UNDEFINED in the ID and the name in the message.
        var hasUndefinedDiag = diagnostics.Any(d =>
            d.Id.Value.Contains("UNDEFINED") &&
            d.Message.Contains(undefinedName));

        return hasUndefinedDiag;
    }

    // ─── Targeted unit-style tests ───────────────────────────────────────

    /// <summary>
    /// Verifies that a declared variable does NOT produce an undefined diagnostic,
    /// serving as a sanity check / negative test for the property.
    /// </summary>
    [Fact]
    public void DeclaredVariable_DoesNotProduceUndefinedDiagnostic()
    {
        // Build: class C { void M() { int x = 0; x; } }
        var statements = ImmutableArray.Create(
            MakeIntVarDecl("x", "0"),
            MakeIdentifierStatement("x"));

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("MIST_UNDEFINED_IDENTIFIER") && d.Message.Contains("'x'"));
    }

    /// <summary>
    /// Verifies that referencing an undeclared variable produces exactly one
    /// MIST_UNDEFINED_IDENTIFIER diagnostic with the correct name.
    /// </summary>
    [Fact]
    public void UndeclaredVariable_ProducesExactDiagnostic()
    {
        // Build: class C { void M() { noSuchVar; } }
        var statements = ImmutableArray.Create(
            MakeIdentifierStatement("noSuchVar"));

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var undefinedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("noSuchVar"))
            .ToList();

        _ = Assert.Single(undefinedDiags);
    }

    /// <summary>
    /// Verifies that referencing an undeclared type in a variable declaration
    /// produces a MIST_UNDEFINED_TYPE diagnostic.
    /// </summary>
    [Fact]
    public void UndeclaredType_ProducesExactDiagnostic()
    {
        // Build: class C { void M() { NoSuchType x = 0; } }
        var statements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("NoSuchType", "x"));

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var undefinedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains("NoSuchType"))
            .ToList();

        _ = Assert.Single(undefinedDiags);
    }

    /// <summary>
    /// Verifies that using an undeclared namespace produces a MIST_UNDEFINED_NAMESPACE diagnostic.
    /// </summary>
    [Fact]
    public void UndeclaredNamespace_ProducesExactDiagnostic()
    {
        // Build: using FakeNamespace; class C { }
        var usingDecl = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("FakeNamespace"),
                Span = new Storm.Runtime.SourceSpan(0, "FakeNamespace".Length),
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var undefinedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_NAMESPACE" && d.Message.Contains("FakeNamespace"))
            .ToList();

        _ = Assert.Single(undefinedDiags);
    }

    // ─── Test ambient type resolver ──────────────────────────────────────

    /// <summary>
    /// Minimal ambient type resolver for testing — knows no namespaces or types.
    /// </summary>
    private sealed class EmptyAmbientTypeResolver : IAmbientTypeResolver
    {
        public bool HasNamespace(string namespaceName)
        {
            return false;
        }

        public IType? ResolveType(string namespaceName, string typeName)
        {
            return null;
        }

        public ImmutableArray<ISymbol> GetMembers(IType type)
        {
            return ImmutableArray<ISymbol>.Empty;
        }

        public ImmutableArray<string> GetTypeNamesInNamespace(string namespaceName)
        {
            return ImmutableArray<string>.Empty;
        }

        public IReadOnlyList<AmbientExtensionMethodInfo> GetExtensionMethodsInNamespace(string namespaceName)
        {
            return Array.Empty<AmbientExtensionMethodInfo>();
        }
    }
}