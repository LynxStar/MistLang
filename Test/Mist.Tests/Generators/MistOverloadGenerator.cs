// Generators for Mist overloading AST nodes.
// Produces random but structurally valid overloaded method, constructor,
// call-site, and class-hierarchy nodes for property-based testing.
// Feature: method-overloading
// **Validates: Requirements 1.1, 2.1, 3.1, 4.1, 5.1**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generators for overloading-specific AST nodes.
/// Provides reusable generators for overloaded methods, constructors,
/// duplicate-signature methods, call sites, ambiguous call sites,
/// and class hierarchies with overloads.
/// </summary>
public static class MistOverloadGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ═══════════════════════════════════════════════════════════════════════
    // Primitive type pools
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// All distinct primitive types available for generating parameter signatures.
    /// Each type has a unique DisplayName, ensuring pairwise-distinct signatures.
    /// </summary>
    private static readonly IType[] DistinctPrimitiveTypes =
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
    /// Maps an <see cref="IType"/> DisplayName to the corresponding AST primitive type node factory.
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
        ["short"] = () => new ShortTypePrimitiveTypeNode(),
        ["decimal"] = () => new DecimalTypePrimitiveTypeNode(),
    };

    /// <summary>
    /// Ambiguous pairs: both param types accept the argument type via widening,
    /// but neither param type is assignable to the other.
    /// </summary>
    private static readonly (IType Param1, IType Param2, IType Arg)[] AmbiguousTuples =
    [
        (PrimitiveType.Float, PrimitiveType.Decimal, PrimitiveType.Int),
        (PrimitiveType.Double, PrimitiveType.Decimal, PrimitiveType.Int),
        (PrimitiveType.Float, PrimitiveType.Decimal, PrimitiveType.Short),
        (PrimitiveType.Double, PrimitiveType.Decimal, PrimitiveType.Short),
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // AST construction helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a <see cref="ParameterNode"/> with the given type name and parameter name.
    /// </summary>
    public static ParameterNode MakeParameter(string typeName, string paramName)
    {
        PrimitiveTypeNode primitiveType = PrimitiveTypeNodeFactory.TryGetValue(typeName, out var factory)
            ? factory()
            : new IntTypePrimitiveTypeNode();

        return new ParameterNode
        {
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveType },
            Name = Tok(paramName),
        };
    }

    /// <summary>
    /// Creates a <see cref="ParameterNode"/> from an <see cref="IType"/>.
    /// </summary>
    public static ParameterNode MakeParameter(IType type, string paramName) =>
        MakeParameter(type.DisplayName, paramName);

    /// <summary>
    /// Creates a method declaration with a single parameter of the given type.
    /// </summary>
    public static MethodDeclarationNode MakeMethod(string name, IType paramType)
    {
        var param = MakeParameter(paramType, "p0");
        return new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = new ParameterListNode { Parameter = param },
            Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    /// <summary>
    /// Creates a parameterless method declaration.
    /// </summary>
    public static MethodDeclarationNode MakeParameterlessMethod(string name)
    {
        return new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    /// <summary>
    /// Creates a constructor declaration with a single parameter of the given type.
    /// </summary>
    public static ConstructorDeclarationNode MakeConstructor(string className, IType paramType)
    {
        var param = MakeParameter(paramType, "p0");
        return new ConstructorDeclarationNode
        {
            Name = Tok(className),
            Parameters = new ParameterListNode { Parameter = param },
            Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
        };
    }

    /// <summary>
    /// Creates a parameterless constructor declaration.
    /// </summary>
    public static ConstructorDeclarationNode MakeParameterlessConstructor(string className)
    {
        return new ConstructorDeclarationNode
        {
            Name = Tok(className),
            Parameters = null,
            Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
        };
    }

    /// <summary>
    /// Creates a class declaration with optional base class and the given members.
    /// </summary>
    public static ClassDeclarationNode MakeClass(
        string name,
        ImmutableArray<MemberDeclarationNode> members,
        string? baseClassName = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(name),
            Members = members,
        };

        if (baseClassName is not null)
        {
            var baseList = MakeBaseList(baseClassName);
            classDecl.BaseClause = baseList;
            classDecl.BaseList = baseList;
        }

        return classDecl;
    }

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = Tok(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
    }

    /// <summary>
    /// Creates a <see cref="CompilationUnitNode"/> from one or more class declarations.
    /// </summary>
    public static CompilationUnitNode MakeCompilationUnit(params ClassDeclarationNode[] classDecls)
    {
        var types = classDecls.Select(c =>
                (TypeDeclarationNode)new ClassTypeTypeDeclarationNode { ClassDeclaration = c })
            .ToImmutableArray();

        return new CompilationUnitNode { Items = (types).AsTopLevelItems() };
    }

    /// <summary>
    /// Creates a default literal expression for the given type, suitable for use as
    /// a method call argument.
    /// </summary>
    public static ExpressionNode MakeDefaultExpr(IType type)
    {
        return type.DisplayName switch
        {
            "int" or "short" or "long" => MistAstGenerator.WrapPrimary(
                new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("0") }),
            "bool" => MistAstGenerator.WrapPrimary(new TrueLiteralExpressionPrimaryNode()),
            "double" or "float" or "decimal" => MistAstGenerator.WrapPrimary(
                new FloatLiteralExpressionPrimaryNode { FloatLiteral = Tok("0.0") }),
            "char" => MistAstGenerator.WrapPrimary(
                new CharLiteralExpressionPrimaryNode { CharLiteral = Tok("'a'") }),
            "string" => MistAstGenerator.WrapPrimary(
                new StringLiteralExpressionPrimaryNode { StringLiteral = Tok("\"\"") }),
            _ => MistAstGenerator.WrapPrimary(
                new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("0") }),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator 1: GenOverloadedMethods
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates N method declarations sharing a name with distinct single-parameter signatures.
    /// Each method has a different primitive type as its parameter.
    /// </summary>
    /// <param name="name">The shared method name.</param>
    /// <param name="n">Number of overloads to generate (2–7).</param>
    /// <returns>A generator producing a list of <see cref="MethodDeclarationNode"/>.</returns>
    public static Gen<IReadOnlyList<MethodDeclarationNode>> GenOverloadedMethods(string name, int n)
    {
        var count = Math.Clamp(n, 2, DistinctPrimitiveTypes.Length);

        return Gen.ArrayOf(Gen.Choose(0, DistinctPrimitiveTypes.Length - 1), count)
            .Where(indices => indices.Distinct().Count() == indices.Length)
            .Select(indices => (IReadOnlyList<MethodDeclarationNode>)indices
                .Select(i => MakeMethod(name, DistinctPrimitiveTypes[i]))
                .ToList());
    }

    /// <summary>
    /// Generates 2–5 method declarations sharing a name with distinct parameter signatures.
    /// The overload count is randomly chosen.
    /// </summary>
    /// <param name="name">The shared method name.</param>
    /// <returns>A generator producing a list of <see cref="MethodDeclarationNode"/>.</returns>
    public static Gen<IReadOnlyList<MethodDeclarationNode>> GenOverloadedMethods(string name)
    {
        return from count in Gen.Choose(2, 5)
               from methods in GenOverloadedMethods(name, count)
               select methods;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator 2: GenOverloadedConstructors
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates N constructor declarations with distinct single-parameter signatures.
    /// Each constructor has a different primitive type as its parameter.
    /// </summary>
    /// <param name="n">Number of overloads to generate (2–7).</param>
    /// <param name="className">The class name for the constructors (default: "C").</param>
    /// <returns>A generator producing a list of <see cref="ConstructorDeclarationNode"/>.</returns>
    public static Gen<IReadOnlyList<ConstructorDeclarationNode>> GenOverloadedConstructors(
        int n,
        string className = "C")
    {
        var count = Math.Clamp(n, 2, DistinctPrimitiveTypes.Length);

        return Gen.ArrayOf(Gen.Choose(0, DistinctPrimitiveTypes.Length - 1), count)
            .Where(indices => indices.Distinct().Count() == indices.Length)
            .Select(indices => (IReadOnlyList<ConstructorDeclarationNode>)indices
                .Select(i => MakeConstructor(className, DistinctPrimitiveTypes[i]))
                .ToList());
    }

    /// <summary>
    /// Generates 2–5 constructor declarations with distinct parameter signatures.
    /// The overload count is randomly chosen.
    /// </summary>
    /// <param name="className">The class name for the constructors (default: "C").</param>
    /// <returns>A generator producing a list of <see cref="ConstructorDeclarationNode"/>.</returns>
    public static Gen<IReadOnlyList<ConstructorDeclarationNode>> GenOverloadedConstructors(
        string className = "C")
    {
        return from count in Gen.Choose(2, 5)
               from ctors in GenOverloadedConstructors(count, className)
               select ctors;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator 3: GenDuplicateSignatureMethods
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates two methods with the same name and the same single-parameter signature.
    /// The parameter type is randomly chosen from the available primitive types.
    /// </summary>
    /// <param name="name">The shared method name.</param>
    /// <returns>A generator producing a pair of <see cref="MethodDeclarationNode"/> with identical signatures.</returns>
    public static Gen<(MethodDeclarationNode First, MethodDeclarationNode Second)> GenDuplicateSignatureMethods(
        string name)
    {
        return Gen.Choose(0, DistinctPrimitiveTypes.Length - 1)
            .Select(i =>
            {
                var paramType = DistinctPrimitiveTypes[i];
                return (MakeMethod(name, paramType), MakeMethod(name, paramType));
            });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator 4: GenOverloadCallSite
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a list of argument types that exactly match the parameter signature
    /// of the overload at <paramref name="matchIndex"/> in the given overload set.
    /// The returned types can be used to construct a call site that resolves to
    /// the specified overload.
    /// </summary>
    /// <param name="overloadParamTypes">
    /// The parameter types for each overload. Each entry is the list of parameter types
    /// for one overload.
    /// </param>
    /// <param name="matchIndex">The index of the overload to match.</param>
    /// <returns>A generator producing the argument types for the call site.</returns>
    public static Gen<IReadOnlyList<IType>> GenOverloadCallSite(
        IReadOnlyList<IReadOnlyList<IType>> overloadParamTypes,
        int matchIndex)
    {
        var targetParams = overloadParamTypes[matchIndex];
        return Gen.Constant((IReadOnlyList<IType>)targetParams.ToList());
    }

    /// <summary>
    /// Generates a scenario with an overload set and a call site that matches one specific overload.
    /// Returns the overload methods, the matching index, and the argument types.
    /// </summary>
    /// <param name="name">The shared method name.</param>
    /// <returns>A generator producing an overload call-site scenario.</returns>
    public static Gen<OverloadCallSiteScenario> GenOverloadCallSiteScenario(string name)
    {
        return from count in Gen.Choose(2, 5)
               from typeIndices in Gen.ArrayOf(Gen.Choose(0, DistinctPrimitiveTypes.Length - 1), count)
                   .Where(indices => indices.Distinct().Count() == indices.Length)
               from matchIndex in Gen.Choose(0, count - 1)
               let paramTypes = typeIndices.Select(i => DistinctPrimitiveTypes[i]).ToList()
               let methods = paramTypes.Select(t => MakeMethod(name, t)).ToList()
               let argTypes = new[] { paramTypes[matchIndex] }
               select new OverloadCallSiteScenario(
                   methods,
                   matchIndex,
                   argTypes,
                   paramTypes);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator 5: GenAmbiguousCallSite
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a call site where multiple overloads are equally applicable.
    /// Uses known ambiguous type combinations where two parameter types both accept
    /// the argument type via widening, but neither is assignable to the other.
    /// </summary>
    /// <param name="name">The shared method name.</param>
    /// <returns>A generator producing an ambiguous call-site scenario.</returns>
    public static Gen<AmbiguousCallSiteScenario> GenAmbiguousCallSite(string name)
    {
        return Gen.Choose(0, AmbiguousTuples.Length - 1)
            .Select(i =>
            {
                var (param1, param2, arg) = AmbiguousTuples[i];
                var methods = new List<MethodDeclarationNode>
                {
                    MakeMethod(name, param1),
                    MakeMethod(name, param2),
                };
                return new AmbiguousCallSiteScenario(
                    methods,
                    new[] { arg },
                    param1,
                    param2,
                    arg);
            });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator 6: GenClassHierarchyWithOverloads
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a base/derived class pair where both classes have a method with the
    /// same name but different parameter signatures (valid cross-hierarchy overload).
    /// The base class has method M with one parameter type, and the derived class has
    /// method M with a different parameter type.
    /// </summary>
    /// <returns>A generator producing a class hierarchy overload scenario.</returns>
    public static Gen<ClassHierarchyOverloadScenario> GenClassHierarchyWithOverloads()
    {
        // Pairs of distinct types for base and derived methods
        var distinctPairs = new (IType Base, IType Derived)[]
        {
            (PrimitiveType.Int, PrimitiveType.Bool),
            (PrimitiveType.Bool, PrimitiveType.Int),
            (PrimitiveType.Int, PrimitiveType.Double),
            (PrimitiveType.Double, PrimitiveType.Float),
            (PrimitiveType.Char, PrimitiveType.Long),
            (PrimitiveType.Long, StringType.Instance),
            (StringType.Instance, PrimitiveType.Int),
        };

        return Gen.Choose(0, distinctPairs.Length - 1)
            .Select(i =>
            {
                var (baseType, derivedType) = distinctPairs[i];

                var baseMethod = MakeMethod("M", baseType);
                var derivedMethod = MakeMethod("M", derivedType);

                var baseClass = MakeClass(
                    "Base",
                    ImmutableArray.Create<MemberDeclarationNode>(
                        new MethodMemberMemberDeclarationNode { Method = baseMethod }));

                var derivedClass = MakeClass(
                    "Derived",
                    ImmutableArray.Create<MemberDeclarationNode>(
                        new MethodMemberMemberDeclarationNode { Method = derivedMethod }),
                    baseClassName: "Base");

                return new ClassHierarchyOverloadScenario(
                    baseClass,
                    derivedClass,
                    baseType,
                    derivedType);
            });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario record types
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034 // Nested types should not be visible

    /// <summary>
    /// Scenario for a call site that matches a specific overload in an overload set.
    /// </summary>
    public record OverloadCallSiteScenario(
        IReadOnlyList<MethodDeclarationNode> Methods,
        int MatchingIndex,
        IReadOnlyList<IType> ArgumentTypes,
        IReadOnlyList<IType> OverloadParamTypes)
    {
        /// <inheritdoc />
        public override string ToString() =>
            $"CallSite: {Methods.Count} overloads " +
            $"[{string.Join(", ", OverloadParamTypes.Select(t => t.DisplayName))}], " +
            $"match at {MatchingIndex}, args=({string.Join(", ", ArgumentTypes.Select(t => t.DisplayName))})";
    }

    /// <summary>
    /// Scenario for an ambiguous call site where multiple overloads are equally applicable.
    /// </summary>
    public record AmbiguousCallSiteScenario(
        IReadOnlyList<MethodDeclarationNode> Methods,
        IReadOnlyList<IType> ArgumentTypes,
        IType Param1Type,
        IType Param2Type,
        IType ArgType)
    {
        /// <inheritdoc />
        public override string ToString() =>
            $"Ambiguous: param1={Param1Type.DisplayName}, param2={Param2Type.DisplayName}, " +
            $"arg={ArgType.DisplayName}";
    }

    /// <summary>
    /// Scenario for a class hierarchy with overloaded methods across base and derived classes.
    /// </summary>
    public record ClassHierarchyOverloadScenario(
        ClassDeclarationNode BaseClass,
        ClassDeclarationNode DerivedClass,
        IType BaseParamType,
        IType DerivedParamType)
    {
        /// <inheritdoc />
        public override string ToString() =>
            $"Hierarchy: Base.M({BaseParamType.DisplayName}) + Derived.M({DerivedParamType.DisplayName})";
    }

#pragma warning restore CA1034
}
