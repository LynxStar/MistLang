// Feature: structured-concurrency, Property 8: Async-Intrinsic Incompatibility
// **Validates: Requirements 2.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that MIST_TC_0104 is emitted when a method
/// combines both the <c>async</c> and <c>intrinsic</c> modifiers.
///
/// **Validates: Requirements 2.4**
///
/// For any method declaration that combines both the <c>async</c> and <c>intrinsic</c>
/// modifiers, the type checker SHALL emit diagnostic <c>MIST_TC_0104</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AsyncIntrinsicDiagnosticPropertyTests
{
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
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

    private static ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        Analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Describes the ordering of async and intrinsic modifiers on a method.
    /// </summary>
    public enum ModifierOrder
    {
        /// <summary><c>async intrinsic</c></summary>
        AsyncFirst,

        /// <summary><c>intrinsic async</c></summary>
        IntrinsicFirst,
    }

    /// <summary>
    /// A scenario describing a method with both async and intrinsic modifiers.
    /// </summary>
    public record AsyncIntrinsicScenario(
        string ClassName,
        string MethodName,
        string ReturnTypeName,
        bool IsStatic,
        ModifierOrder Order)
    {
        public override string ToString()
        {
            var staticStr = IsStatic ? "static " : "";
            var (first, second) = Order == ModifierOrder.AsyncFirst
                ? ("async", "intrinsic")
                : ("intrinsic", "async");
            var retType = ReturnTypeName == "void" ? "void" : ReturnTypeName;
            return $"class {ClassName} {{ public {staticStr}{first} {second} {retType} {MethodName}(); }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Worker", "Service", "Handler", "Processor", "Engine"];
    private static readonly string[] MethodNames = ["Compute", "Fetch", "Process", "Execute", "Load", "DoWork"];
    private static readonly string[] ReturnTypes = ["int", "string", "bool", "float", "void"];

    public static Gen<AsyncIntrinsicScenario> GenAsyncIntrinsicScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               from isStatic in Gen.Elements(true, false)
               from order in Gen.Elements(ModifierOrder.AsyncFirst, ModifierOrder.IntrinsicFirst)
               select new AsyncIntrinsicScenario(className, methodName, returnType, isStatic, order);
    }

    public static Arbitrary<AsyncIntrinsicScenario> ArbitraryAsyncIntrinsicScenario()
    {
        return GenAsyncIntrinsicScenario().ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    private static CompilationUnitNode BuildAst(AsyncIntrinsicScenario scenario)
    {
        TypeReferenceNode returnTypeRef = scenario.ReturnTypeName == "void"
            ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
            : MakePrimitiveTypeRef(scenario.ReturnTypeName);

        // Build modifiers in the specified order
        var modifiers = ImmutableArray.CreateBuilder<ModifierNode>();
        modifiers.Add(new PublicModifierModifierNode());

        if(scenario.IsStatic)
        {
            modifiers.Add(new StaticModifierModifierNode());
        }

        if(scenario.Order == ModifierOrder.AsyncFirst)
        {
            modifiers.Add(new AsyncModifierModifierNode());
            modifiers.Add(new IntrinsicModifierModifierNode());
        }
        else
        {
            modifiers.Add(new IntrinsicModifierModifierNode());
            modifiers.Add(new AsyncModifierModifierNode());
        }

        // Intrinsic methods have no body (semicolon-terminated)
        var method = new MethodDeclarationNode
        {
            Name = Tok(scenario.MethodName),
            ReturnType = returnTypeRef,
            Parameters = null,
            Body = null!, // Intrinsic methods are semicolon-terminated with no body
            Modifiers = modifiers.ToImmutable(),
        };

        // The class containing the method must be intrinsic for intrinsic methods
        var classModifiers = ImmutableArray.Create<ModifierNode>(new IntrinsicModifierModifierNode());

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.ClassName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
            Modifiers = classModifiers,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Async-Intrinsic Incompatibility
    //
    // **Validates: Requirements 2.4**
    //
    // For any method declaration that combines both the `async` and `intrinsic`
    // modifiers, the type checker SHALL emit diagnostic MIST_TC_0104.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AsyncIntrinsicDiagnosticPropertyTests) })]
    public Property AsyncAndIntrinsic_EmitsDiagnostic(AsyncIntrinsicScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, buildDiagnostics) = StdlibTestHelper.BuildModelWithStdlib(ast);
        var tcDiagnostics = RunTypeChecker(model);

        // Combine all diagnostics to check for MIST_TC_0104
        var allDiagnostics = buildDiagnostics.AddRange(tcDiagnostics);

        var matchingDiagnostics = allDiagnostics
            .Where(d => d.Id.Value == "MIST_TC_0104")
            .ToList();

        if(matchingDiagnostics.Count == 0)
        {
            return false.ToProperty()
                .Label($"Expected MIST_TC_0104 diagnostic but none was emitted. " +
                       $"Scenario: {scenario}. " +
                       $"All diagnostics: [{string.Join(", ", allDiagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        // Verify the diagnostic message contains the method name
        var diag = matchingDiagnostics[0];
        if(!diag.Message.Contains(scenario.MethodName))
        {
            return false.ToProperty()
                .Label($"MIST_TC_0104 message does not contain method name '{scenario.MethodName}'. " +
                       $"Message: '{diag.Message}'. Scenario: {scenario}");
        }

        // Verify the diagnostic message mentions both modifiers
        if(!diag.Message.Contains("async") || !diag.Message.Contains("intrinsic"))
        {
            return false.ToProperty()
                .Label($"MIST_TC_0104 message does not mention both 'async' and 'intrinsic'. " +
                       $"Message: '{diag.Message}'. Scenario: {scenario}");
        }

        return true.ToProperty();
    }
}