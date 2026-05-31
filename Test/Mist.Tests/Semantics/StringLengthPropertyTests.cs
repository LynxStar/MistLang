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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for string method call resolution (Bug 3 fix).
///
/// Property 3 (Bug Condition): For any string method call including Length,
/// the semantic model builder should not crash and should resolve the correct
/// return type.
///
/// Property 6 (Preservation): For string methods other than Length (Contains,
/// Substring) and Array.Length, the resolution should continue to work correctly
/// after the fix.
/// </summary>
public class StringLengthPropertyTests
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
        // After Task 42, String and Array are stdlib types (not ambient types),
        // so tests that reference string.Length() or array.Length need the stdlib loaded.
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
            Arguments = null,
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
    /// Creates an expression like <c>identifier.MethodName(arg)</c> — a method call
    /// with a single string literal argument.
    /// </summary>
    private static ExpressionNode MakeMethodCallWithStringArgExpression(string targetName, string methodName, string argValue)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(targetName),
        };

        // Build the argument: a string literal expression
        var argPrimary = new StringLiteralExpressionPrimaryNode
        {
            StringLiteral = MakeToken($"\"{argValue}\""),
        };
        var argPostfix = new PostfixNode
        {
            Target = argPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var argUnary = new PostfixExpressionUnaryNode { Primary = argPostfix };
        var argMult = new MultiplicativeNode { Left = argUnary };
        var argAdd = new AdditiveNode { Left = argMult };
        var argRel = new RelationalComparisonRelationalNode { Left = argAdd };
        var argEq = new EqualityNode { Left = argRel };
        var argLogAnd = new LogicalAndNode { Left = argEq };
        var argLogOr = new LogicalOrNode { Left = argLogAnd };
        var argAssign = new AssignmentNode { Target = new VoidCoalescingNode { Left = argLogOr } };
        var argExpr = new ExpressionNode { Assignment = argAssign };

        var argList = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = argExpr } };

        var methodCallSuffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = MakeToken(methodName),
            Arguments = argList,
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
    /// Creates an expression like <c>identifier.MethodName(intArg)</c> — a method call
    /// with a single integer literal argument.
    /// </summary>
    private static ExpressionNode MakeMethodCallWithIntArgExpression(string targetName, string methodName, string argValue)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(targetName),
        };

        var argPrimary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(argValue),
        };
        var argPostfix = new PostfixNode
        {
            Target = argPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var argUnary = new PostfixExpressionUnaryNode { Primary = argPostfix };
        var argMult = new MultiplicativeNode { Left = argUnary };
        var argAdd = new AdditiveNode { Left = argMult };
        var argRel = new RelationalComparisonRelationalNode { Left = argAdd };
        var argEq = new EqualityNode { Left = argRel };
        var argLogAnd = new LogicalAndNode { Left = argEq };
        var argLogOr = new LogicalOrNode { Left = argLogAnd };
        var argAssign = new AssignmentNode { Target = new VoidCoalescingNode { Left = argLogOr } };
        var argExpr = new ExpressionNode { Assignment = argAssign };

        var argList = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = argExpr } };

        var methodCallSuffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = MakeToken(methodName),
            Arguments = argList,
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
    /// Builds a program with a method that declares a string parameter and calls
    /// a method on it, storing the result in a typed local variable.
    /// </summary>
    private static CompilationUnitNode BuildStringMethodCallProgram(
        string methodCallName,
        string returnTypeName,
        ExpressionNode callExpr)
    {
        var param = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            } },
            Name = MakeToken("input"),
        };

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
            Initializer = callExpr,
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

    /// <summary>
    /// Builds a program with a method that declares an array parameter and calls
    /// a method on it (e.g., <c>arr.Length()</c>).
    /// </summary>
    private static CompilationUnitNode BuildArrayMethodCallProgram(string methodCallName)
    {
        var param = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
                ArrayRank = new UnsizedArrayRankArrayRankSuffixNode(),
            } },
            Name = MakeToken("arr"),
        };

        var callExpr = MakeMethodCallExpression("arr", methodCallName);

        var varDecl = new TypedVariableDeclarationVariableDeclarationStatementNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Name = MakeToken("result"),
            Initializer = callExpr,
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

    // ─── Test scenario records ───────────────────────────────────────────

    /// <summary>
    /// Represents a test scenario for a string method call.
    /// </summary>
#pragma warning disable CA1034
    public record StringMethodCallScenario(string MethodName, string ExpectedReturnType)
    {
        public override string ToString() =>
            $"Method={MethodName}, ExpectedReturn={ExpectedReturnType}";
    }

    /// <summary>
    /// Represents a preservation test scenario for non-Length string methods
    /// and Array.Length.
    /// </summary>
    public record PreservationScenario(string TargetType, string MethodName, string ExpectedReturnType)
    {
        public override string ToString() =>
            $"Target={TargetType}, Method={MethodName}, ExpectedReturn={ExpectedReturnType}";
    }
