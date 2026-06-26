// Feature: structured-concurrency, Property 2: Async Method Return Type Wrapping
// **Validates: Requirements 2.1, 2.2**

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
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying async method return type wrapping in the semantic model.
///
/// **Validates: Requirements 2.1, 2.2**
///
/// For any method declared with the <c>async</c> modifier and a declared return type <c>T</c>,
/// the semantic model SHALL record the method's effective return type as <c>Task&lt;T&gt;</c>
/// (a <see cref="ConstructedType"/> whose generic definition is <c>Standard.Task</c> and
/// whose type argument is <c>T</c>).
///
/// For <c>void</c> return type, the type argument is <c>Standard.Void</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AsyncMethodReturnTypePropertyTests
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

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
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

    private static MethodDeclarationNode MakeAsyncMethod(
        string name,
        TypeReferenceNode returnType,
        ImmutableArray<ModifierNode>? additionalModifiers = null)
    {
        var modifiers = ImmutableArray.CreateBuilder<ModifierNode>();
        modifiers.Add(new PublicModifierModifierNode());
        if(additionalModifiers is not null)
        {
            foreach(var mod in additionalModifiers.Value)
            {
                modifiers.Add(mod);
            }
        }
        modifiers.Add(new AsyncModifierModifierNode());

        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType,
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = modifiers.ToImmutable(),
        };
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method)
    {
        return new MethodMemberMemberDeclarationNode { Method = method };
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing an async method with a specific return type.
    /// </summary>
    public record AsyncMethodScenario(
        string ClassName,
        string MethodName,
        string ReturnTypeName,
        bool IsStatic,
        bool IsVoid)
    {
        public override string ToString()
        {
            var staticStr = IsStatic ? "static " : "";
            return $"class {ClassName} {{ public {staticStr}async {ReturnTypeName} {MethodName}() {{ }} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Worker", "Service", "Handler", "Processor"];
    private static readonly string[] MethodNames = ["Compute", "Fetch", "Process", "Execute", "Load"];
    private static readonly string[] ReturnTypes = ["int", "string", "bool", "float", "void"];

    /// <summary>
    /// Maps primitive keyword names to their resolved stdlib display names.
    /// When the semantic model resolves a primitive keyword, it produces the
    /// stdlib type (e.g., "int" → "Int32", "string" → "String").
    /// </summary>
    private static readonly Dictionary<string, string> ResolvedDisplayNames = new()
    {
        ["int"] = "Int32",
        ["string"] = "String",
        ["bool"] = "Boolean",
        ["float"] = "Single",
        ["void"] = "Void",
    };

    public static Gen<AsyncMethodScenario> GenAsyncMethodScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               from isStatic in Gen.Elements(true, false)
               select new AsyncMethodScenario(
                   className, methodName, returnType, isStatic, returnType == "void");
    }

    public static Arbitrary<AsyncMethodScenario> ArbitraryAsyncMethodScenario()
    {
        return GenAsyncMethodScenario().ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    private static CompilationUnitNode BuildAst(AsyncMethodScenario scenario)
    {
        TypeReferenceNode returnTypeRef = scenario.IsVoid
            ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
            : MakePrimitiveTypeRef(scenario.ReturnTypeName);

        var additionalModifiers = scenario.IsStatic
            ? ImmutableArray.Create<ModifierNode>(new StaticModifierModifierNode())
            : (ImmutableArray<ModifierNode>?)null;

        var method = MakeAsyncMethod(scenario.MethodName, returnTypeRef, additionalModifiers);
        var member = WrapMethod(method);

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.ClassName),
            Members = ImmutableArray.Create(member),
        };

        return MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Async Method Return Type Wrapping
    //
    // **Validates: Requirements 2.1, 2.2**
    //
    // For any method declared with the `async` modifier and a declared return
    // type T, the semantic model SHALL record the method's effective return type
    // as Task<T> (a ConstructedType whose generic definition is Standard.Task
    // and whose type argument is T).
    //
    // For void return type, the type argument is Standard.Void.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AsyncMethodReturnTypePropertyTests) })]
    public Property AsyncMethod_EffectiveReturnType_IsTaskOfT(AsyncMethodScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, diagnostics) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Find the method symbol
        var methodSymbol = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == scenario.MethodName && s.Kind == SymbolKind.Method);

        if(methodSymbol is null)
        {
            return false.ToProperty()
                .Label($"Method symbol '{scenario.MethodName}' not found in model.");
        }

        // Verify IsAsync is true
        if(!methodSymbol.IsAsync)
        {
            return false.ToProperty()
                .Label($"Method '{scenario.MethodName}' does not have IsAsync = true.");
        }

        // Verify EffectiveReturnType is a ConstructedType
        if(methodSymbol.EffectiveReturnType is not ConstructedType constructedType)
        {
            return false.ToProperty()
                .Label($"EffectiveReturnType is not a ConstructedType. " +
                       $"Actual: {methodSymbol.EffectiveReturnType?.GetType().Name ?? "null"} " +
                       $"({methodSymbol.EffectiveReturnType?.DisplayName ?? "null"})");
        }

        // Verify the generic definition is Standard.Task (a ClassType named "Task" in namespace "Standard")
        if(constructedType.GenericDefinition is not ClassType taskClass)
        {
            return false.ToProperty()
                .Label($"GenericDefinition is not a ClassType. " +
                       $"Actual: {constructedType.GenericDefinition.GetType().Name}");
        }

        if(taskClass.DisplayName != "Task")
        {
            return false.ToProperty()
                .Label($"GenericDefinition DisplayName is not 'Task'. Actual: '{taskClass.DisplayName}'");
        }

        if(taskClass.ContainingNamespace != "Standard")
        {
            return false.ToProperty()
                .Label($"GenericDefinition namespace is not 'Standard'. Actual: '{taskClass.ContainingNamespace}'");
        }

        // Verify there is exactly one type argument
        if(constructedType.TypeArguments.Count != 1)
        {
            return false.ToProperty()
                .Label($"Expected 1 type argument, got {constructedType.TypeArguments.Count}.");
        }

        // Verify the type argument matches the declared return type
        var typeArg = constructedType.TypeArguments[0];
        var expectedDisplayName = ResolvedDisplayNames[scenario.ReturnTypeName];

        // The resolved type uses the stdlib display name (e.g., "int" → "Int32")
        if(typeArg.DisplayName != expectedDisplayName)
        {
            return false.ToProperty()
                .Label($"Type argument mismatch. Expected '{expectedDisplayName}' " +
                       $"(from keyword '{scenario.ReturnTypeName}'), got '{typeArg.DisplayName}'.");
        }

        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AsyncMethodReturnTypePropertyTests) })]
    public Property AsyncMethod_DeclaredReturnType_MatchesWrittenType(AsyncMethodScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Find the method symbol
        var methodSymbol = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == scenario.MethodName && s.Kind == SymbolKind.Method);

        if(methodSymbol is null)
        {
            return false.ToProperty()
                .Label($"Method symbol '{scenario.MethodName}' not found in model.");
        }

        // DeclaredReturnType should be the type the developer wrote (T, not Task<T>)
        if(methodSymbol.DeclaredReturnType is null)
        {
            return false.ToProperty()
                .Label($"DeclaredReturnType is null for method '{scenario.MethodName}'.");
        }

        var expectedDisplayName = ResolvedDisplayNames[scenario.ReturnTypeName];

        // The resolved type uses the stdlib display name (e.g., "int" → "Int32")
        if(methodSymbol.DeclaredReturnType.DisplayName != expectedDisplayName)
        {
            return false.ToProperty()
                .Label($"DeclaredReturnType mismatch. Expected '{expectedDisplayName}' " +
                       $"(from keyword '{scenario.ReturnTypeName}'), " +
                       $"got '{methodSymbol.DeclaredReturnType.DisplayName}'.");
        }

        return true.ToProperty();
    }
}