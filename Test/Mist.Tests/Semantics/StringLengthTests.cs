using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests verifying that <c>string.Length()</c> resolves to type <c>int</c>
/// without crashing or producing error diagnostics (Bug 3 fix).
///
/// **Validates: Requirements 2.4, 2.5**
/// </summary>
public class StringLengthTests
{
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

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        IAmbientTypeResolver? ambientTypeResolver = null,
        string source = "")
    {
        // After Task 42, String is a stdlib type (not an ambient type), so
        // tests that reference string.Length() need the stdlib loaded.
        return Helpers.StdlibTestHelper.BuildModelWithStdlib(ast, ambientTypeResolver, source);
    }

    private static UsingDeclarationNode MakeUsingDeclaration(string namespaceName)
    {
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken(namespaceName),
            Span = new Storm.Runtime.SourceSpan(0, namespaceName.Length),
        };
        return new UsingDeclarationNode { Name = qualifiedId };
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
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        return WrapPrimaryAsExpression(new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        });
    }

    /// <summary>
    /// Creates an expression like <c>identifier.MethodName()</c> — a method call
    /// with no arguments on an identifier target.
    /// </summary>
    private static ExpressionNode MakeMethodCallExpression(string targetName, string methodName)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(targetName),
        };
        var methodCallSuffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = MakeToken(methodName),
            Arguments = null, // No arguments
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(methodCallSuffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Builds a class with a method that declares a string parameter and calls
    /// a method on it (e.g., <c>input.Length()</c>), returning the result as a
    /// typed local variable.
    /// </summary>
    private static CompilationUnitNode BuildStringMethodCallProgram(
        string methodCallName,
        string returnTypeName = "int")
    {
        // Parameter: string input
        var param = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            } },
            Name = MakeToken("input"),
        };

        // Variable declaration: <returnType> result = input.<methodCallName>();
        var methodCallExpr = MakeMethodCallExpression("input", methodCallName);

        PrimitiveTypeNode primitiveTypeNode = returnTypeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };

        var varDecl = new TypedVariableDeclarationVariableDeclarationStatementNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveTypeNode } },
            Name = MakeToken("result"),
            Initializer = methodCallExpr,
        };

        var body = new BlockNode
        {
            Statements = ImmutableArray.Create<StatementNode>(
                new VariableDeclarationStatementStatementNode
                {
                    VariableDeclaration = varDecl,
                }),
        };

        var method = new MethodDeclarationNode
        {
            Name = MakeToken("TestMethod"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = param },
            Body = body.AsMethodBody(),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };

        return new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("System")),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Test 1: <c>string.Length()</c> resolves to <c>int</c> — no crash, no error diagnostics.
    /// </summary>
    [Fact]
    public void StringLength_ResolvesToInt_NoCrash_NoErrorDiagnostics()
    {
        var ast = BuildStringMethodCallProgram("Length", "int");
        var resolver = new CSharpAmbientTypeResolver();
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // No crash occurred (we got here)
        // No MIST_SEMANTIC_ERROR diagnostics
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_SEMANTIC_ERROR");

        // No MIST_UNDEFINED_MEMBER diagnostics for Length
        Assert.DoesNotContain(diagnostics, d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains("Length"));

        // The class type should still be registered (AST not dropped)
        Assert.Contains(model.AllSymbols, s => s.Name == "TestClass" && s.Kind == SymbolKind.Class);
    }

    /// <summary>
    /// Test 2: <c>string.Length()</c> in a method body — the local variable
    /// <c>result</c> has type <c>int</c>.
    /// </summary>
    [Fact]
    public void StringLength_InMethodBody_ResultTypeIsInt()
    {
        var ast = BuildStringMethodCallProgram("Length", "int");
        var resolver = new CSharpAmbientTypeResolver();
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // No semantic errors
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_SEMANTIC_ERROR");

        // The 'result' local variable should be registered with type int
        var resultSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "result" && s.Kind == SymbolKind.LocalVariable);

        Assert.NotNull(resultSymbol);
        Assert.NotNull(resultSymbol!.DeclaredType);
        // After the stdlib migration, LegacyPrimitiveTypes.Int returns the stdlib
        // Standard.Int32 StructType. In parallel test execution, the
        // PrimitiveTypeRegistry may be in different states, so we check
        // the display name rather than reference equality.
        Assert.True(
            resultSymbol.DeclaredType!.DisplayName == "int" ||
            resultSymbol.DeclaredType!.DisplayName == "Int32",
            $"Expected result type to be int/Int32, got '{resultSymbol.DeclaredType.DisplayName}'");
    }

    /// <summary>
    /// Test 3: <c>string.Length()</c> with no arguments — works correctly
    /// (no crash from null argument list).
    /// </summary>
    [Fact]
    public void StringLength_NoArguments_WorksCorrectly()
    {
        var ast = BuildStringMethodCallProgram("Length", "int");
        var resolver = new CSharpAmbientTypeResolver();

        // This should not throw — the key assertion is that we don't crash
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // No crash diagnostics
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_SEMANTIC_ERROR");

        // The file's AST should be preserved (class is still registered)
        Assert.Contains(model.AllSymbols, s => s.Name == "TestClass" && s.Kind == SymbolKind.Class);

        // The method should be registered
        Assert.Contains(model.AllSymbols, s => s.Name == "TestMethod" && s.Kind == SymbolKind.Method);

        // The parameter should be registered
        Assert.Contains(model.AllSymbols, s => s.Name == "input" && s.Kind == SymbolKind.Parameter);
    }
}
