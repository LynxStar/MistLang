// Feature: structured-concurrency, Property 4: Await Functor Unwrap Type
// **Validates: Requirements 4.1, 4.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying await functor unwrap type resolution in the semantic model.
///
/// **Validates: Requirements 4.1, 4.2**
///
/// For any expression of type <c>Task&lt;T&gt;</c> preceded by the <c>await</c> functor,
/// the resulting expression SHALL have type <c>T</c> in the semantic model.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AwaitUnwrapTypePropertyTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string primitiveName)
    {
        PrimitiveTypeNode primitiveNode = primitiveName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveNode } };
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing an await functor applied to a Task&lt;T&gt; expression.
    /// The program has an async method returning T, and a caller that does:
    ///   var x = await async TargetMethod();
    /// The async functor produces Task&lt;T&gt;, then await unwraps it to T.
    /// </summary>
    public record AwaitUnwrapScenario(
        string ClassName,
        string TargetMethodName,
        string ReturnTypeName,
        bool IsVoid)
    {
        public override string ToString()
        {
            var retType = IsVoid ? "void" : ReturnTypeName;
            return $"class {ClassName} {{ public {retType} {TargetMethodName}() {{}} " +
                   $"public void Main() {{ var x = await async {TargetMethodName}(); }} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Worker", "Service", "Handler", "Processor"];
    private static readonly string[] MethodNames = ["Compute", "Fetch", "Process", "Execute", "Load"];
    private static readonly string[] ReturnTypes = ["int", "string", "bool", "float", "void"];

    /// <summary>
    /// Maps primitive keyword names to their resolved stdlib display names.
    /// </summary>
    private static readonly Dictionary<string, string> ResolvedDisplayNames = new()
    {
        ["int"] = "Int32",
        ["string"] = "String",
        ["bool"] = "Boolean",
        ["float"] = "Single",
        ["void"] = "Void",
    };

    public static Gen<AwaitUnwrapScenario> GenAwaitUnwrapScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               select new AwaitUnwrapScenario(
                   className, methodName, returnType, returnType == "void");
    }

    public static Arbitrary<AwaitUnwrapScenario> ArbitraryAwaitUnwrapScenario() =>
        GenAwaitUnwrapScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds an AST representing a class with:
    /// - A sync target method with the specified return type
    /// - A sync Main method containing: var x = await async TargetMethod();
    ///
    /// The async functor lifts the call to Task&lt;T&gt;, then await unwraps to T.
    /// Returns the AST and a reference to the AwaitExpressionUnaryNode so we can
    /// query its type from the semantic model.
    /// </summary>
    private static (CompilationUnitNode Ast, AwaitExpressionUnaryNode AwaitExprNode) BuildAst(
        AwaitUnwrapScenario scenario)
    {
        // Build the target method (sync, with the specified return type)
        TypeReferenceNode returnTypeRef = scenario.IsVoid
            ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
            : MakePrimitiveTypeRef(scenario.ReturnTypeName);

        var targetMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.TargetMethodName),
            ReturnType = returnTypeRef,
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Build the expression: await async TargetMethod()
        // Step 1: Build the method invocation: TargetMethod()
        var callPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(scenario.TargetMethodName) };
        var invocationSuffix = new InvocationSuffixPostfixSuffixNode();
        var callPostfix = new PostfixNode
        {
            Target = callPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(invocationSuffix),
        };
        var callUnary = new PostfixExpressionUnaryNode { Primary = callPostfix };

        // Step 2: Wrap in async functor: async TargetMethod()  → produces Task<T>
        var asyncExprNode = new AsyncExpressionUnaryNode { Operand = callUnary };

        // Step 3: Wrap in await functor: await async TargetMethod()  → unwraps to T
        var awaitExprNode = new AwaitExpressionUnaryNode { Operand = asyncExprNode };

        // Build the expression chain for: var x = await async TargetMethod();
        var mult = new MultiplicativeNode { Left = awaitExprNode };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assign };

        // Build: var x = await async TargetMethod();
        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = Tok("x"),
                Initializer = expr,
            },
        };

        // Build Main method (sync context — no async modifier)
        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray.Create<StatementNode>(varDecl) },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        // Build the class
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.ClassName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = targetMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        return (ast, awaitExprNode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Await Functor Unwrap Type
    //
    // **Validates: Requirements 4.1, 4.2**
    //
    // For any expression of type Task<T> preceded by the `await` functor,
    // the resulting expression SHALL have type T in the semantic model.
    //
    // This covers:
    // - 4.1: await on Task<T> → type T
    // - 4.2: await on Task<Void> → type void
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AwaitUnwrapTypePropertyTests) })]
    public Property AwaitFunctor_ExpressionType_IsT(AwaitUnwrapScenario scenario)
    {
        var (ast, awaitExprNode) = BuildAst(scenario);
        var (model, diagnostics) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Query the semantic model for the type of the await expression node
        var exprType = model.GetTypeOf(awaitExprNode);

        if(exprType is null)
            return false.ToProperty()
                .Label($"GetTypeOf returned null for await expression in scenario: {scenario}");

        // The expression type should be T (the unwrapped type), not Task<T>
        var expectedDisplayName = ResolvedDisplayNames[scenario.ReturnTypeName];

        // Verify the type is NOT a Task<T> (it should be unwrapped)
        if(exprType is ConstructedType constructedType &&
            constructedType.GenericDefinition is ClassType classType &&
            classType.DisplayName == "Task" &&
            classType.ContainingNamespace == "Standard")
        {
            return false.ToProperty()
                .Label($"Await expression type is still Task<T> (not unwrapped). " +
                       $"Actual: {exprType.DisplayName}. Scenario: {scenario}");
        }

        // Verify the display name matches the expected unwrapped type
        if(exprType.DisplayName != expectedDisplayName)
            return false.ToProperty()
                .Label($"Await expression type mismatch. Expected '{expectedDisplayName}' " +
                       $"(from return type '{scenario.ReturnTypeName}'), " +
                       $"got '{exprType.DisplayName}'. Scenario: {scenario}");

        return true.ToProperty();
    }
}