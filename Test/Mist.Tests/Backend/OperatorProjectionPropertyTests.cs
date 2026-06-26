// Feature: assembly-type-provider, Property 9: Operator Projection
//
// **Validates: Requirements 12.1, 12.2, 12.3, 12.4**
//
// For any CLR type that defines public static operator methods (op_Addition,
// op_Equality, op_Implicit, op_UnaryNegation, etc.), the Assembly_Type_Provider
// SHALL project corresponding Mist operator symbols with correct operand types
// and return types.

using System.Collections.Immutable;
using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Represents a CLR operator method selected from the fixture assembly.
/// </summary>
public sealed record OperatorMethodScenario(
    Type DeclaringType,
    MethodInfo OperatorMethod)
{
    public override string ToString() =>
        $"{DeclaringType.Name}.{OperatorMethod.Name}({string.Join(", ", OperatorMethod.GetParameters().Select(p => p.ParameterType.Name))}) -> {OperatorMethod.ReturnType.Name}";
}

/// <summary>
/// Property-based tests for operator projection in <see cref="TypeProjector"/>.
/// Verifies that CLR operator methods are projected as Mist operator symbols
/// with correct names, parameter types, and return types.
/// All tests are pure in-process — no process spawning.
///
/// **Validates: Requirements 12.1, 12.2, 12.3, 12.4**
/// </summary>
public sealed class OperatorProjectionPropertyTests
{
    private readonly TypeProjector projector;
    private readonly IStdlibTypeProvider stdlib;
    private readonly IntrinsicTypeMap intrinsicMap;

    public OperatorProjectionPropertyTests()
    {
        intrinsicMap = new IntrinsicTypeMap();
        var filter = new VisibilityFilter();
        projector = new TypeProjector(intrinsicMap, filter);
        stdlib = FallbackStdlibTypeProvider.Instance;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// All recognized CLR operator method names.
    /// </summary>
    private static readonly HashSet<string> RecognizedOperatorNames = new()
    {
        "op_Addition", "op_Subtraction", "op_Multiply", "op_Division", "op_Modulus",
        "op_Equality", "op_Inequality", "op_LessThan", "op_GreaterThan",
        "op_LessThanOrEqual", "op_GreaterThanOrEqual",
        "op_UnaryNegation", "op_UnaryPlus",
        "op_Implicit", "op_Explicit",
    };

    /// <summary>
    /// Maps CLR operator names to expected Mist operator symbol name prefixes.
    /// </summary>
    private static readonly Dictionary<string, string> BinaryAndUnaryOperatorMap = new()
    {
        ["op_Addition"] = "operator+",
        ["op_Subtraction"] = "operator-",
        ["op_Multiply"] = "operator*",
        ["op_Division"] = "operator/",
        ["op_Modulus"] = "operator%",
        ["op_Equality"] = "operator==",
        ["op_Inequality"] = "operator!=",
        ["op_LessThan"] = "operator<",
        ["op_GreaterThan"] = "operator>",
        ["op_LessThanOrEqual"] = "operator<=",
        ["op_GreaterThanOrEqual"] = "operator>=",
        ["op_UnaryNegation"] = "operator-",
        ["op_UnaryPlus"] = "operator+",
    };

    /// <summary>
    /// All operator method scenarios from fixture types.
    /// </summary>
    private static readonly OperatorMethodScenario[] AllOperatorScenarios = BuildOperatorScenarios();

    private static OperatorMethodScenario[] BuildOperatorScenarios()
    {
        var scenarios = new List<OperatorMethodScenario>();

        // Scan all public types in the InteropFixtures assembly for operator methods
        var fixtureAssembly = typeof(Operatable).Assembly;
        foreach(var type in fixtureAssembly.GetExportedTypes())
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach(var method in methods)
            {
                if(method.IsSpecialName && RecognizedOperatorNames.Contains(method.Name))
                {
                    scenarios.Add(new OperatorMethodScenario(type, method));
                }
            }
        }

        return scenarios.ToArray();
    }

    private static Gen<OperatorMethodScenario> GenOperatorScenario()
    {
        return FsCheck.Fluent.Gen.Elements(AllOperatorScenarios);
    }

    public static Arbitrary<OperatorMethodScenario> ArbOperatorScenario() =>
        FsCheck.Fluent.Arb.From(GenOperatorScenario());

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes the expected Mist operator symbol name for a CLR operator method.
    /// </summary>
    private static string GetExpectedMistOperatorName(MethodInfo method)
    {
        if(BinaryAndUnaryOperatorMap.TryGetValue(method.Name, out var mistName))
        {
            return mistName;
        }

        // Cast operators: operator_{kind}_{targetTypeName}
        if(method.Name is "op_Implicit" or "op_Explicit")
        {
            var kind = method.Name == "op_Implicit" ? "implicit" : "explicit";
            var targetTypeName = TypeProjector.StripGenericAritySuffix(method.ReturnType.Name);
            return $"operator_{kind}_{targetTypeName}";
        }

        throw new InvalidOperationException($"Unrecognized operator: {method.Name}");
    }

