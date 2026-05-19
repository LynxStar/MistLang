// Feature: structured-concurrency, Property 5: Auto-Unwrap in Sync Context
// **Validates: Requirements 5.1, 5.2, 5.3**

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
/// Property-based tests verifying auto-unwrap behavior for async method calls in sync contexts.
///
/// **Validates: Requirements 5.1, 5.2, 5.3**
///
/// For any invocation of an async method within a sync context (a method body without
/// the <c>async</c> modifier) where the <c>async</c> functor is not used, the expression
/// type at the call site SHALL be <c>T</c> (the unwrapped type), not <c>Task&lt;T&gt;</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class SyncContextAutoUnwrapPropertyTests
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
    /// A scenario describing a direct call to an async method from a sync context.
    /// </summary>
    public record SyncAutoUnwrapScenario(
        string ClassName,
        string AsyncMethodName,
        string ReturnTypeName,
        bool IsVoid)
    {
        public override string ToString()
        {
            var retType = IsVoid ? "void" : ReturnTypeName;
            return $"class {ClassName} {{ public async {retType} {AsyncMethodName}() {{}} " +
                   $"public void Caller() {{ var x = {AsyncMethodName}(); }} }}";
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

    public static Gen<SyncAutoUnwrapScenario> GenSyncAutoUnwrapScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               select new SyncAutoUnwrapScenario(
                   className, methodName, returnType, returnType == "void");
    }

    public static Arbitrary<SyncAutoUnwrapScenario> ArbitrarySyncAutoUnwrapScenario() =>
        GenSyncAutoUnwrapScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds an AST representing a class with:
    /// - An async method with the specified return type
    /// - A sync Caller method containing: var x = AsyncMethod();
    ///
    /// Returns the AST and a reference to the PostfixNode representing the
    /// method invocation so we can query its type from the semantic model.
    /// </summary>
    private static (CompilationUnitNode Ast, PostfixNode InvocationNode) BuildAst(
        SyncAutoUnwrapScenario scenario)
    {
        // Build the async target method
        TypeReferenceNode returnTypeRef = scenario.IsVoid
            ? new VoidTypeRefTypeReferenceNode()
            : MakePrimitiveTypeRef(scenario.ReturnTypeName);

        var targetMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.AsyncMethodName),
            ReturnType = returnTypeRef,
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new AsyncModifierModifierNode()),
        };

        // Build the method invocation: AsyncMethod()
        var callPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(scenario.AsyncMethodName) };
        var invocationSuffix = new InvocationSuffixPostfixSuffixNode();
        var callPostfix = new PostfixNode
        {
            Target = callPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(invocationSuffix),
        };
        var callUnary = new PostfixExpressionUnaryNode { Primary = callPostfix };

        // Build the expression chain for: var x = AsyncMethod();
        var mult = new MultiplicativeNode { Left = callUnary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        var expr = new ExpressionNode { Assignment = assign };

        // Build: var x = AsyncMethod();
        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = Tok("x"),
                Initializer = expr,
            },
        };

        // Build Caller method (sync context — no async modifier)
        var callerMethod = new MethodDeclarationNode
        {
            Name = Tok("Caller"),
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
                new MethodMemberMemberDeclarationNode { Method = callerMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        return (ast, callPostfix);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Auto-Unwrap in Sync Context
    //
    // **Validates: Requirements 5.1, 5.2, 5.3**
    //
    // For any invocation of an async method within a sync context where the
    // `async` functor is not used, the expression type at the call site
    // SHALL be T (the unwrapped type), not Task<T>.
    //
    // This covers:
    // - 5.1: Async method invoked in sync context → implicitly awaited, type is T
    // - 5.2: Result assigned to variable → variable type is T, not Task<T>
    // - 5.3: Only way to get Task<T> in sync context is via async functor
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SyncContextAutoUnwrapPropertyTests) })]
    public Property SyncContext_AsyncMethodCall_TypeIsT_NotTaskOfT(SyncAutoUnwrapScenario scenario)
    {
        var (ast, invocationNode) = BuildAst(scenario);
        var (model, diagnostics) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Query the semantic model for the type of the invocation expression
        var exprType = model.GetTypeOf(invocationNode);

        if (exprType is null)
            return false.ToProperty()
                .Label($"GetTypeOf returned null for invocation in scenario: {scenario}");

        // The expression type must NOT be a Task<T> (ConstructedType with Standard.Task)
        if (exprType is ConstructedType constructedType
            && constructedType.GenericDefinition is ClassType taskClass
            && taskClass.DisplayName == "Task"
            && taskClass.ContainingNamespace == "Standard")
        {
            return false.ToProperty()
                .Label($"Expression type is Task<T> but should be auto-unwrapped to T. " +
                       $"Actual: {exprType.DisplayName}. Scenario: {scenario}");
        }

        // Verify the expression type matches the declared return type of the async method
        var expectedDisplayName = ResolvedDisplayNames[scenario.ReturnTypeName];

        if (exprType.DisplayName != expectedDisplayName)
            return false.ToProperty()
                .Label($"Expression type mismatch. Expected '{expectedDisplayName}' " +
                       $"(from declared return type '{scenario.ReturnTypeName}'), " +
                       $"got '{exprType.DisplayName}'. Scenario: {scenario}");

        return true.ToProperty();
    }
}
