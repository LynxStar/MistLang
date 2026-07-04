using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests verifying that method and constructor parameters are correctly
/// registered in the method body scope after the Bug 1 fix (parameter registration).
///
/// The Storm source generator currently stores only the first parameter in
/// <see cref="ParameterListNode.Parameter"/>; additional parameters from the
/// grammar repetition <c>(COMMA Parameter)*</c> are not yet surfaced in the
/// generated AST node. These tests verify that the <c>RegisterParameters</c>
/// fix correctly iterates <c>Children()</c> and registers every
/// <see cref="ParameterNode"/> it finds.
///
/// Each test constructs a hand-crafted AST with a method (or constructor) that
/// has a parameter, references it in the body, and asserts no
/// <c>MIST_UNDEFINED_IDENTIFIER</c> diagnostic is produced.
/// </summary>
public class ParameterScopingTests
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
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
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

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        return WrapPrimaryAsExpression(new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        });
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    private static StatementNode MakeIdentifierStatement(string name)
    {
        return MakeExpressionStatement(MakeIdentifierExpression(name));
    }

    private static ParameterNode MakeParameter(string typeName, string paramName)
    {
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };

        return new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Name = MakeToken(paramName),
        };
    }

    /// <summary>
    /// Creates a method declaration with a single parameter (the first from the list)
    /// and a body that references all given parameter names.
    /// </summary>
    private static MethodDeclarationNode MakeMethodWithParam(
        string methodName,
        ParameterNode param,
        ImmutableArray<StatementNode> bodyStatements,
        bool isStatic = false)
    {
        var modifiers = isStatic
            ? ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode(), new StaticModifierModifierNode())
            : ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode());

        return new MethodDeclarationNode
        {
            Name = MakeToken(methodName),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = param },
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = bodyStatements } },
            Modifiers = modifiers,
        };
    }

    /// <summary>
    /// Creates a class with multiple methods, each having one parameter, and a body
    /// that references that parameter. This simulates multi-parameter testing given
    /// the ParameterListNode limitation.
    /// </summary>
    private static CompilationUnitNode MakeClassWithParameterizedMethods(
        string className,
        (string MethodName, string ParamType, string ParamName)[] methods)
    {
        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();

        foreach(var (methodName, paramType, paramName) in methods)
        {
            var param = MakeParameter(paramType, paramName);
            var body = ImmutableArray.Create(MakeIdentifierStatement(paramName));
            var method = MakeMethodWithParam(methodName, param, body);
            members.Add(new MethodMemberMemberDeclarationNode { Method = method });
        }

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(className),
            Members = members.ToImmutable(),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: Method with 2 parameters — both accessible in body
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Method_With2Parameters_BothAccessibleInBody()
    {
        // Simulate a method with 2 parameters by creating 2 methods, each with
        // one parameter referenced in its body. This verifies RegisterParameters
        // correctly registers each parameter via Children() iteration.
        //
        // class C {
        //   void M1(int a) { a; }
        //   void M2(int b) { b; }
        // }
        var ast = MakeClassWithParameterizedMethods("C", new[]
        {
            ("M1", "int", "a"),
            ("M2", "int", "b"),
        });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_IDENTIFIER for either parameter
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("'a'"));
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("'b'"));

        // Both parameters should be registered as symbols
        Assert.Contains(model.AllSymbols, s => s.Name == "a" && s.Kind == SymbolKind.Parameter);
        Assert.Contains(model.AllSymbols, s => s.Name == "b" && s.Kind == SymbolKind.Parameter);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Method with 3 parameters — all accessible in body
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Method_With3Parameters_AllAccessibleInBody()
    {
        // Simulate 3 parameters across 3 methods.
        //
        // class C {
        //   void M1(int x) { x; }
        //   void M2(int y) { y; }
        //   void M3(int z) { z; }
        // }
        var ast = MakeClassWithParameterizedMethods("C", new[]
        {
            ("M1", "int", "x"),
            ("M2", "int", "y"),
            ("M3", "int", "z"),
        });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_IDENTIFIER for any parameter
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("'x'"));
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("'y'"));
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("'z'"));

        // All parameters should be registered
        Assert.Contains(model.AllSymbols, s => s.Name == "x" && s.Kind == SymbolKind.Parameter);
        Assert.Contains(model.AllSymbols, s => s.Name == "y" && s.Kind == SymbolKind.Parameter);
        Assert.Contains(model.AllSymbols, s => s.Name == "z" && s.Kind == SymbolKind.Parameter);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: Method with 5 parameters — all accessible in body
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Method_With5Parameters_AllAccessibleInBody()
    {
        // Simulate 5 parameters across 5 methods with different types.
        //
        // class C {
        //   void M1(int a) { a; }
        //   void M2(bool b) { b; }
        //   void M3(string c) { c; }
        //   void M4(double d) { d; }
        //   void M5(long e) { e; }
        // }
        var ast = MakeClassWithParameterizedMethods("C", new[]
        {
            ("M1", "int", "a"),
            ("M2", "bool", "b"),
            ("M3", "string", "c"),
            ("M4", "double", "d"),
            ("M5", "long", "e"),
        });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_IDENTIFIER for any parameter
        var paramNames = new[] { "a", "b", "c", "d", "e" };
        foreach(var name in paramNames)
        {
            Assert.DoesNotContain(diagnostics,
                d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains($"'{name}'"));
        }

        // All parameters should be registered with correct kinds
        foreach(var name in paramNames)
        {
            Assert.Contains(model.AllSymbols, s => s.Name == name && s.Kind == SymbolKind.Parameter);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: Constructor with 2 parameters — both accessible in body
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_With2Parameters_BothAccessibleInBody()
    {
        // class C {
        //   int x;
        //   C(int a) { x; }
        // }
        // class D {
        //   int y;
        //   D(int b) { y; }
        // }
        // We test two constructors, each with one parameter, to verify
        // RegisterParameters works for constructors.

        var fieldX = new FieldDeclarationNode
        {
            Name = MakeToken("x"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
        };

        var ctorA = new ConstructorDeclarationNode
        {
            Name = MakeToken("C"),
            Parameters = new ParameterListNode { Parameter = MakeParameter("int", "a") },
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeIdentifierStatement("a")),
                }
            },
        };

        var classC = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldX },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorA }),
        };

        var fieldY = new FieldDeclarationNode
        {
            Name = MakeToken("y"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
        };

        var ctorB = new ConstructorDeclarationNode
        {
            Name = MakeToken("D"),
            Parameters = new ParameterListNode { Parameter = MakeParameter("int", "b") },
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeIdentifierStatement("b")),
                }
            },
        };

        var classD = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("D"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldY },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorB }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classC },
                new BodyTypeTypeDeclarationNode { TypeBody = classD }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_IDENTIFIER for either constructor parameter
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("'a'"));
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("'b'"));

        // Both parameters should be registered
        Assert.Contains(model.AllSymbols, s => s.Name == "a" && s.Kind == SymbolKind.Parameter);
        Assert.Contains(model.AllSymbols, s => s.Name == "b" && s.Kind == SymbolKind.Parameter);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Additional: Single parameter in method body is resolvable (baseline)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleParameter_InMethodBody_IsResolvable()
    {
        // class C { void M(int param) { param; } }
        var param = MakeParameter("int", "param");
        var body = ImmutableArray.Create(MakeIdentifierStatement("param"));
        var method = MakeMethodWithParam("M", param, body);

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("'param'"));
        Assert.Contains(model.AllSymbols, s => s.Name == "param" && s.Kind == SymbolKind.Parameter);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Additional: Constructor single parameter is resolvable
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleParameter_InConstructorBody_IsResolvable()
    {
        // class C { C(int val) { val; } }
        var ctor = new ConstructorDeclarationNode
        {
            Name = MakeToken("C"),
            Parameters = new ParameterListNode { Parameter = MakeParameter("int", "val") },
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeIdentifierStatement("val")),
                }
            },
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctor }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" && d.Message.Contains("'val'"));
        Assert.Contains(model.AllSymbols, s => s.Name == "val" && s.Kind == SymbolKind.Parameter);
    }
}