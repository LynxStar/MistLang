// Feature: structured-concurrency, Property 3: Async Functor Expression Type
// **Validates: Requirements 3.1, 3.2, 3.3, 3.5**

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
/// Property-based tests verifying async functor expression type resolution in the semantic model.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.5**
///
/// For any method invocation expression preceded by the <c>async</c> functor, if the method
/// has return type <c>T</c>, the resulting expression SHALL have type <c>Task&lt;T&gt;</c>
/// in the semantic model.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AsyncFunctorTypePropertyTests
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
        return new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveNode };
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing an async functor applied to a method invocation.
    /// </summary>
    public record AsyncFunctorScenario(
        string ClassName,
        string TargetMethodName,
        string ReturnTypeName,
        bool IsTargetAsync,
        bool IsVoid)
    {
        public override string ToString()
        {
            var asyncMod = IsTargetAsync ? "async " : "";
            var retType = IsVoid ? "void" : ReturnTypeName;
            return $"class {ClassName} {{ public {asyncMod}{retType} {TargetMethodName}() {{}} " +
                   $"public void Main() {{ var t = async {TargetMethodName}(); }} }}";
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

    public static Gen<AsyncFunctorScenario> GenAsyncFunctorScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               from isTargetAsync in Gen.Elements(true, false)
               select new AsyncFunctorScenario(
                   className, methodName, returnType, isTargetAsync, returnType == "void");
    }

    public static Arbitrary<AsyncFunctorScenario> ArbitraryAsyncFunctorScenario() =>
        GenAsyncFunctorScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds an AST representing a class with:
    /// - A target method (sync or async) with the specified return type
    /// - A sync Main method containing: var t = async TargetMethod();
    ///
    /// Returns the AST and a reference to the AsyncExpressionUnaryNode so we can
    /// query its type from the semantic model.
    /// </summary>
    private static (CompilationUnitNode Ast, AsyncExpressionUnaryNode AsyncExprNode) BuildAst(
        AsyncFunctorScenario scenario)
    {
        // Build the target method (the one being called with async functor)
        TypeReferenceNode returnTypeRef = scenario.IsVoid
            ? new VoidTypeRefTypeReferenceNode()
            : MakePrimitiveTypeRef(scenario.ReturnTypeName);

        var targetModifiers = ImmutableArray.CreateBuilder<ModifierNode>();
        targetModifiers.Add(new PublicModifierModifierNode());
        if (scenario.IsTargetAsync)
            targetModifiers.Add(new AsyncModifierModifierNode());

        var targetMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.TargetMethodName),
            ReturnType = returnTypeRef,
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = targetModifiers.ToImmutable(),
        };

        // Build the async functor expression: async TargetMethod()
        // First, build the method invocation: TargetMethod()
        var callPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(scenario.TargetMethodName) };
        var invocationSuffix = new InvocationSuffixPostfixSuffixNode();
        var callPostfix = new PostfixNode
        {
            Target = callPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(invocationSuffix),
        };
        var callUnary = new PostfixExpressionUnaryNode { Primary = callPostfix };

        // Wrap in async functor: async TargetMethod()
        var asyncExprNode = new AsyncExpressionUnaryNode { Operand = callUnary };

        // Build the expression chain for: var t = async TargetMethod();
        var mult = new MultiplicativeNode { Left = asyncExprNode };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        var expr = new ExpressionNode { Assignment = assign };

        // Build: var t = async TargetMethod();
        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = Tok("t"),
                Initializer = expr,
            },
        };

        // Build Main method (sync context — no async modifier)
        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
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

        return (ast, asyncExprNode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Async Functor Expression Type
    //
    // **Validates: Requirements 3.1, 3.2, 3.3, 3.5**
    //
    // For any method invocation expression preceded by the `async` functor,
    // if the method has return type T, the resulting expression SHALL have
    // type Task<T> in the semantic model.
    //
    // This covers:
    // - 3.1: async wraps invocation in Task
    // - 3.2: async on method with return type T → Task<T>
    // - 3.3: async on method with return type void → Task<Void>
    // - 3.5: async on async method in sync context → Task<T> (cancels auto-unwrap)
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AsyncFunctorTypePropertyTests) })]
    public Property AsyncFunctor_ExpressionType_IsTaskOfT(AsyncFunctorScenario scenario)
    {
        var (ast, asyncExprNode) = BuildAst(scenario);
        var (model, diagnostics) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Query the semantic model for the type of the async expression node
        var exprType = model.GetTypeOf(asyncExprNode);

        if (exprType is null)
            return false.ToProperty()
                .Label($"GetTypeOf returned null for async expression in scenario: {scenario}");

        // The expression type must be a ConstructedType (Task<T>)
        if (exprType is not ConstructedType constructedType)
            return false.ToProperty()
                .Label($"Expression type is not a ConstructedType. " +
                       $"Actual: {exprType.GetType().Name} ({exprType.DisplayName}). " +
                       $"Scenario: {scenario}");

        // Verify the generic definition is Standard.Task
        if (constructedType.GenericDefinition is not ClassType taskClass)
            return false.ToProperty()
                .Label($"GenericDefinition is not a ClassType. " +
                       $"Actual: {constructedType.GenericDefinition.GetType().Name}. " +
                       $"Scenario: {scenario}");

        if (taskClass.DisplayName != "Task")
            return false.ToProperty()
                .Label($"GenericDefinition DisplayName is not 'Task'. " +
                       $"Actual: '{taskClass.DisplayName}'. Scenario: {scenario}");

        if (taskClass.ContainingNamespace != "Standard")
            return false.ToProperty()
                .Label($"GenericDefinition namespace is not 'Standard'. " +
                       $"Actual: '{taskClass.ContainingNamespace}'. Scenario: {scenario}");

        // Verify there is exactly one type argument
        if (constructedType.TypeArguments.Count != 1)
            return false.ToProperty()
                .Label($"Expected 1 type argument, got {constructedType.TypeArguments.Count}. " +
                       $"Scenario: {scenario}");

        // Verify the type argument matches the declared return type of the target method.
        // For both sync and async target methods, the async functor should produce Task<T>
        // where T is the declared return type (not Task<Task<T>>), because:
        // - For sync methods: async wraps T → Task<T>
        // - For async methods in sync context: the operand is auto-unwrapped to T first,
        //   then async wraps T → Task<T> (Requirement 3.5)
        var typeArg = constructedType.TypeArguments[0];
        var expectedDisplayName = ResolvedDisplayNames[scenario.ReturnTypeName];

        if (typeArg.DisplayName != expectedDisplayName)
            return false.ToProperty()
                .Label($"Type argument mismatch. Expected '{expectedDisplayName}' " +
                       $"(from return type '{scenario.ReturnTypeName}'), " +
                       $"got '{typeArg.DisplayName}'. Scenario: {scenario}");

        return true.ToProperty();
    }
}