#pragma warning restore CA1034

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    /// <summary>
    /// Generates random string method call scenarios including Length, Contains,
    /// and Substring.
    /// </summary>
    public static Arbitrary<StringMethodCallScenario> ArbitraryStringMethodCallScenario()
    {
        var gen = Gen.Elements(
            new StringMethodCallScenario("Length", "int"),
            new StringMethodCallScenario("Contains", "bool"),
            new StringMethodCallScenario("Substring", "string"));
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates preservation scenarios: Contains (returns bool), Substring
    /// (returns string), and Array.Length (returns int).
    /// </summary>
    public static Arbitrary<PreservationScenario> ArbitraryPreservationScenario()
    {
        var gen = Gen.Elements(
            new PreservationScenario("string", "Contains", "bool"),
            new PreservationScenario("string", "Substring", "string"),
            new PreservationScenario("array", "Length", "int"));
        return gen.ToArbitrary();
    }

    // ─── Property 3 (Bug Condition) ─────────────────────────────────────

    /// <summary>
    /// Property 3: For any string method call (Length, Contains, Substring),
    /// the semantic model builder should not crash and should resolve the
    /// correct return type.
    ///
    /// **Validates: Requirements 2.4, 2.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(StringLengthPropertyTests) }, MaxTest = 50)]
    public bool StringMethodCall_NoCrash_CorrectTypeResolution(StringMethodCallScenario scenario)
    {
        ExpressionNode callExpr;
        if (scenario.MethodName == "Contains")
        {
            callExpr = MakeMethodCallWithStringArgExpression("input", scenario.MethodName, "x");
        }
        else if (scenario.MethodName == "Substring")
        {
            callExpr = MakeMethodCallWithIntArgExpression("input", scenario.MethodName, "0");
        }
        else
        {
            // Length — no arguments
            callExpr = MakeMethodCallExpression("input", scenario.MethodName);
        }

        var ast = BuildStringMethodCallProgram(scenario.MethodName, scenario.ExpectedReturnType, callExpr);
        var resolver = new CSharpAmbientTypeResolver();
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // No crash (MIST_SEMANTIC_ERROR)
        if (diagnostics.Any(d => d.Id.Value == "MIST_SEMANTIC_ERROR"))
            return false;

        // No undefined member for the method
        if (diagnostics.Any(d => d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName)))
            return false;

        // The class type should still be registered (AST not dropped)
        if (!model.AllSymbols.Any(s => s.Name == "TestClass" && s.Kind == SymbolKind.Class))
            return false;

        return true;
    }

    // ─── Property 6 (Preservation) ──────────────────────────────────────

    /// <summary>
    /// Property 6: For string methods other than Length (Contains, Substring)
    /// and Array.Length, the resolution should continue to work correctly
    /// after the fix. This is a preservation test.
    ///
    /// **Validates: Requirements 3.3, 3.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(StringLengthPropertyTests) }, MaxTest = 50)]
    public bool PreservationTest_OtherStringMethods_AndArrayLength_ResolveCorrectly(PreservationScenario scenario)
    {
        var resolver = new CSharpAmbientTypeResolver();

        if (scenario.TargetType == "array")
        {
            // Array.Length test
            var ast = BuildArrayMethodCallProgram(scenario.MethodName);
            var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

            // No crash
            if (diagnostics.Any(d => d.Id.Value == "MIST_SEMANTIC_ERROR"))
                return false;

            // No undefined member
            if (diagnostics.Any(d => d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName)))
                return false;

            // Class still registered
            if (!model.AllSymbols.Any(s => s.Name == "TestClass" && s.Kind == SymbolKind.Class))
                return false;

            return true;
        }
        else
        {
            // String method test (Contains or Substring)
            ExpressionNode callExpr;
            if (scenario.MethodName == "Contains")
            {
                callExpr = MakeMethodCallWithStringArgExpression("input", scenario.MethodName, "x");
            }
            else if (scenario.MethodName == "Substring")
            {
                callExpr = MakeMethodCallWithIntArgExpression("input", scenario.MethodName, "0");
            }
            else
            {
                callExpr = MakeMethodCallExpression("input", scenario.MethodName);
            }

            var ast = BuildStringMethodCallProgram(scenario.MethodName, scenario.ExpectedReturnType, callExpr);
            var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

            // No crash
            if (diagnostics.Any(d => d.Id.Value == "MIST_SEMANTIC_ERROR"))
                return false;

            // No undefined member
            if (diagnostics.Any(d => d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName)))
                return false;

            // Class still registered
            if (!model.AllSymbols.Any(s => s.Name == "TestClass" && s.Kind == SymbolKind.Class))
                return false;

            return true;
        }
    }
}
