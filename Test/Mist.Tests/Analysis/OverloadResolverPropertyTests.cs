// Feature: method-overloading, Properties 6–9: OverloadResolver property tests
// Property 6: Single applicable overload resolves without error
// **Validates: Requirements 3.1, 3.2, 2.3**
// Property 7: Most-specific candidate wins
// **Validates: Requirements 3.3**
// Property 8: No applicable candidate produces diagnostic
// **Validates: Requirements 3.4, 3.5**
// Property 9: Ambiguous call produces diagnostic
// **Validates: Requirements 4.1, 4.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for <see cref="OverloadResolver"/>.
/// Tests verify overload resolution behavior across generated scenarios:
/// - Single applicable overload resolves successfully (Property 6)
/// - Most-specific candidate wins among multiple applicable (Property 7)
/// - No applicable candidate produces NoApplicableCandidate (Property 8)
/// - Ambiguous candidates produce AmbiguousCall (Property 9)
/// </summary>
public class OverloadResolverPropertyTests
{
    // ─── Shared helpers ──────────────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    /// <summary>
    /// All primitive types available for generating parameter signatures.
    /// These are distinct types where IsAssignable(a, b) is false when a != b
    /// (except for numeric widening, which we handle separately).
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
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            "short" => new ShortTypePrimitiveTypeNode(),
            "decimal" => new DecimalTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };

        return new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Name = MakeToken(paramName),
        };
    }

    /// <summary>
    /// Maps an IType to the type name string used in MakeParameter.
    /// </summary>
    private static string TypeToName(IType type)
    {
        return type.DisplayName;
    }

    /// <summary>
    /// Creates a method declaration node with the given name and a single parameter type.
    /// Uses single-parameter overloads which are sufficient to test all resolution properties.
    /// </summary>
    private static MethodDeclarationNode MakeMethod(string name, IType paramType)
    {
        var param = MakeParameter(TypeToName(paramType), "p0");
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
    /// Creates a parameterless method declaration node.
    /// </summary>
    private static MethodDeclarationNode MakeParameterlessMethod(string name)
    {
        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    /// <summary>
    /// Builds a class with the given method declarations and returns the semantic model.
    /// </summary>
    private static ISemanticModel BuildClassWithMethods(string className, IReadOnlyList<MethodDeclarationNode> methods)
    {
        var members = methods.Select(m =>
            (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m }).ToImmutableArray();

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = members,
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        return BuildModel(ast);
    }

    /// <summary>
    /// Builds a class with the given method declarations and returns the semantic model,
    /// with stdlib types loaded so that implicit cast operators are available.
    /// </summary>
    private static ISemanticModel BuildClassWithMethodsWithStdlib(string className, IReadOnlyList<MethodDeclarationNode> methods)
    {
        var members = methods.Select(m =>
            (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m }).ToImmutableArray();

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = members,
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);
        return model;
    }

    /// <summary>
    /// Finds the stdlib type whose display name matches the given legacy primitive type.
    /// Returns the stdlib type (which has implicit cast operators in its member scope),
    /// or falls back to the original type if not found.
    /// </summary>
    private static IType FindStdlibType(ISemanticModel model, IType legacyType)
    {
        return model.AllSymbols
            .Where(s => s.Kind is SymbolKind.Struct or SymbolKind.Class
                        && s.DeclaredType is not null
                        && ArePrimitiveEquivalent(s.DeclaredType, legacyType))
            .Select(s => s.DeclaredType!)
            .FirstOrDefault() ?? legacyType;
    }

    /// <summary>
    /// Checks whether two types are primitive-equivalent (e.g. "int" ↔ "Int32").
    /// </summary>
    private static bool ArePrimitiveEquivalent(IType a, IType b)
    {
        if(a.DisplayName == b.DisplayName)
        {
            return true;
        }

        // Map legacy display names to stdlib names and vice versa.
        var map = new Dictionary<string, string>
        {
            ["int"] = "Int32",
            ["Int32"] = "int",
            ["long"] = "Int64",
            ["Int64"] = "long",
            ["short"] = "Int16",
            ["Int16"] = "short",
            ["float"] = "Single",
            ["Single"] = "float",
            ["double"] = "Double",
            ["Double"] = "double",
            ["decimal"] = "Decimal",
            ["Decimal"] = "decimal",
            ["bool"] = "Boolean",
            ["Boolean"] = "bool",
            ["char"] = "Char",
            ["Char"] = "char",
            ["string"] = "String",
            ["String"] = "string",
            ["byte"] = "Byte",
            ["Byte"] = "byte",
            ["sbyte"] = "SByte",
            ["SByte"] = "sbyte",
            ["ushort"] = "UInt16",
            ["UInt16"] = "ushort",
            ["uint"] = "UInt32",
            ["UInt32"] = "uint",
            ["ulong"] = "UInt64",
            ["UInt64"] = "ulong",
        };

        return map.TryGetValue(a.DisplayName, out var mapped) && mapped == b.DisplayName;
    }

    /// <summary>
    /// Collects all method symbols with the given name from the semantic model.
    /// </summary>
    private static IReadOnlyList<ISymbol> GetMethodCandidates(ISemanticModel model, string methodName)
    {
        return model.AllSymbols
            .Where(s => s.Name == methodName && s.Kind == SymbolKind.Method)
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types for FsCheck
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario for Property 6: A set of overloads where exactly one matches
    /// the given argument type (single-parameter overloads).
    /// </summary>
#pragma warning disable CA1034
    public record SingleMatchScenario(
        IReadOnlyList<IType> OverloadParamTypes,
        int MatchingIndex)
    {
        public override string ToString()
        {
            return $"SingleMatch: {OverloadParamTypes.Count} overloads " +
            $"[{string.Join(", ", OverloadParamTypes.Select(t => t.DisplayName))}], " +
            $"match at index {MatchingIndex} ({OverloadParamTypes[MatchingIndex].DisplayName})";
        }
    }

    /// <summary>
    /// Scenario for Property 7: Multiple applicable overloads where one is
    /// strictly more specific.
    /// </summary>
    public record MostSpecificScenario(
        IType SpecificParamType,
        IType BroadParamType,
        IType ArgumentType)
    {
        public override string ToString()
        {
            return $"MostSpecific: specific={SpecificParamType.DisplayName}, " +
            $"broad={BroadParamType.DisplayName}, arg={ArgumentType.DisplayName}";
        }
    }

    /// <summary>
    /// Scenario for Property 8: An overload set where no candidate matches
    /// the argument types (type mismatch or arity mismatch).
    /// </summary>
    public record NoMatchScenario(
        IReadOnlyList<IType> OverloadParamTypes,
        IReadOnlyList<IType> ArgumentTypes,
        string Reason)
    {
        public override string ToString()
        {
            return $"NoMatch ({Reason}): {OverloadParamTypes.Count} overloads " +
            $"[{string.Join(", ", OverloadParamTypes.Select(t => t.DisplayName))}], " +
            $"args=({string.Join(", ", ArgumentTypes.Select(t => t.DisplayName))})";
        }
    }

    /// <summary>
    /// Scenario for Property 9: Two overloads that are equally applicable
    /// for the given argument types.
    /// </summary>
    public record AmbiguousScenario(
        IType ParamType1,
        IType ParamType2,
        IType ArgumentType)
    {
        public override string ToString()
        {
            return $"Ambiguous: param1={ParamType1.DisplayName}, param2={ParamType2.DisplayName}, " +
            $"arg={ArgumentType.DisplayName}";
        }
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates scenarios where exactly one overload matches the argument types.
    /// Strategy: pick N distinct primitive types for N single-param overloads,
    /// then use one of those types as the argument type for an exact match.
    /// </summary>
    public static Arbitrary<SingleMatchScenario> ArbitrarySingleMatchScenarios()
    {
        var gen =
            from overloadCount in Gen.Choose(2, 5)
            from typeIndices in Gen.ArrayOf(Gen.Choose(0, DistinctPrimitiveTypes.Length - 1), overloadCount)
                .Where(indices => indices.Distinct().Count() == indices.Length) // all distinct
            from matchIndex in Gen.Choose(0, overloadCount - 1)
            let paramTypes = typeIndices.Select(i => DistinctPrimitiveTypes[i]).ToList()
            select new SingleMatchScenario(paramTypes, matchIndex);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates scenarios where multiple overloads are applicable but one is
    /// more specific due to numeric widening.
    /// Strategy: use int→long widening. One overload takes int (specific),
    /// another takes long (broad). Argument is int → both applicable, int wins.
    /// We vary which numeric widening pair to use.
    /// </summary>
    public static Arbitrary<MostSpecificScenario> ArbitraryMostSpecificScenarios()
    {
        // Pairs where (specific, broad) means specific is assignable to broad
        // but broad is NOT assignable to specific.
        var wideningPairs = new (IType specific, IType broad)[]
        {
            (LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Long),
            (LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Double),
            (LegacyPrimitiveTypes.Float, LegacyPrimitiveTypes.Double),
            (LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Float),
            (LegacyPrimitiveTypes.Short, LegacyPrimitiveTypes.Int),
            (LegacyPrimitiveTypes.Short, LegacyPrimitiveTypes.Long),
        };

        var gen = from pairIndex in Gen.Choose(0, wideningPairs.Length - 1)
                  let pair = wideningPairs[pairIndex]
                  select new MostSpecificScenario(pair.specific, pair.broad, pair.specific);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates scenarios where no overload matches the argument types.
    /// Strategy 1: Type mismatch — overloads expect types not assignable from the argument.
    /// Strategy 2: Arity mismatch — overloads have 1 param, call has 0 args.
    /// </summary>
    public static Arbitrary<NoMatchScenario> ArbitraryNoMatchScenarios()
    {
        // Strategy 1: Type mismatch — overloads expect int/bool/double, argument is string
        var typeMismatchGen =
            from overloadCount in Gen.Choose(1, 3) // max 3 to fit in 3 distinct types
            from paramTypeIndices in Gen.ArrayOf(Gen.Choose(0, 2), overloadCount) // int, bool, double — first 3 types
                .Where(indices => indices.Distinct().Count() == indices.Length)
            let paramTypes = paramTypeIndices.Select(i => DistinctPrimitiveTypes[i]).ToList()
            // Use string as argument — not assignable to int, bool, or double
            let argTypes = (IReadOnlyList<IType>)new[] { LegacyPrimitiveTypes.String }
            where paramTypes.All(t => t.DisplayName != "string")
            select new NoMatchScenario(paramTypes, argTypes, "type mismatch");

        // Strategy 2: Arity mismatch — overloads have 1 param, call has 0 args
        var arityMismatchGen =
            from overloadCount in Gen.Choose(1, 3)
            from paramTypeIndices in Gen.ArrayOf(Gen.Choose(0, DistinctPrimitiveTypes.Length - 1), overloadCount)
                .Where(indices => indices.Distinct().Count() == indices.Length)
            let paramTypes = paramTypeIndices.Select(i => DistinctPrimitiveTypes[i]).ToList()
            let argTypes = (IReadOnlyList<IType>)Array.Empty<IType>()
            select new NoMatchScenario(paramTypes, argTypes, "arity mismatch");

        var gen = Gen.OneOf(typeMismatchGen, arityMismatchGen);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates scenarios where two overloads are equally applicable (ambiguous).
    /// Strategy: use two types that both accept the argument type via widening,
    /// but NEITHER is assignable to the other (so no "more specific" winner).
    /// For example: float and decimal both accept int, but float→decimal is false
    /// and decimal→float is false.
    /// </summary>
    public static Arbitrary<AmbiguousScenario> ArbitraryAmbiguousScenarios()
    {
        // Truly ambiguous pairs: both accept the argument type, but neither
        // is assignable to the other.
        // int → float (yes), int → decimal (yes), float → decimal (no), decimal → float (no)
        // int → double (yes), int → decimal (yes), double → decimal (no), decimal → double (no)
        // short → float (yes), short → decimal (yes), float → decimal (no), decimal → float (no)
        var ambiguousPairs = new (IType param1, IType param2, IType arg)[]
        {
            (LegacyPrimitiveTypes.Float, LegacyPrimitiveTypes.Decimal, LegacyPrimitiveTypes.Int),
            (LegacyPrimitiveTypes.Double, LegacyPrimitiveTypes.Decimal, LegacyPrimitiveTypes.Int),
            (LegacyPrimitiveTypes.Float, LegacyPrimitiveTypes.Decimal, LegacyPrimitiveTypes.Short),
            (LegacyPrimitiveTypes.Double, LegacyPrimitiveTypes.Decimal, LegacyPrimitiveTypes.Short),
        };

        var gen = from pairIndex in Gen.Choose(0, ambiguousPairs.Length - 1)
                  let pair = ambiguousPairs[pairIndex]
                  select new AmbiguousScenario(pair.param1, pair.param2, pair.arg);

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Single applicable overload resolves without error
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 6: For any overload set and call site where exactly one candidate
    /// is applicable, the resolver selects that candidate.
    ///
    /// **Validates: Requirements 3.1, 3.2, 2.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OverloadResolverPropertyTests) }, MaxTest = 20)]
    public bool SingleApplicableOverload_ResolvesWithoutError(SingleMatchScenario scenario)
    {
        // Arrange: build methods with distinct single-parameter types
        var methods = new List<MethodDeclarationNode>();
        for(var i = 0; i < scenario.OverloadParamTypes.Count; i++)
        {
            methods.Add(MakeMethod("M", scenario.OverloadParamTypes[i]));
        }

        var model = BuildClassWithMethods("C", methods);
        var candidates = GetMethodCandidates(model, "M");

        // Verify we got the expected number of candidates
        if(candidates.Count != scenario.OverloadParamTypes.Count)
        {
            return false;
        }

        // Act: call with the exact type of the matching overload
        var argTypes = new IType[] { scenario.OverloadParamTypes[scenario.MatchingIndex] };
        var result = OverloadResolver.Resolve(candidates, argTypes, model);

        // Assert: resolution succeeds
        if(result.Status != OverloadResolutionStatus.Success)
        {
            return false;
        }

        // Assert: the selected candidate has the expected parameter type
        if(result.BestCandidate is null)
        {
            return false;
        }

        var selectedSig = ParameterSignature.Extract(result.BestCandidate, model);
        return selectedSig.Count == 1
               && selectedSig[0].DisplayName == scenario.OverloadParamTypes[scenario.MatchingIndex].DisplayName;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Most-specific candidate wins
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 7: For any overload set where multiple candidates are applicable
    /// but one has strictly more specific parameter types, the resolver selects
    /// the most-specific candidate.
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OverloadResolverPropertyTests) }, MaxTest = 20)]
    public bool MostSpecificCandidate_Wins(MostSpecificScenario scenario)
    {
        // Arrange: two overloads — one with the specific type, one with the broad type
        var specificMethod = MakeMethod("M", scenario.SpecificParamType);
        var broadMethod = MakeMethod("M", scenario.BroadParamType);

        var model = BuildClassWithMethods("C", new[] { specificMethod, broadMethod });
        var candidates = GetMethodCandidates(model, "M");

        if(candidates.Count != 2)
        {
            return false;
        }

        // Act: call with the specific type as argument
        var argTypes = new IType[] { scenario.ArgumentType };
        var result = OverloadResolver.Resolve(candidates, argTypes, model);

        // Assert: resolution succeeds and picks the specific overload
        if(result.Status != OverloadResolutionStatus.Success)
        {
            return false;
        }

        if(result.BestCandidate is null)
        {
            return false;
        }

        var selectedSig = ParameterSignature.Extract(result.BestCandidate, model);
        return selectedSig.Count == 1
               && selectedSig[0].DisplayName == scenario.SpecificParamType.DisplayName;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: No applicable candidate produces diagnostic
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 8: For any overload set and call site where no candidate matches,
    /// the resolver returns NoApplicableCandidate.
    ///
    /// **Validates: Requirements 3.4, 3.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OverloadResolverPropertyTests) }, MaxTest = 20)]
    public bool NoApplicableCandidate_ProducesDiagnostic(NoMatchScenario scenario)
    {
        // Arrange: build methods with the given parameter types
        var methods = new List<MethodDeclarationNode>();
        for(var i = 0; i < scenario.OverloadParamTypes.Count; i++)
        {
            methods.Add(MakeMethod("M", scenario.OverloadParamTypes[i]));
        }

        var model = BuildClassWithMethods("C", methods);
        var candidates = GetMethodCandidates(model, "M");

        if(candidates.Count != scenario.OverloadParamTypes.Count)
        {
            return false;
        }

        // Act
        var result = OverloadResolver.Resolve(candidates, scenario.ArgumentTypes, model);

        // Assert: no applicable candidate
        if(result.Status != OverloadResolutionStatus.NoApplicableCandidate)
        {
            return false;
        }

        // Assert: no best candidate selected
        if(result.BestCandidate is not null)
        {
            return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Ambiguous call produces diagnostic
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 9: For any overload set and call site where two or more candidates
    /// are equally applicable, the resolver returns AmbiguousCall.
    ///
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OverloadResolverPropertyTests) }, MaxTest = 20)]
    public bool AmbiguousCall_ProducesDiagnostic(AmbiguousScenario scenario)
    {
        // Arrange: two overloads with types that are both applicable but neither
        // is more specific than the other.
        // Build with stdlib so that primitive type keywords resolve to stdlib types
        // (e.g. int → Standard.Int32) which have implicit cast operators in their
        // member scopes, enabling numeric widening in IsAssignable.
        var method1 = MakeMethod("M", scenario.ParamType1);
        var method2 = MakeMethod("M", scenario.ParamType2);

        var model = BuildClassWithMethodsWithStdlib("C", new[] { method1, method2 });
        var candidates = GetMethodCandidates(model, "M");

        if(candidates.Count != 2)
        {
            return false;
        }

        // Look up the actual stdlib type for the argument so that
        // TryFindImplicitCast can find the implicit cast operators
        // on the type's member scope.
        var actualArgType = FindStdlibType(model, scenario.ArgumentType);
        var argTypes = new IType[] { actualArgType };
        var result = OverloadResolver.Resolve(candidates, argTypes, model);

        // Assert: ambiguous call
        if(result.Status != OverloadResolutionStatus.AmbiguousCall)
        {
            return false;
        }

        // Assert: no single best candidate
        if(result.BestCandidate is not null)
        {
            return false;
        }

        // Assert: ambiguous candidates list is populated with at least 2 candidates
        if(result.AmbiguousCandidates is null || result.AmbiguousCandidates.Count < 2)
        {
            return false;
        }

        return true;
    }
}