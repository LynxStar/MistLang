// Feature: void-unit-type, Property 4: Non-void to void type mismatch
// **Validates: Requirements 3.4, 4.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that providing a non-void value where VoidType is expected
/// produces a type mismatch diagnostic.
///
/// Property 4: For any non-void type T and any context where a VoidType value is expected
/// (return statement in void method, assignment to void variable, argument to void parameter),
/// providing a value of type T SHALL produce a type mismatch diagnostic.
/// </summary>
public class VoidTypeMismatchPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a non-void type with its corresponding literal expression and type reference.
    /// </summary>
    public enum NonVoidKind
    {
        IntegerType,
        BooleanType,
        StringType,
        FloatingType,
        CharacterType,
    }

    /// <summary>
    /// The context in which a non-void value is provided where void is expected.
    /// </summary>
    public enum MismatchContext
    {
        /// <summary><c>return &lt;T-value&gt;;</c> in a void method (Req 3.4)</summary>
        ReturnInVoidMethod,

        /// <summary><c>void x = &lt;T-value&gt;;</c> (Req 4.4)</summary>
        AssignToVoidVariable,
    }

    /// <summary>
    /// A scenario combining a non-void type kind with a mismatch context.
    /// </summary>
    public record VoidMismatchScenario(
        NonVoidKind TypeKind,
        MismatchContext Context,
        string MethodName,
        string VarName)
    {
        public override string ToString() =>
            $"{TypeKind} value in {Context} (method={MethodName}, var={VarName})";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Run", "Handle", "Init", "Cleanup", "Reset"];

    private static readonly string[] VarNames =
        ["x", "y", "result", "value", "temp", "data"];

    public static Gen<VoidMismatchScenario> GenScenario()
    {
        return from typeKind in Gen.Elements(
                   NonVoidKind.IntegerType,
                   NonVoidKind.BooleanType,
                   NonVoidKind.StringType,
                   NonVoidKind.FloatingType,
                   NonVoidKind.CharacterType)
               from context in Gen.Elements(
                   MismatchContext.ReturnInVoidMethod,
                   MismatchContext.AssignToVoidVariable)
               from methodName in Gen.Elements(MethodNames)
               from varName in Gen.Elements(VarNames)
               select new VoidMismatchScenario(typeKind, context, methodName, varName);
    }

    public static Arbitrary<VoidMismatchScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates a literal expression for the given non-void type kind.
    /// </summary>
    private static ExpressionNode MakeLiteralExpression(NonVoidKind kind)
    {
        return kind switch
        {
            NonVoidKind.IntegerType => MistAstGenerator.WrapPrimary(
                new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("42") }),
            NonVoidKind.BooleanType => MistAstGenerator.WrapPrimary(
                new TrueLiteralExpressionPrimaryNode()),
            NonVoidKind.StringType => MistAstGenerator.WrapPrimary(
                new StringLiteralExpressionPrimaryNode { StringLiteral = Tok("\"hello\"") }),
            NonVoidKind.FloatingType => MistAstGenerator.WrapPrimary(
                new FloatLiteralExpressionPrimaryNode { FloatLiteral = Tok("3.14") }),
            NonVoidKind.CharacterType => MistAstGenerator.WrapPrimary(
                new CharLiteralExpressionPrimaryNode { CharLiteral = Tok("'a'") }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>
    /// Creates a type reference for the given non-void type kind.
    /// </summary>
    private static TypeReferenceNode MakeTypeRef(NonVoidKind kind)
    {
        PrimitiveTypeNode primitiveNode = kind switch
        {
            NonVoidKind.IntegerType => new IntTypePrimitiveTypeNode(),
            NonVoidKind.BooleanType => new BoolTypePrimitiveTypeNode(),
            NonVoidKind.StringType => new StringTypePrimitiveTypeNode(),
            NonVoidKind.FloatingType => new FloatTypePrimitiveTypeNode(),
            NonVoidKind.CharacterType => new CharTypePrimitiveTypeNode(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveNode };
    }

    /// <summary>
    /// Creates a standard Main method for the entry point.
    /// </summary>
    private static MethodDeclarationNode MakeMainMethod(ImmutableArray<StatementNode>? body = null)
    {
        return new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body ?? ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    // ─── Scenario builders ───────────────────────────────────────────────

    /// <summary>
    /// Builds a program where a void method returns a non-void value.
    /// <c>class Program { public static void Main() { } public void MethodName() { return &lt;T-value&gt;; } }</c>
    /// Expected: MIST_TC_0008 diagnostic.
    /// </summary>
    private static CompilationUnitNode BuildReturnInVoidMethod(VoidMismatchScenario scenario)
    {
        var literal = MakeLiteralExpression(scenario.TypeKind);
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = literal },
        };

        var testMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.MethodName),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray.Create<StatementNode>(returnStmt) } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() },
                new MethodMemberMemberDeclarationNode { Method = testMethod }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a program where a non-void value is assigned to a void variable.
    /// <c>class Program { public static void Main() { void x = &lt;T-value&gt;; } }</c>
    /// Expected: MIST_TC_0001 diagnostic.
    /// </summary>
    private static CompilationUnitNode BuildAssignToVoidVariable(VoidMismatchScenario scenario)
    {
        var literal = MakeLiteralExpression(scenario.TypeKind);
        var varDecl = MistAstGenerator.MakeVarDecl(
            new VoidTypeRefTypeReferenceNode(),
            scenario.VarName,
            literal);

        var mainBody = ImmutableArray.Create(varDecl);

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod(mainBody) }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 4: For any non-void type T and any context where a VoidType value is expected,
    /// providing a value of type T SHALL produce a type mismatch diagnostic.
    ///
    /// **Validates: Requirements 3.4, 4.4**
    ///
    /// Note: Requirement 6.3 (argument to void parameter) is validated by unit tests rather than
    /// this property test due to a pre-existing type checker limitation where
    /// MethodCallSuffixPostfixSuffixNode argument type checking requires symbol registration
    /// that isn't available for all call patterns.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(VoidTypeMismatchPropertyTests) }, MaxTest = 100)]
    public bool NonVoidValue_InVoidContext_ProducesDiagnostic(VoidMismatchScenario scenario)
    {
        var ast = scenario.Context switch
        {
            MismatchContext.ReturnInVoidMethod => BuildReturnInVoidMethod(scenario),
            MismatchContext.AssignToVoidVariable => BuildAssignToVoidVariable(scenario),
            _ => throw new ArgumentOutOfRangeException(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var expectedDiagnosticId = scenario.Context switch
        {
            MismatchContext.ReturnInVoidMethod => "MIST_TC_0008",
            MismatchContext.AssignToVoidVariable => "MIST_TC_0001",
            _ => throw new ArgumentOutOfRangeException(),
        };

        var errors = diagnostics.Where(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id.Value == expectedDiagnosticId).ToList();

        return errors.Count > 0;
    }
}
