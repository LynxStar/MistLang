// Feature: method-overloading, Property 10: Cross-hierarchy overloading is not a redeclaration error
// Feature: method-overloading, Property 11: Cross-hierarchy same-signature is a redeclaration error
// **Validates: Requirements 5.1, 5.2**

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
/// Property-based tests for cross-hierarchy overloading behavior.
/// Verifies that the <see cref="SemanticModelBuilder"/> correctly distinguishes
/// between valid cross-hierarchy overloads (different parameter signatures) and
/// redeclarations (same parameter signatures) across class inheritance.
/// </summary>
public class CrossHierarchyOverloadPropertyTests
{
    // ─── Shared helpers ──────────────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    /// <summary>
    /// Pairs of distinct primitive types used for generating cross-hierarchy overload scenarios.
    /// Each pair (BaseType, DerivedType) has different DisplayNames so signatures differ.
    /// </summary>
    private static readonly (IType BaseType, IType DerivedType)[] DistinctTypePairs =
    [
        (LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Bool),
        (LegacyPrimitiveTypes.Bool, LegacyPrimitiveTypes.Int),
        (LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Double),
        (LegacyPrimitiveTypes.Double, LegacyPrimitiveTypes.Float),
        (LegacyPrimitiveTypes.Char, LegacyPrimitiveTypes.Long),
        (LegacyPrimitiveTypes.Long, LegacyPrimitiveTypes.String),
        (LegacyPrimitiveTypes.String, LegacyPrimitiveTypes.Int),
    ];

    /// <summary>
    /// Primitive types used for generating same-signature redeclaration scenarios.
    /// </summary>
    private static readonly IType[] PrimitiveTypes =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.Double,
        LegacyPrimitiveTypes.Float,
        LegacyPrimitiveTypes.Char,
        LegacyPrimitiveTypes.Long,
        LegacyPrimitiveTypes.String,
    ];

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

    /// <summary>
    /// Builds a semantic model and returns both the model and the diagnostic bag
    /// so tests can inspect emitted diagnostics.
    /// </summary>
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
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Name = MakeToken(paramName),
        };
    }

    /// <summary>
    /// Creates a method declaration with a single parameter of the given type.
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
    /// Creates a class declaration with a base class reference and the given members.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithBase(
        string name,
        string? baseClassName,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if(baseClassName is not null)
        {
            var baseList = MakeBaseList(baseClassName);
            classDecl.BaseClause = baseList;
            classDecl.BaseList = baseList;
        }

        return classDecl;
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

    /// <summary>
    /// Builds a CompilationUnitNode from a list of class declarations.
    /// </summary>
    private static CompilationUnitNode MakeCompilationUnit(params ClassDeclarationNode[] classDecls)
    {
        var types = classDecls.Select(c =>
            (TypeDeclarationNode)new ClassTypeTypeDeclarationNode { ClassDeclaration = c })
            .ToImmutableArray();

        return new CompilationUnitNode { Items = (types).AsTopLevelItems() };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types for FsCheck
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario: A base class has method M with one parameter type, and a derived class
    /// has method M with a different parameter type (valid cross-hierarchy overload).
    /// </summary>
#pragma warning disable CA1034
    public record CrossHierarchyOverloadScenario(IType BaseParamType, IType DerivedParamType)
    {
        public override string ToString()
        {
            return $"CrossHierarchyOverload: Base.M({BaseParamType.DisplayName}) vs Derived.M({DerivedParamType.DisplayName})";
        }
    }

    /// <summary>
    /// Scenario: A base class has method M with one parameter type, and a derived class
    /// has method M with the same parameter type (redeclaration error).
    /// </summary>
    public record CrossHierarchySameSignatureScenario(IType ParamType)
    {
        public override string ToString()
        {
            return $"CrossHierarchySameSignature: Base.M({ParamType.DisplayName}) vs Derived.M({ParamType.DisplayName})";
        }
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a pair of distinct primitive types for cross-hierarchy overload scenarios.
    /// </summary>
    public static Arbitrary<CrossHierarchyOverloadScenario> ArbitraryCrossHierarchyOverloadScenarios()
    {
        var gen =
            from pairIndex in Gen.Choose(0, DistinctTypePairs.Length - 1)
            let pair = DistinctTypePairs[pairIndex]
            select new CrossHierarchyOverloadScenario(pair.BaseType, pair.DerivedType);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a random primitive type for same-signature redeclaration scenarios.
    /// </summary>
    public static Arbitrary<CrossHierarchySameSignatureScenario> ArbitraryCrossHierarchySameSignatureScenarios()
    {
        var gen =
            from typeIndex in Gen.Choose(0, PrimitiveTypes.Length - 1)
            let paramType = PrimitiveTypes[typeIndex]
            select new CrossHierarchySameSignatureScenario(paramType);

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Cross-hierarchy overloading is not a redeclaration error
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 10: For any class hierarchy where a derived class declares a method
    /// with the same name but different parameter signature as a base class method,
    /// no redeclaration diagnostic is emitted.
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CrossHierarchyOverloadPropertyTests) }, MaxTest = 20)]
    public bool CrossHierarchyOverload_NoRedeclarationDiagnostic(CrossHierarchyOverloadScenario scenario)
    {
        // Arrange: base class with M(baseType), derived class with M(derivedType)
        var baseMethod = MakeMethod("M", scenario.BaseParamType);
        var derivedMethod = MakeMethod("M", scenario.DerivedParamType);

        var baseClass = MakeClassWithBase(
            "Base",
            baseClassName: null,
            ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = baseMethod }));

        var derivedClass = MakeClassWithBase(
            "Derived",
            baseClassName: "Base",
            ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = derivedMethod }));

        var ast = MakeCompilationUnit(baseClass, derivedClass);

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: no MIST_MEMBER_REDECLARATION diagnostic is emitted
        var allDiagnostics = diagnostics.ToImmutableArray();
        var redeclDiags = allDiagnostics
            .Where(d => d.Id.Value == "MIST_MEMBER_REDECLARATION")
            .ToList();

        return redeclDiags.Count == 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Cross-hierarchy same-signature is a redeclaration error
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 11: For any class hierarchy where a derived class declares a method
    /// with the same name and identical parameter signature as a base class method,
    /// a redeclaration diagnostic is emitted.
    ///
    /// **Validates: Requirements 5.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CrossHierarchyOverloadPropertyTests) }, MaxTest = 20)]
    public bool CrossHierarchySameSignature_EmitsRedeclarationDiagnostic(CrossHierarchySameSignatureScenario scenario)
    {
        // Arrange: base class with M(paramType), derived class with M(paramType)
        var baseMethod = MakeMethod("M", scenario.ParamType);
        var derivedMethod = MakeMethod("M", scenario.ParamType);

        var baseClass = MakeClassWithBase(
            "Base",
            baseClassName: null,
            ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = baseMethod }));

        var derivedClass = MakeClassWithBase(
            "Derived",
            baseClassName: "Base",
            ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = derivedMethod }));

        var ast = MakeCompilationUnit(baseClass, derivedClass);

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: at least one MIST_MEMBER_REDECLARATION diagnostic is emitted
        // mentioning the derived class and the method name
        var allDiagnostics = diagnostics.ToImmutableArray();
        var redeclDiags = allDiagnostics
            .Where(d => d.Id.Value == "MIST_MEMBER_REDECLARATION"
                        && d.Message.Contains("Derived")
                        && d.Message.Contains("M"))
            .ToList();

        return redeclDiags.Count >= 1;
    }
}