// Feature: method-overloading, Property 13: Interface overload satisfaction
// **Validates: Requirements 6.2, 6.3**

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

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for interface overload satisfaction.
/// Verifies that the <see cref="SemanticModelBuilder"/> correctly validates
/// that implementing classes provide all overloaded method signatures
/// declared in an interface, emitting MIST_SM_0022 for each missing one.
/// </summary>
public class InterfaceOverloadSatisfactionPropertyTests
{
    // ─── Shared helpers ──────────────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    /// <summary>
    /// Maps a type name string to the corresponding AST primitive type node.
    /// </summary>
    private static readonly Dictionary<string, Func<PrimitiveTypeNode>> PrimitiveTypeNodeFactory = new()
    {
        ["int"] = () => new IntTypePrimitiveTypeNode(),
        ["bool"] = () => new BoolTypePrimitiveTypeNode(),
        ["string"] = () => new StringTypePrimitiveTypeNode(),
        ["double"] = () => new DoubleTypePrimitiveTypeNode(),
        ["float"] = () => new FloatTypePrimitiveTypeNode(),
        ["char"] = () => new CharTypePrimitiveTypeNode(),
        ["long"] = () => new LongTypePrimitiveTypeNode(),
    };

    private static Storm.Runtime.Token MakeToken(string lexeme)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation()
    {
        var sourceText = new SourceText("test.mist", "");
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private static (ISemanticModel Model, DiagnosticBag Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        var model = Builder.Build(compilation, ImmutableArray.Create(ast), null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics);
    }

    private static ParameterNode MakeParameter(string typeName, string paramName)
    {
        PrimitiveTypeNode primitiveType = PrimitiveTypeNodeFactory.TryGetValue(typeName, out var factory)
            ? factory()
            : new IntTypePrimitiveTypeNode();

        return new ParameterNode
        {
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveType },
            Name = MakeToken(paramName),
        };
    }

    /// <summary>
    /// Creates an interface method declaration with a single parameter of the given type.
    /// </summary>
    private static InterfaceMethodDeclarationNode MakeInterfaceMethod(string name, IType paramType)
    {
        var param = MakeParameter(paramType.DisplayName, "p0");
        return new InterfaceMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = new ParameterListNode { Parameter = param },
        };
    }

    /// <summary>
    /// Creates a class method declaration with a single parameter of the given type.
    /// </summary>
    private static MethodDeclarationNode MakeMethod(string name, IType paramType)
    {
        var param = MakeParameter(paramType.DisplayName, "p0");
        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    /// <summary>
    /// Creates an InterfaceDeclarationNode with the given methods.
    /// </summary>
    private static InterfaceDeclarationNode MakeInterfaceDecl(
        string name,
        ImmutableArray<InterfaceMethodDeclarationNode> methods)
    {
        var members = methods.Select(m =>
            (InterfaceMemberDeclarationNode)new InterfaceMethodMemberInterfaceMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        return new InterfaceDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
    }

    /// <summary>
    /// Creates a ClassDeclarationNode that implements an interface.
    /// </summary>
    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string interfaceName,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var baseList = MakeBaseList(interfaceName);
        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
            BaseClause = baseList,
            BaseList = baseList,
        };
        return decl;
    }

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method) =>
        new MethodMemberMemberDeclarationNode { Method = method };

    private static TypeDeclarationNode WrapInterface(InterfaceDeclarationNode decl) =>
        new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types for FsCheck
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pairs of distinct primitive types used for generating overloaded interface methods.
    /// Each pair represents two different parameter types for the same method name.
    /// </summary>
    private static readonly (IType First, IType Second)[] DistinctTypePairs =
    [
        (PrimitiveType.Int, PrimitiveType.Bool),
        (PrimitiveType.Bool, PrimitiveType.Double),
        (PrimitiveType.Double, PrimitiveType.Float),
        (PrimitiveType.Float, PrimitiveType.Char),
        (PrimitiveType.Char, PrimitiveType.Long),
        (PrimitiveType.Long, StringType.Instance),
        (StringType.Instance, PrimitiveType.Int),
    ];

    /// <summary>
    /// Scenario: An interface has overloaded methods M(typeA) and M(typeB),
    /// and the implementing class provides both — no diagnostic expected.
    /// </summary>
