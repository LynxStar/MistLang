// Feature: method-overloading, Property 1: Overload registration preserves all distinct signatures
// **Validates: Requirements 1.1, 2.1, 6.1**

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
/// Property-based tests for overload registration in the semantic model.
/// Verifies that the <see cref="SemanticModelBuilder"/> correctly registers
/// all distinct method and constructor overloads as separate symbols.
/// </summary>
public class OverloadRegistrationPropertyTests
{
    // ─── Shared helpers ──────────────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    /// <summary>
    /// Primitive types used for generating distinct parameter signatures.
    /// Each type produces a unique DisplayName, ensuring pairwise-distinct signatures.
    /// </summary>
    private static readonly IType[] DistinctPrimitiveTypes =
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

    private static ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return Builder.Build(compilation, ImmutableArray.Create(ast), null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
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
    /// Scenario: N methods sharing a name with pairwise-distinct single-parameter types.
    /// </summary>
#pragma warning disable CA1034
    public record OverloadedMethodScenario(
        IReadOnlyList<IType> ParamTypes)
    {
        public override string ToString()
        {
            return $"Methods: {ParamTypes.Count} overloads [{string.Join(", ", ParamTypes.Select(t => t.DisplayName))}]";
        }
    }

    /// <summary>
    /// Scenario: N constructors with pairwise-distinct single-parameter types.
    /// </summary>
    public record OverloadedConstructorScenario(
        IReadOnlyList<IType> ParamTypes)
    {
        public override string ToString()
        {
            return $"Constructors: {ParamTypes.Count} overloads [{string.Join(", ", ParamTypes.Select(t => t.DisplayName))}]";
        }
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates N (2–5) distinct primitive types for method overloads.
    /// </summary>
    public static Arbitrary<OverloadedMethodScenario> ArbitraryOverloadedMethodScenarios()
    {
        var gen =
            from overloadCount in Gen.Choose(2, 5)
            from typeIndices in Gen.ArrayOf(Gen.Choose(0, DistinctPrimitiveTypes.Length - 1), overloadCount)
                .Where(indices => indices.Distinct().Count() == indices.Length)
            let paramTypes = typeIndices.Select(i => DistinctPrimitiveTypes[i]).ToList()
            select new OverloadedMethodScenario(paramTypes);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates N (2–5) distinct primitive types for constructor overloads.
    /// </summary>
    public static Arbitrary<OverloadedConstructorScenario> ArbitraryOverloadedConstructorScenarios()
    {
        var gen =
            from overloadCount in Gen.Choose(2, 5)
            from typeIndices in Gen.ArrayOf(Gen.Choose(0, DistinctPrimitiveTypes.Length - 1), overloadCount)
                .Where(indices => indices.Distinct().Count() == indices.Length)
            let paramTypes = typeIndices.Select(i => DistinctPrimitiveTypes[i]).ToList()
            select new OverloadedConstructorScenario(paramTypes);

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Overload registration preserves all distinct signatures
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 1 (methods): For any type with N method declarations sharing a name
    /// but with pairwise-distinct parameter signatures, the semantic model registers
    /// exactly N symbols with that name.
    ///
    /// **Validates: Requirements 1.1, 2.1, 6.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OverloadRegistrationPropertyTests) }, MaxTest = 20)]
    public bool OverloadedMethods_RegisterExactlyNSymbols(OverloadedMethodScenario scenario)
    {
        // Arrange: build N methods named "M" with distinct single-parameter types
        var methods = scenario.ParamTypes
            .Select(t => MakeMethod("M", t))
            .ToList();

        var members = methods
            .Select(m => (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m })
            .ToImmutableArray();

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

        var model = BuildModel(ast);

        // Act: collect all method symbols named "M"
        var methodSymbols = model.AllSymbols
            .Where(s => s.Name == "M" && s.Kind == SymbolKind.Method)
            .ToList();

        // Assert: exactly N method symbols registered
        if(methodSymbols.Count != scenario.ParamTypes.Count)
        {
            return false;
        }

        // Assert: each registered symbol has a distinct parameter signature
        var signatures = methodSymbols
            .Select(s => ParameterSignature.Extract(s, model))
            .ToList();

        for(var i = 0; i < signatures.Count; i++)
        {
            for(var j = i + 1; j < signatures.Count; j++)
            {
                if(ParameterSignature.AreEqual(signatures[i], signatures[j]))
                {
                    return false; // Found duplicate signatures — should not happen
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Property 1 (constructors): For any type with N constructor declarations
    /// with pairwise-distinct parameter signatures, the semantic model registers
    /// exactly N constructor symbols.
    ///
    /// **Validates: Requirements 1.1, 2.1, 6.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OverloadRegistrationPropertyTests) }, MaxTest = 20)]
    public bool OverloadedConstructors_RegisterExactlyNSymbols(OverloadedConstructorScenario scenario)
    {
        // Arrange: build N constructors for class "C" with distinct single-parameter types
        var constructors = scenario.ParamTypes
            .Select(t => MakeConstructor("C", t))
            .ToList();

        var members = constructors
            .Select(c => (MemberDeclarationNode)new ConstructorMemberMemberDeclarationNode { Constructor = c })
            .ToImmutableArray();

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

        var model = BuildModel(ast);

        // Act: collect all constructor symbols named "C"
        var ctorSymbols = model.AllSymbols
            .Where(s => s.Name == "C" && s.Kind == SymbolKind.Constructor)
            .ToList();

        // Assert: exactly N constructor symbols registered
        if(ctorSymbols.Count != scenario.ParamTypes.Count)
        {
            return false;
        }

        // Assert: each registered constructor has a distinct parameter signature
        var signatures = ctorSymbols
            .Select(s => ParameterSignature.Extract(s, model))
            .ToList();

        for(var i = 0; i < signatures.Count; i++)
        {
            for(var j = i + 1; j < signatures.Count; j++)
            {
                if(ParameterSignature.AreEqual(signatures[i], signatures[j]))
                {
                    return false; // Found duplicate signatures — should not happen
                }
            }
        }

        return true;
    }
}