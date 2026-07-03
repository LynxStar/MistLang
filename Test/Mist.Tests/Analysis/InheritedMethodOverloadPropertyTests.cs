// Feature: method-overloading, Property 12: Inherited methods included in overload resolution
// **Validates: Requirements 3.6, 5.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for inherited method overload resolution.
/// Verifies that when a method call is made on a derived type and the matching
/// overload is declared in a base class (not the derived class), the
/// <see cref="OverloadResolver"/> still finds and selects the inherited candidate.
/// </summary>
public class InheritedMethodOverloadPropertyTests
{
    // ─── Shared helpers ──────────────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    /// <summary>
    /// Pairs of distinct primitive types used for generating inherited overload scenarios.
    /// Each pair (BaseParamType, DerivedParamType) has different DisplayNames so signatures differ.
    /// The argument type will match the base class's parameter type, forcing the resolver
    /// to select the inherited (base) method.
    /// </summary>
    private static readonly (IType BaseParamType, IType DerivedParamType)[] DistinctTypePairs =
    [
        (LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Bool),
        (LegacyPrimitiveTypes.Bool, LegacyPrimitiveTypes.Int),
        (LegacyPrimitiveTypes.Double, LegacyPrimitiveTypes.Bool),
        (LegacyPrimitiveTypes.Char, LegacyPrimitiveTypes.Long),
        (LegacyPrimitiveTypes.Long, LegacyPrimitiveTypes.Bool),
        (LegacyPrimitiveTypes.String, LegacyPrimitiveTypes.Int),
        (LegacyPrimitiveTypes.Float, LegacyPrimitiveTypes.Bool),
    ];

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

    private static ParameterNode MakeParameter(string typeName, string paramName)
    {
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };

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
    /// Creates a class declaration with an optional base class reference and the given members.
    /// </summary>
    private static TypeBodyDeclarationNode MakeClassWithBase(
        string name,
        string? baseClassName,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
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
    private static CompilationUnitNode MakeCompilationUnit(params TypeBodyDeclarationNode[] classDecls)
    {
        var types = classDecls.Select(c =>
            (TypeDeclarationNode)new BodyTypeTypeDeclarationNode { TypeBody = c })
            .ToImmutableArray();

        return new CompilationUnitNode { Items = types.AsTopLevelItems() };
    }

    /// <summary>
    /// Builds a semantic model and returns it along with the diagnostic bag.
    /// </summary>
    private static (ISemanticModel Model, DiagnosticBag Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        var model = Builder.Build(compilation, ImmutableArray.Create(ast), null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics);
    }

    /// <summary>
    /// Collects all method symbols with the given name from a type and its base types,
    /// simulating the TypeChecker's CollectMethodOverloads logic.
    /// </summary>
    private static List<ISymbol> CollectMethodOverloads(IType? targetType, string methodName)
    {
        var results = new List<ISymbol>();
        if(targetType is null)
        {
            return results;
        }

        // Collect from the target type's own member scope.
        if(targetType is ClassType ct)
        {
            foreach(var member in ct.MemberScope.Members)
            {
                if(member.Kind == SymbolKind.Method && member.Name == methodName)
                {
                    results.Add(member);
                }
            }

            // Walk the base type chain to collect inherited methods.
            var baseType = ct.BaseType as ClassType;
            while(baseType is not null)
            {
                foreach(var member in baseType.MemberScope.Members)
                {
                    if(member.Kind == SymbolKind.Method && member.Name == methodName)
                    {
                        results.Add(member);
                    }
                }

                baseType = baseType.BaseType as ClassType;
            }
        }

        return results;
    }

    /// <summary>
    /// Finds the ClassType for a given class name from the semantic model.
    /// </summary>
    private static ClassType? FindClassType(ISemanticModel model, string className)
    {
        return model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Class && s.Name == className)
            .Select(s => s.DeclaredType as ClassType)
            .FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario type for FsCheck
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario: A base class has method M with one parameter type, and a derived class
    /// has method M with a different parameter type. The argument type matches the base
    /// class's parameter type, so the resolver must select the inherited base method.
    /// </summary>
#pragma warning disable CA1034
    public record InheritedOverloadScenario(IType BaseParamType, IType DerivedParamType)
    {
        public override string ToString()
        {
            return $"InheritedOverload: Base.M({BaseParamType.DisplayName}), " +
            $"Derived.M({DerivedParamType.DisplayName}), arg={BaseParamType.DisplayName}";
        }
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates pairs of distinct primitive types for inherited overload scenarios.
    /// </summary>
    public static Arbitrary<InheritedOverloadScenario> ArbitraryInheritedOverloadScenarios()
    {
        var gen =
            from pairIndex in Gen.Choose(0, DistinctTypePairs.Length - 1)
            let pair = DistinctTypePairs[pairIndex]
            select new InheritedOverloadScenario(pair.BaseParamType, pair.DerivedParamType);

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Inherited methods included in overload resolution
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 12: For any method call on a derived type where the matching overload
    /// is declared in a base class, the overload resolver finds and selects that
    /// inherited candidate.
    ///
    /// Setup:
    /// - Base class has M(baseParamType)
    /// - Derived class has M(derivedParamType) where derivedParamType != baseParamType
    /// - Argument type is baseParamType
    /// - The resolver must select the base class's M(baseParamType)
    ///
    /// **Validates: Requirements 3.6, 5.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InheritedMethodOverloadPropertyTests) }, MaxTest = 20)]
    public bool InheritedMethod_SelectedByOverloadResolver(InheritedOverloadScenario scenario)
    {
        // Arrange: base class with M(baseParamType), derived class with M(derivedParamType)
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

        // Act: build the semantic model
        var (model, _) = BuildModelWithDiagnostics(ast);

        // Find the Derived class type
        var derivedType = FindClassType(model, "Derived");
        if(derivedType is null)
        {
            return false;
        }

        // Collect the full overload set from Derived (including inherited from Base)
        var candidates = CollectMethodOverloads(derivedType, "M");

        // We expect 2 candidates: one from Derived, one from Base
        if(candidates.Count != 2)
        {
            return false;
        }

        // Resolve with argument type matching the base class's parameter type
        var argTypes = new IType[] { scenario.BaseParamType };
        var result = OverloadResolver.Resolve(candidates, argTypes, model);

        // Assert: resolution succeeds
        if(result.Status != OverloadResolutionStatus.Success)
        {
            return false;
        }

        if(result.BestCandidate is null)
        {
            return false;
        }

        // Assert: the selected candidate has the base class's parameter type
        var selectedSig = ParameterSignature.Extract(result.BestCandidate, model);
        return selectedSig.Count == 1
               && selectedSig[0].DisplayName == scenario.BaseParamType.DisplayName;
    }
}