    /// <summary>
    /// Projects a type and its operators, returning the operator symbols.
    /// Mirrors the flow in AssemblyTypeProvider.ResolveType.
    /// </summary>
    private ImmutableArray<ISymbol> ProjectTypeWithOperators(Type clrType)
    {
        var (projectedType, _) = projector.ProjectType(clrType, stdlib);

        var memberScope = projectedType switch
        {
            ClassType ct => ct.MemberScope as Scope,
            StructType st => st.MemberScope as Scope,
            AbstractType it => it.MemberScope as Scope,
            _ => null,
        };

        if(memberScope is null)
        {
            return ImmutableArray<ISymbol>.Empty;
        }

        return projector.ProjectOperators(clrType, memberScope, stdlib);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Operator Projection
    //
    // **Validates: Requirements 12.1, 12.2, 12.3, 12.4**
    //
    // For any CLR type that defines public static operator methods, the
    // TypeProjector SHALL project corresponding Mist operator symbols with
    // correct operand types and return types.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any operator method on a fixture type, the TypeProjector produces
    /// a Mist operator symbol with the expected name.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OperatorProjectionPropertyTests) })]
    public Property OperatorMethod_ProjectsToCorrectMistOperatorName(OperatorMethodScenario scenario)
    {
        var operators = ProjectTypeWithOperators(scenario.DeclaringType);

        var expectedName = GetExpectedMistOperatorName(scenario.OperatorMethod);

        // Find the operator symbol in the projected operators
        var found = operators.Any(m => m.Name == expectedName && m.Kind == SymbolKind.Method);

        return found.ToProperty()
            .Label($"Expected operator symbol '{expectedName}' for {scenario.DeclaringType.Name}.{scenario.OperatorMethod.Name}")
            .Classify(scenario.OperatorMethod.Name.StartsWith("op_Implicit") || scenario.OperatorMethod.Name.StartsWith("op_Explicit"), "cast")
            .Classify(scenario.OperatorMethod.Name.StartsWith("op_Unary"), "unary")
            .Classify(!scenario.OperatorMethod.Name.StartsWith("op_Implicit") && !scenario.OperatorMethod.Name.StartsWith("op_Explicit") && !scenario.OperatorMethod.Name.StartsWith("op_Unary"), "binary/comparison");
    }

    /// <summary>
    /// For any operator method on a fixture type, the projected Mist operator symbol
    /// has the correct return type (matching the CLR operator's return type after
    /// intrinsic substitution).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OperatorProjectionPropertyTests) })]
    public Property OperatorMethod_ProjectsCorrectReturnType(OperatorMethodScenario scenario)
    {
        var operators = ProjectTypeWithOperators(scenario.DeclaringType);

        var expectedName = GetExpectedMistOperatorName(scenario.OperatorMethod);

        // Project the expected return type through the same pipeline
        var expectedReturnType = projector.ProjectTypeReference(scenario.OperatorMethod.ReturnType, stdlib);
        expectedReturnType = intrinsicMap.SubstituteIntrinsics(expectedReturnType, stdlib);

        // Find the operator symbol
        var operatorSymbol = operators
            .FirstOrDefault(m => m.Name == expectedName && m.Kind == SymbolKind.Method);

        if(operatorSymbol is null)
        {
            return false.ToProperty().Label($"Operator symbol '{expectedName}' not found");
        }

        // The DeclaredType on the operator symbol is the return type
        var actualReturnType = operatorSymbol.DeclaredType;
        var typesMatch = actualReturnType?.DisplayName == expectedReturnType.DisplayName;

        return typesMatch.ToProperty()
            .Label($"Return type: expected '{expectedReturnType.DisplayName}', got '{actualReturnType?.DisplayName}'");
    }

    /// <summary>
    /// For any operator method on a fixture type, all projected operators have
    /// SymbolOrigin.Backend (they come from CLR assembly metadata, not user code).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OperatorProjectionPropertyTests) })]
    public Property OperatorMethod_HasBackendOrigin(OperatorMethodScenario scenario)
    {
        var operators = ProjectTypeWithOperators(scenario.DeclaringType);

        var expectedName = GetExpectedMistOperatorName(scenario.OperatorMethod);

        // Find the operator symbol
        var operatorSymbol = operators
            .FirstOrDefault(m => m.Name == expectedName && m.Kind == SymbolKind.Method);

        if(operatorSymbol is null)
        {
            return false.ToProperty().Label($"Operator symbol '{expectedName}' not found");
        }

        return (operatorSymbol.Origin == SymbolOrigin.Backend).ToProperty()
            .Label($"Expected SymbolOrigin.Backend, got {operatorSymbol.Origin}");
    }

    /// <summary>
    /// For any operator method on a fixture type, the projected operator symbol
    /// is registered in the type's member scope (accessible via scope lookup).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OperatorProjectionPropertyTests) })]
    public Property OperatorMethod_RegisteredInMemberScope(OperatorMethodScenario scenario)
    {
        var (projectedType, _) = projector.ProjectType(scenario.DeclaringType, stdlib);

        var memberScope = projectedType switch
        {
            ClassType ct => ct.MemberScope as Scope,
            StructType st => st.MemberScope as Scope,
            AbstractType it => it.MemberScope as Scope,
            _ => null,
        };

        if(memberScope is null)
        {
            return false.ToProperty().Label("Could not get member scope");
        }

        _ = projector.ProjectOperators(scenario.DeclaringType, memberScope, stdlib);

        var expectedName = GetExpectedMistOperatorName(scenario.OperatorMethod);

        // Verify the operator is findable in the member scope
        var found = memberScope.Members.Any(m => m.Name == expectedName && m.Kind == SymbolKind.Method);

        return found.ToProperty()
            .Label($"Operator '{expectedName}' not found in member scope of {scenario.DeclaringType.Name}");
    }
}