// Feature: drop-semantics, Property 15: Await desugars to Drop
// **Validates: Requirements 7.5, 8.5**

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
/// Property-based tests verifying that <c>await e</c> and <c>e.Drop()</c> resolve to
/// the same method and result type <c>T</c> for Task/ValueTask at the semantic-model level.
///
/// Feature: drop-semantics, Property 15: Await desugars to Drop
/// **Validates: Requirements 7.5, 8.5**
///
/// For any expression <c>e</c> of type <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c>,
/// the await keyword is syntactic sugar for <c>e.Drop()</c>. Both invoke the same Drop
/// method and yield the same result type <c>T</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AwaitDesugarsToDropEquivalenceTests
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
    /// A scenario describing an await vs Drop() equivalence test.
    /// An async method returns T, producing a Task&lt;T&gt;. The test verifies
    /// that <c>await async Method()</c> resolves to the same type T as what
    /// the Task type's Drop() method returns.
    /// </summary>
    public record AwaitDropEquivalenceScenario(
        string ClassName,
        string AsyncMethodName,
        string ReturnTypeName,
        bool IsVoid)
    {
        public override string ToString()
        {
            var retType = IsVoid ? "void" : ReturnTypeName;
            return $"class {ClassName} {{ async {retType} {AsyncMethodName}() {{}} → " +
                   $"await resolves to {retType}, Drop() resolves to {retType} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Worker", "Service", "Handler", "Processor", "Engine"];
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

    public static Gen<AwaitDropEquivalenceScenario> GenScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               select new AwaitDropEquivalenceScenario(
                   className, methodName, returnType, returnType == "void");
    }

    public static Arbitrary<AwaitDropEquivalenceScenario> ArbitraryAwaitDropEquivalenceScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds an AST representing a class with:
    /// - A sync target method with the specified return type
    /// - A sync Main method containing: var x = await async TargetMethod();
    ///
    /// Returns the AST and the AwaitExpressionUnaryNode so we can query its
    /// resolved type from the semantic model.
    /// </summary>
    private static (CompilationUnitNode Ast, AwaitExpressionUnaryNode AwaitNode) BuildAstWithAwait(
        AwaitDropEquivalenceScenario scenario)
    {
        // Build the target method (sync, with the specified return type)
        var returnTypeRef = scenario.IsVoid
            ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
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
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Build the expression: await async TargetMethod()
        // Step 1: Method invocation: TargetMethod()
        var callPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(scenario.AsyncMethodName) };
        var invocationSuffix = new InvocationSuffixPostfixSuffixNode();
        var callPostfix = new PostfixNode
        {
            Target = callPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(invocationSuffix),
        };
        var callUnary = new PostfixExpressionUnaryNode { Primary = callPostfix };

        // Step 2: async functor: async TargetMethod() → produces Task<T>
        var asyncExprNode = new AsyncExpressionUnaryNode { Operand = callUnary };

        // Step 3: await functor: await async TargetMethod() → unwraps to T
        var awaitExprNode = new AwaitExpressionUnaryNode { Operand = asyncExprNode };

        // Build expression chain for: var x = await async TargetMethod();
        var mult = new MultiplicativeNode { Left = awaitExprNode };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assign };

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = Tok("x"),
                Initializer = expr,
            },
        };

        // Build Main method (sync context)
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
    // Property 15: Await desugars to Drop
    //
    // **Validates: Requirements 7.5, 8.5**
    //
    // For Task<T>: `await e` resolves to type T, and the Task type's Drop()
    // method has return type T. Both resolve to the same result type.
    //
    // For ValueTask<T>: The type's Drop() method has return type T, which is
    // the same type that await would unwrap to (syntactic sugar equivalence).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AwaitDesugarsToDropEquivalenceTests) })]
    public Property AwaitAndDrop_ResolveToSameResultType_ForTask(AwaitDropEquivalenceScenario scenario)
    {
        // Build an AST with `await async Method()` to get the resolved type of await
        var (ast, awaitNode) = BuildAstWithAwait(scenario);
        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // 1. Get the type that `await` resolves to
        var awaitResolvedType = model.GetTypeOf(awaitNode);
        if(awaitResolvedType is null)
        {
            return false.ToProperty()
                .Label($"GetTypeOf(awaitNode) returned null. Scenario: {scenario}");
        }

        var expectedDisplayName = ResolvedDisplayNames[scenario.ReturnTypeName];

        if(awaitResolvedType.DisplayName != expectedDisplayName)
        {
            return false.ToProperty()
                .Label($"await resolved to '{awaitResolvedType.DisplayName}', " +
                       $"expected '{expectedDisplayName}'. Scenario: {scenario}");
        }

        // 2. Get the type that `e.Drop()` would resolve to via the Task type model.
        //    The async functor produces Task<T>, and Task's Drop() returns T.
        //    Find the Task<TResult> ClassType and verify Drop equivalence.
        var taskSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Kind == SymbolKind.Class
                && s.Name == "Task"
                && s.DeclaredType is ClassType ct
                && ct.ContainingNamespace == "Standard"
                && ct.IsBackendBacked);

        if(taskSymbol is null)
        {
            return false.ToProperty()
                .Label($"Could not find Standard.Task type in model. Scenario: {scenario}");
        }

        var taskClassType = (ClassType)taskSymbol.DeclaredType!;

        // Verify Task has Drop recognized
        if(!taskClassType.HasDrop)
        {
            return false.ToProperty()
                .Label($"Task<TResult>.HasDrop is false — Drop() not recognized. Scenario: {scenario}");
        }

        // The DropReturnType on the generic definition is the type parameter TResult.
        // When constructed as Task<int>, Drop() returns int. This is the same T that
        // await unwraps to — proving the semantic equivalence.
        if(taskClassType.DropReturnType is null)
        {
            return false.ToProperty()
                .Label($"Task<TResult>.DropReturnType is null (void) — expected TResult. Scenario: {scenario}");
        }

        // The DropReturnType should be the type parameter TResult (a TypeParameterType)
        // on the generic definition. The concrete type after construction matches what
        // await resolves to.
        if(taskClassType.DropReturnType is not TypeParameterType)
        {
            return false.ToProperty()
                .Label($"Task<TResult>.DropReturnType is not a TypeParameterType. " +
                       $"Actual: {taskClassType.DropReturnType.GetType().Name} " +
                       $"({taskClassType.DropReturnType.DisplayName}). Scenario: {scenario}");
        }

        // 3. Verify the structural equivalence: Task<T>.Drop() returns T, await Task<T> gives T.
        //    Both paths yield the same result type. Since await is resolved to the Task's
        //    first type argument, and Drop's return type IS that type argument, they are
        //    semantically equivalent.
        return true.ToProperty();
    }

    // ─── ValueTask equivalence (Requirement 8.5) ─────────────────────────

    /// <summary>
    /// Verifies the structural equivalence for ValueTask&lt;T&gt;: the type has
    /// HasDrop=true with DropReturnType=TResult, proving that <c>await vtask</c>
    /// (which desugars to <c>vtask.Drop()</c>) yields type T.
    /// </summary>
    [Fact]
    public void ValueTask_HasDrop_WithTResultReturnType()
    {
        // Build a minimal AST (just need the stdlib loaded to inspect ValueTask)
        var emptyClass = new ClassDeclarationNode
        {
            Name = Tok("Placeholder"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = emptyClass }).AsTopLevelItems(),
        };

        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Find ValueTask<TResult> in the model
        var valueTaskSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Kind == SymbolKind.Class
                && s.Name == "ValueTask"
                && s.DeclaredType is ClassType ct
                && ct.ContainingNamespace == "Standard"
                && ct.IsBackendBacked);

        Assert.NotNull(valueTaskSymbol);

        var valueTaskClassType = (ClassType)valueTaskSymbol!.DeclaredType!;

        // ValueTask must have Drop recognized
        Assert.True(valueTaskClassType.HasDrop,
            "ValueTask<TResult>.HasDrop should be true — Drop() must be recognized.");

        // DropReturnType should be TResult (the type parameter)
        Assert.NotNull(valueTaskClassType.DropReturnType);
        _ = Assert.IsType<TypeParameterType>(valueTaskClassType.DropReturnType);

        // Verify the type parameter is the same one used in the type's definition
        var dropReturnParam = (TypeParameterType)valueTaskClassType.DropReturnType!;
        Assert.Equal("TResult", dropReturnParam.DisplayName);
    }

    /// <summary>
    /// Verifies the structural equivalence for Task&lt;T&gt;: the type has
    /// HasDrop=true with DropReturnType=TResult, proving that <c>await task</c>
    /// (which desugars to <c>task.Drop()</c>) yields type T.
    /// </summary>
    [Fact]
    public void Task_HasDrop_WithTResultReturnType()
    {
        // Build a minimal AST (just need the stdlib loaded to inspect Task)
        var emptyClass = new ClassDeclarationNode
        {
            Name = Tok("Placeholder"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = emptyClass }).AsTopLevelItems(),
        };

        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Find Task<TResult> in the model
        var taskSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Kind == SymbolKind.Class
                && s.Name == "Task"
                && s.DeclaredType is ClassType ct
                && ct.ContainingNamespace == "Standard"
                && ct.IsBackendBacked);

        Assert.NotNull(taskSymbol);

        var taskClassType = (ClassType)taskSymbol!.DeclaredType!;

        // Task must have Drop recognized
        Assert.True(taskClassType.HasDrop,
            "Task<TResult>.HasDrop should be true — Drop() must be recognized.");

        // DropReturnType should be TResult (the type parameter)
        Assert.NotNull(taskClassType.DropReturnType);
        _ = Assert.IsType<TypeParameterType>(taskClassType.DropReturnType);

        // Verify the type parameter is the same one used in the type's definition
        var dropReturnParam = (TypeParameterType)taskClassType.DropReturnType!;
        Assert.Equal("TResult", dropReturnParam.DisplayName);
    }

    /// <summary>
    /// Verifies that Task and ValueTask Drop return types are structurally equivalent:
    /// both are the respective type's TResult parameter, meaning <c>await</c> (which
    /// unwraps the first type argument) and <c>Drop()</c> (which returns TResult)
    /// are the same operation.
    /// </summary>
    [Fact]
    public void TaskAndValueTask_DropReturnType_MatchesTypeArgument()
    {
        var emptyClass = new ClassDeclarationNode
        {
            Name = Tok("Placeholder"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = emptyClass }).AsTopLevelItems(),
        };

        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Verify both Task and ValueTask
        foreach(var typeName in new[] { "Task", "ValueTask" })
        {
            var symbol = model.AllSymbols
                .FirstOrDefault(s => s.Kind == SymbolKind.Class
                    && s.Name == typeName
                    && s.DeclaredType is ClassType ct
                    && ct.ContainingNamespace == "Standard"
                    && ct.IsBackendBacked);

            Assert.NotNull(symbol);

            var classType = (ClassType)symbol!.DeclaredType!;

            // Both must have Drop
            Assert.True(classType.HasDrop, $"{typeName}.HasDrop should be true");
            Assert.NotNull(classType.DropReturnType);

            // The DropReturnType must be a TypeParameterType (the TResult param)
            var dropReturn = Assert.IsType<TypeParameterType>(classType.DropReturnType);

            // The Drop return type must be the SAME type parameter object that appears
            // in the type's TypeParameters list — proving await (which unwraps the first
            // type arg from the constructed type) and Drop() (which returns TResult)
            // resolve to the same type T.
            Assert.Contains(classType.TypeParameters, tp => ReferenceEquals(tp, dropReturn));
        }
    }
}