#pragma warning disable CA1034
    public record AllSignaturesMatchedScenario(IType FirstParamType, IType SecondParamType)
    {
        public override string ToString() =>
            $"AllMatched: IFoo.M({FirstParamType.DisplayName}), IFoo.M({SecondParamType.DisplayName}) — class provides both";
    }

    /// <summary>
    /// Scenario: An interface has overloaded methods M(typeA) and M(typeB),
    /// but the implementing class only provides M(typeA) — diagnostic expected for M(typeB).
    /// </summary>
    public record MissingSignatureScenario(IType ProvidedParamType, IType MissingParamType)
    {
        public override string ToString() =>
            $"Missing: IFoo.M({ProvidedParamType.DisplayName}) provided, IFoo.M({MissingParamType.DisplayName}) missing";
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a scenario where both overloaded interface methods are satisfied.
    /// </summary>
    public static Arbitrary<AllSignaturesMatchedScenario> ArbitraryAllSignaturesMatchedScenarios()
    {
        var gen =
            from pairIndex in Gen.Choose(0, DistinctTypePairs.Length - 1)
            let pair = DistinctTypePairs[pairIndex]
            select new AllSignaturesMatchedScenario(pair.First, pair.Second);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a scenario where one overloaded interface method is missing.
    /// </summary>
    public static Arbitrary<MissingSignatureScenario> ArbitraryMissingSignatureScenarios()
    {
        var gen =
            from pairIndex in Gen.Choose(0, DistinctTypePairs.Length - 1)
            let pair = DistinctTypePairs[pairIndex]
            select new MissingSignatureScenario(pair.First, pair.Second);

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13a: All signatures matched — no diagnostic
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 13a: For any interface with overloaded methods (same name, different
    /// parameter signatures) and an implementing class that provides both signatures,
    /// the semantic model builder SHALL emit no MIST_SM_0022 diagnostic.
    ///
    /// **Validates: Requirements 6.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceOverloadSatisfactionPropertyTests) }, MaxTest = 20)]
    public bool AllSignaturesMatched_NoMissingInterfaceOverloadDiagnostic(AllSignaturesMatchedScenario scenario)
    {
        // Arrange: interface IFoo with M(firstType) and M(secondType)
        var interfaceMethod1 = MakeInterfaceMethod("M", scenario.FirstParamType);
        var interfaceMethod2 = MakeInterfaceMethod("M", scenario.SecondParamType);
        var interfaceDecl = MakeInterfaceDecl("IFoo",
            ImmutableArray.Create(interfaceMethod1, interfaceMethod2));

        // Implementing class provides both M(firstType) and M(secondType)
        var classMethod1 = MakeMethod("M", scenario.FirstParamType);
        var classMethod2 = MakeMethod("M", scenario.SecondParamType);
        var classDecl = MakeClassDecl("MyClass", "IFoo",
            ImmutableArray.Create(WrapMethod(classMethod1), WrapMethod(classMethod2)));

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), WrapClass(classDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: no MIST_SM_0022 diagnostic
        var allDiagnostics = diagnostics.ToImmutableArray();
        var missingOverloadDiags = allDiagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();

        return missingOverloadDiags.Count == 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13b: Missing signature — diagnostic emitted
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 13b: For any interface with overloaded methods (same name, different
    /// parameter signatures) and an implementing class that only provides one of the
    /// signatures, the semantic model builder SHALL emit a MIST_SM_0022 diagnostic
    /// for the missing signature.
    ///
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceOverloadSatisfactionPropertyTests) }, MaxTest = 20)]
    public bool MissingSignature_EmitsMissingInterfaceOverloadDiagnostic(MissingSignatureScenario scenario)
    {
        // Arrange: interface IFoo with M(providedType) and M(missingType)
        var interfaceMethod1 = MakeInterfaceMethod("M", scenario.ProvidedParamType);
        var interfaceMethod2 = MakeInterfaceMethod("M", scenario.MissingParamType);
        var interfaceDecl = MakeInterfaceDecl("IFoo",
            ImmutableArray.Create(interfaceMethod1, interfaceMethod2));

        // Implementing class only provides M(providedType), missing M(missingType)
        var classMethod = MakeMethod("M", scenario.ProvidedParamType);
        var classDecl = MakeClassDecl("MyClass", "IFoo",
            ImmutableArray.Create(WrapMethod(classMethod)));

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), WrapClass(classDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: at least one MIST_SM_0022 diagnostic referencing the missing signature
        var allDiagnostics = diagnostics.ToImmutableArray();
        var missingOverloadDiags = allDiagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022"
                        && d.Message.Contains("M")
                        && d.Message.Contains("MyClass"))
            .ToList();

        return missingOverloadDiags.Count >= 1;
    }
}
