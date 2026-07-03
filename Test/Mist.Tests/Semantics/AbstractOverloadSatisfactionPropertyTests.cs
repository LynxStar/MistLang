// Feature: method-overloading, Property 13: Abstract overload satisfaction
// **Validates: Requirements 6.2, 6.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for abstract overload satisfaction.
/// Verifies that the <see cref="SemanticModelBuilder"/> correctly validates
/// that implementing classes provide all overloaded method signatures
/// declared in an abstract type, emitting MIST_SM_0022 for each missing one.
/// </summary>
public class AbstractOverloadSatisfactionPropertyTests
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
        var primitiveType = PrimitiveTypeNodeFactory.TryGetValue(typeName, out var factory)
            ? factory()
            : new IntTypePrimitiveTypeNode();

        return new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Name = MakeToken(paramName),
        };
    }

    /// <summary>
    /// Creates an abstract method declaration with a single parameter of the given type.
    /// </summary>
    private static MethodDeclarationNode MakeAbstractMethod(string name, IType paramType)
    {
        var param = MakeParameter(paramType.DisplayName, "p0");
        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodNoBodyMethodBodyNode(),
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
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    /// <summary>
    /// Creates an TypeBodyDeclarationNode with the given methods.
    /// </summary>
    private static TypeBodyDeclarationNode MakeAbstractDecl(
        string name,
        ImmutableArray<MethodDeclarationNode> methods)
    {
        var members = methods.Select(m =>
            (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        return new TypeBodyDeclarationNode
        {
            Keyword = new AbstractKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members,
        };
    }

    /// <summary>
    /// Creates a TypeBodyDeclarationNode that implements an abstract type.
    /// </summary>
    private static TypeBodyDeclarationNode MakeClassDecl(
        string name,
        string abstractName,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var baseList = MakeBaseList(abstractName);
        var decl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
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

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method)
    {
        return new MethodMemberMemberDeclarationNode { Method = method };
    }

    private static TypeDeclarationNode WrapAbstract(TypeBodyDeclarationNode decl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = decl };
    }

    private static TypeDeclarationNode WrapClass(TypeBodyDeclarationNode decl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = decl };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types for FsCheck
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pairs of distinct primitive types used for generating overloaded abstract methods.
    /// Each pair represents two different parameter types for the same method name.
    ///
    /// After Task 32's compat shim, <see cref="PrimitiveType"/> accessors
    /// (e.g. <c>LegacyPrimitiveTypes.Int</c>) may return stdlib <c>StructType</c>
    /// instances whose <c>DisplayName</c> is the stdlib simple name
    /// (<c>"Int32"</c>, <c>"Char"</c>, etc.) rather than the keyword
    /// (<c>"int"</c>, <c>"char"</c>). The <see cref="PrimitiveTypeNodeFactory"/>
    /// only maps keyword-form names, so we use the legacy singletons
    /// directly to guarantee stable <c>DisplayName</c> values.
    /// </summary>
    private static readonly (IType First, IType Second)[] DistinctTypePairs =
    [
        (LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Bool),
        (LegacyPrimitiveTypes.Bool, LegacyPrimitiveTypes.Double),
        (LegacyPrimitiveTypes.Double, LegacyPrimitiveTypes.Float),
        (LegacyPrimitiveTypes.Float, LegacyPrimitiveTypes.Char),
        (LegacyPrimitiveTypes.Char, LegacyPrimitiveTypes.Long),
        (LegacyPrimitiveTypes.Long, LegacyPrimitiveTypes.String),
        (LegacyPrimitiveTypes.String, LegacyPrimitiveTypes.Int),
    ];

    /// <summary>
    /// Scenario: An abstract type has overloaded methods M(typeA) and M(typeB),
    /// and the implementing class provides both — no diagnostic expected.
    /// </summary>
#pragma warning disable CA1034
    public record AllSignaturesMatchedScenario(IType FirstParamType, IType SecondParamType)
    {
        public override string ToString()
        {
            return $"AllMatched: IFoo.M({FirstParamType.DisplayName}), IFoo.M({SecondParamType.DisplayName}) — class provides both";
        }
    }

    /// <summary>
    /// Scenario: An abstract type has overloaded methods M(typeA) and M(typeB),
    /// but the implementing class only provides M(typeA) — diagnostic expected for M(typeB).
    /// </summary>
    public record MissingSignatureScenario(IType ProvidedParamType, IType MissingParamType)
    {
        public override string ToString()
        {
            return $"Missing: IFoo.M({ProvidedParamType.DisplayName}) provided, IFoo.M({MissingParamType.DisplayName}) missing";
        }
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a scenario where both overloaded abstract methods are satisfied.
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
    /// Generates a scenario where one overloaded abstract method is missing.
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
    /// Property 13a: For any abstract type with overloaded methods (same name, different
    /// parameter signatures) and an implementing class that provides both signatures,
    /// the semantic model builder SHALL emit no MIST_SM_0022 diagnostic.
    ///
    /// **Validates: Requirements 6.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractOverloadSatisfactionPropertyTests) }, MaxTest = 20)]
    public bool AllSignaturesMatched_NoMissingAbstractOverloadDiagnostic(AllSignaturesMatchedScenario scenario)
    {
        // Arrange: abstract IFoo with M(firstType) and M(secondType)
        var abstractMethod1 = MakeAbstractMethod("M", scenario.FirstParamType);
        var abstractMethod2 = MakeAbstractMethod("M", scenario.SecondParamType);
        var abstractDecl = MakeAbstractDecl("IFoo",
            ImmutableArray.Create(abstractMethod1, abstractMethod2));

        // Implementing class provides both M(firstType) and M(secondType)
        var classMethod1 = MakeMethod("M", scenario.FirstParamType);
        var classMethod2 = MakeMethod("M", scenario.SecondParamType);
        var classDecl = MakeClassDecl("MyClass", "IFoo",
            ImmutableArray.Create(WrapMethod(classMethod1), WrapMethod(classMethod2)));

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));

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
    /// Property 13b: For any abstract type with overloaded methods (same name, different
    /// parameter signatures) and an implementing class that only provides one of the
    /// signatures, the semantic model builder SHALL emit a MIST_SM_0022 diagnostic
    /// for the missing signature.
    ///
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractOverloadSatisfactionPropertyTests) }, MaxTest = 20)]
    public bool MissingSignature_EmitsMissingAbstractOverloadDiagnostic(MissingSignatureScenario scenario)
    {
        // Arrange: abstract IFoo with M(providedType) and M(missingType)
        var abstractMethod1 = MakeAbstractMethod("M", scenario.ProvidedParamType);
        var abstractMethod2 = MakeAbstractMethod("M", scenario.MissingParamType);
        var abstractDecl = MakeAbstractDecl("IFoo",
            ImmutableArray.Create(abstractMethod1, abstractMethod2));

        // Implementing class only provides M(providedType), missing M(missingType)
        var classMethod = MakeMethod("M", scenario.ProvidedParamType);
        var classDecl = MakeClassDecl("MyClass", "IFoo",
            ImmutableArray.Create(WrapMethod(classMethod)));

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));

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