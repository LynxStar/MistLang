// Feature: method-overloading, Property 2: Duplicate signature detection
// **Validates: Requirements 1.2, 1.4, 2.2**

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
/// Property-based tests for duplicate signature detection in the semantic model.
/// Verifies that the <see cref="SemanticModelBuilder"/> correctly emits
/// <c>MIST_SM_0020</c> for duplicate method signatures and
/// <c>MIST_SM_0021</c> for duplicate constructor signatures.
/// </summary>
public class DuplicateSignaturePropertyTests
{
    // ─── Shared helpers ──────────────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    /// <summary>
    /// Primitive types used for generating parameter signatures.
    /// </summary>
    private static readonly IType[] PrimitiveTypes =
    [
        PrimitiveType.Int,
        PrimitiveType.Bool,
        PrimitiveType.Double,
        PrimitiveType.Float,
        PrimitiveType.Char,
        PrimitiveType.Long,
        StringType.Instance,
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
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveType },
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
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    /// <summary>
    /// Creates a constructor declaration with a single parameter of the given type.
    /// </summary>
    private static ConstructorDeclarationNode MakeConstructor(string className, IType paramType)
    {
        var param = MakeParameter(paramType.DisplayName, "p0");
        return new ConstructorDeclarationNode
        {
            Name = MakeToken(className),
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types for FsCheck
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario: Two methods with the same name and the same parameter type (duplicate signature).
    /// </summary>
#pragma warning disable CA1034
    public record DuplicateMethodScenario(IType ParamType)
    {
        public override string ToString() =>
            $"DuplicateMethods: same name, same param type [{ParamType.DisplayName}]";
    }

    /// <summary>
    /// Scenario: Two constructors with the same parameter type (duplicate signature).
    /// </summary>
    public record DuplicateConstructorScenario(IType ParamType)
    {
        public override string ToString() =>
            $"DuplicateConstructors: same param type [{ParamType.DisplayName}]";
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random primitive type for duplicate method scenarios.
    /// </summary>
    public static Arbitrary<DuplicateMethodScenario> ArbitraryDuplicateMethodScenarios()
    {
        var gen =
            from typeIndex in Gen.Choose(0, PrimitiveTypes.Length - 1)
            let paramType = PrimitiveTypes[typeIndex]
            select new DuplicateMethodScenario(paramType);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a random primitive type for duplicate constructor scenarios.
    /// </summary>
    public static Arbitrary<DuplicateConstructorScenario> ArbitraryDuplicateConstructorScenarios()
    {
        var gen =
            from typeIndex in Gen.Choose(0, PrimitiveTypes.Length - 1)
            let paramType = PrimitiveTypes[typeIndex]
            select new DuplicateConstructorScenario(paramType);

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Duplicate signature detection
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 2 (methods): For any type containing two methods with the same name
    /// and identical parameter signatures, the builder emits a MIST_SM_0020 diagnostic.
    ///
    /// **Validates: Requirements 1.2, 1.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DuplicateSignaturePropertyTests) }, MaxTest = 20)]
    public bool DuplicateMethods_EmitDiagnostic(DuplicateMethodScenario scenario)
    {
        // Arrange: build two methods named "M" with the same parameter type
        var method1 = MakeMethod("M", scenario.ParamType);
        var method2 = MakeMethod("M", scenario.ParamType);

        var members = ImmutableArray.Create<MemberDeclarationNode>(
            new MethodMemberMemberDeclarationNode { Method = method1 },
            new MethodMemberMemberDeclarationNode { Method = method2 });

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = members,
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: at least one MIST_SM_0020 diagnostic was emitted
        var allDiagnostics = diagnostics.ToImmutableArray();
        var duplicateMethodDiags = allDiagnostics
            .Where(d => d.Id == SemanticModelBuilder.DuplicateMethodSignature)
            .ToList();

        return duplicateMethodDiags.Count >= 1;
    }

    /// <summary>
    /// Property 2 (constructors): For any type containing two constructors with
    /// identical parameter signatures, the builder emits a MIST_SM_0021 diagnostic.
    ///
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DuplicateSignaturePropertyTests) }, MaxTest = 20)]
    public bool DuplicateConstructors_EmitDiagnostic(DuplicateConstructorScenario scenario)
    {
        // Arrange: build two constructors for class "C" with the same parameter type
        var ctor1 = MakeConstructor("C", scenario.ParamType);
        var ctor2 = MakeConstructor("C", scenario.ParamType);

        var members = ImmutableArray.Create<MemberDeclarationNode>(
            new ConstructorMemberMemberDeclarationNode { Constructor = ctor1 },
            new ConstructorMemberMemberDeclarationNode { Constructor = ctor2 });

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = members,
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: at least one MIST_SM_0021 diagnostic was emitted
        var allDiagnostics = diagnostics.ToImmutableArray();
        var duplicateCtorDiags = allDiagnostics
            .Where(d => d.Id == SemanticModelBuilder.DuplicateConstructorSignature)
            .ToList();

        return duplicateCtorDiags.Count >= 1;
    }
}
