// Feature: assembly-type-provider, Property 5: Recursive Generic Argument Substitution
// **Validates: Requirements 7.5, 10.7**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// A scenario describing a nested generic type construction with intrinsic type arguments.
/// The generator produces random nesting depths and intrinsic type placements.
/// </summary>
public record RecursiveSubstitutionScenario(
    ConstructedType InputType,
    string Description);

/// <summary>
/// Property-based tests for recursive generic argument substitution in
/// <see cref="IntrinsicTypeMap.SubstituteIntrinsics"/>.
///
/// Property 5: Recursive Generic Argument Substitution
/// For any constructed generic CLR type whose type arguments include intrinsic types
/// at any nesting depth, SubstituteIntrinsics SHALL recursively substitute the
/// corresponding Standard Library types at every position. The substitution depth
/// is unbounded (e.g., Dictionary&lt;string, List&lt;int&gt;&gt; →
/// Dictionary&lt;Standard.String, List&lt;Standard.Int32&gt;&gt;).
///
/// **Validates: Requirements 7.5, 10.7**
/// </summary>
public class IntrinsicTypeMapRecursiveSubstitutionPropertyTests
{
    private static readonly IntrinsicTypeMap Map = new();
    private static readonly IStdlibTypeProvider Stdlib = FallbackStdlibTypeProvider.Instance;

    // ── Intrinsic CLR types that should be substituted ───────────────────

    private static readonly (string DisplayName, string Namespace, bool IsClass, string ExpectedDisplayName)[] IntrinsicTypes =
    [
        ("Int32", "System", false, "int"),
        ("UInt32", "System", false, "uint"),
        ("Int64", "System", false, "long"),
        ("UInt64", "System", false, "ulong"),
        ("Int16", "System", false, "short"),
        ("UInt16", "System", false, "ushort"),
        ("Byte", "System", false, "byte"),
        ("SByte", "System", false, "sbyte"),
        ("Single", "System", false, "float"),
        ("Double", "System", false, "double"),
        ("Decimal", "System", false, "decimal"),
        ("Boolean", "System", false, "bool"),
        ("Char", "System", false, "char"),
        ("String", "System", true, "string"),
        ("Void", "System", false, "void"),
    ];

    // ── Non-intrinsic generic type definitions (containers) ──────────────

    private static readonly (string DisplayName, string Namespace, int Arity)[] GenericContainers =
    [
        ("List", "System.Collections.Generic", 1),
        ("Dictionary", "System.Collections.Generic", 2),
        ("HashSet", "System.Collections.Generic", 1),
        ("Queue", "System.Collections.Generic", 1),
        ("Stack", "System.Collections.Generic", 1),
        ("KeyValuePair", "System.Collections.Generic", 2),
        ("Tuple", "System", 2),
    ];

    // ── Non-intrinsic leaf types (should pass through unchanged) ─────────

    private static readonly (string DisplayName, string Namespace)[] NonIntrinsicLeaves =
    [
        ("MyService", "MyApp"),
        ("Widget", "UI.Controls"),
        ("Connection", "Data.Sql"),
        ("Logger", "Diagnostics"),
    ];

    // ── Helper factories ─────────────────────────────────────────────────

    private static IType MakeIntrinsicType(int index)
    {
        var (displayName, ns, isClass, _) = IntrinsicTypes[index];
        return isClass
            ? new ClassType(displayName, new Scope(null), containingNamespace: ns)
            : new StructType(displayName, new Scope(null), containingNamespace: ns);
    }

    private static IType MakeNonIntrinsicLeaf(int index)
    {
        var (displayName, ns) = NonIntrinsicLeaves[index];
        return new ClassType(displayName, new Scope(null), containingNamespace: ns);
    }

    private static ClassType MakeGenericDefinition(int containerIndex)
    {
        var (displayName, ns, arity) = GenericContainers[containerIndex];
        var scope = new Scope(null);
        var cls = new ClassType(displayName, scope, containingNamespace: ns);
        var typeParams = new TypeParameterType[arity];
        for(var i = 0; i < arity; i++)
        {
            typeParams[i] = new TypeParameterType($"T{i}", i);
        }

        cls.TypeParameters = typeParams;
        return cls;
    }

    // ── FsCheck Arbitrary ────────────────────────────────────────────────

    /// <summary>
    /// Generates random nested generic type constructions with intrinsic arguments
    /// at various nesting depths (1-4 levels deep).
    /// </summary>
    public static Arbitrary<RecursiveSubstitutionScenario> ArbitraryRecursiveSubstitutionScenario()
    {
        var genLeaf = Gen.OneOf(
            Gen.Choose(0, IntrinsicTypes.Length - 1).Select(i => (Type: MakeIntrinsicType(i), IsIntrinsic: true)),
            Gen.Choose(0, NonIntrinsicLeaves.Length - 1).Select(i => (Type: MakeNonIntrinsicLeaf(i), IsIntrinsic: false))
        );

        // Generate a type argument (either a leaf or a nested generic)
        Gen<IType> GenTypeArg(int maxDepth)
        {
            if(maxDepth <= 0)
            {
                return genLeaf.Select(l => l.Type);
            }

            return Gen.OneOf(
                genLeaf.Select(l => l.Type),
                GenConstructedType(maxDepth - 1)
            );
        }

        // Generate a constructed generic type at a given depth
        Gen<IType> GenConstructedType(int maxDepth)
        {
            return Gen.Choose(0, GenericContainers.Length - 1).SelectMany(containerIdx =>
            {
                var (_, _, arity) = GenericContainers[containerIdx];
                var argGens = Enumerable.Range(0, arity)
                    .Select(_ => GenTypeArg(maxDepth))
                    .ToArray();

                return GenCombine(argGens).Select(args =>
                {
                    var def = MakeGenericDefinition(containerIdx);
                    return (IType)new ConstructedType(def, args);
                });
            });
        }

        // Top-level: generate a constructed type with depth 1-3 (ensuring at least one intrinsic somewhere)
        var genScenario =
            from depth in Gen.Choose(1, 3)
            from containerIdx in Gen.Choose(0, GenericContainers.Length - 1)
            let container = GenericContainers[containerIdx]
            from args in GenArgsWithAtLeastOneIntrinsic(container.Arity, depth)
            let def = MakeGenericDefinition(containerIdx)
            let constructed = new ConstructedType(def, args)
            select new RecursiveSubstitutionScenario(
                constructed,
                $"{container.DisplayName}<...> depth={depth}");

        return genScenario.ToArbitrary();
    }

    /// <summary>
    /// Generates type arguments ensuring at least one intrinsic type appears somewhere
    /// in the tree (so the property is meaningful — substitution actually occurs).
    /// </summary>
    private static Gen<IType[]> GenArgsWithAtLeastOneIntrinsic(int arity, int maxDepth)
    {
        // Generate one guaranteed intrinsic position
        return Gen.Choose(0, arity - 1).SelectMany(intrinsicPos =>
        {
            var argGens = new Gen<IType>[arity];
            for(var i = 0; i < arity; i++)
            {
                if(i == intrinsicPos)
                {
                    // This position must contain an intrinsic (possibly nested)
                    argGens[i] = GenTypeArgWithIntrinsic(maxDepth);
                }
                else
                {
                    // Other positions can be anything
                    argGens[i] = GenAnyTypeArg(maxDepth);
                }
            }

            return GenCombine(argGens);
        });
    }

    /// <summary>
    /// Generates a type argument that is guaranteed to contain at least one intrinsic type.
    /// </summary>
    private static Gen<IType> GenTypeArgWithIntrinsic(int maxDepth)
    {
        if(maxDepth <= 0)
        {
            // At leaf level, just pick an intrinsic
            return Gen.Choose(0, IntrinsicTypes.Length - 1).Select(i => MakeIntrinsicType(i));
        }

        return Gen.OneOf(
            // Direct intrinsic leaf
            Gen.Choose(0, IntrinsicTypes.Length - 1).Select(i => MakeIntrinsicType(i)),
            // Nested generic containing an intrinsic
            Gen.Choose(0, GenericContainers.Length - 1).SelectMany(containerIdx =>
            {
                var (_, _, innerArity) = GenericContainers[containerIdx];
                return GenArgsWithAtLeastOneIntrinsic(innerArity, maxDepth - 1).Select(args =>
                {
                    var def = MakeGenericDefinition(containerIdx);
                    return (IType)new ConstructedType(def, args);
                });
            })
        );
    }

    /// <summary>
    /// Generates any type argument (intrinsic, non-intrinsic, or nested generic).
    /// </summary>
    private static Gen<IType> GenAnyTypeArg(int maxDepth)
    {
        if(maxDepth <= 0)
        {
            return Gen.OneOf(
                Gen.Choose(0, IntrinsicTypes.Length - 1).Select(i => MakeIntrinsicType(i)),
                Gen.Choose(0, NonIntrinsicLeaves.Length - 1).Select(i => MakeNonIntrinsicLeaf(i))
            );
        }

        return Gen.OneOf(
            Gen.Choose(0, IntrinsicTypes.Length - 1).Select(i => MakeIntrinsicType(i)),
            Gen.Choose(0, NonIntrinsicLeaves.Length - 1).Select(i => MakeNonIntrinsicLeaf(i)),
            Gen.Choose(0, GenericContainers.Length - 1).SelectMany(containerIdx =>
            {
                var (_, _, innerArity) = GenericContainers[containerIdx];
                var argGens = Enumerable.Range(0, innerArity)
                    .Select(_ => GenAnyTypeArg(maxDepth - 1))
                    .ToArray();
                return GenCombine(argGens).Select(args =>
                {
                    var def = MakeGenericDefinition(containerIdx);
                    return (IType)new ConstructedType(def, args);
                });
            })
        );
    }

    /// <summary>
    /// Combines multiple Gen&lt;IType&gt; into a Gen&lt;IType[]&gt;.
    /// </summary>
    private static Gen<IType[]> GenCombine(Gen<IType>[] gens)
    {
        if(gens.Length == 1)
        {
            return gens[0].Select(t => new[] { t });
        }

        if(gens.Length == 2)
        {
            return gens[0].SelectMany(t0 => gens[1].Select(t1 => new[] { t0, t1 }));
        }

        // General case: fold
        var acc = gens[0].Select(t => new[] { t });
        for(var i = 1; i < gens.Length; i++)
        {
            var idx = i;
            acc = acc.SelectMany(arr => gens[idx].Select(t => arr.Append(t).ToArray()));
        }

        return acc;
    }

    // ── Property Test ────────────────────────────────────────────────────

    /// <summary>
    /// Property 5: For any nested generic construction with intrinsic type arguments,
    /// SubstituteIntrinsics recursively substitutes at ALL levels. After substitution,
    /// no intrinsic CLR type remains anywhere in the type tree.
    ///
    /// **Validates: Requirements 7.5, 10.7**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(IntrinsicTypeMapRecursiveSubstitutionPropertyTests) })]
    public Property RecursiveSubstitution_AllIntrinsicsReplacedAtEveryLevel(
        RecursiveSubstitutionScenario scenario)
    {
        var result = Map.SubstituteIntrinsics(scenario.InputType, Stdlib);

        // The result must be a ConstructedType (generic structure is preserved)
        var isConstructed = result is ConstructedType;

        // Verify no intrinsic CLR types remain anywhere in the result tree
        var noIntrinsicsRemain = !ContainsUnsubstitutedIntrinsic(result);

        // Verify that all intrinsic types in the input were actually substituted
        // (the result differs from input when intrinsics were present)
        var inputHadIntrinsics = ContainsUnsubstitutedIntrinsic(scenario.InputType);
        var wasSubstituted = !ReferenceEquals(result, scenario.InputType);

        return (isConstructed && noIntrinsicsRemain && (!inputHadIntrinsics || wasSubstituted))
            .Label($"isConstructed={isConstructed}, noIntrinsicsRemain={noIntrinsicsRemain}, " +
                   $"inputHadIntrinsics={inputHadIntrinsics}, wasSubstituted={wasSubstituted}, " +
                   $"scenario={scenario.Description}");
    }

    // ── Verification helpers ─────────────────────────────────────────────

    /// <summary>
    /// Recursively checks whether any type in the tree is an unsubstituted intrinsic
    /// (i.e., a CLR type that should have been replaced by a Standard Library type).
    /// </summary>
    private static bool ContainsUnsubstitutedIntrinsic(IType type)
    {
        return type switch
        {
            ConstructedType ct =>
                ContainsUnsubstitutedIntrinsic(ct.GenericDefinition) ||
                ct.TypeArguments.Any(ContainsUnsubstitutedIntrinsic),

            StructType st when st.ContainingNamespace == "Standard" => false, // Already stdlib
            ClassType ct when ct.ContainingNamespace == "Standard" => false,  // Already stdlib

            StructType st => IsIntrinsicClrType(st.DisplayName, st.ContainingNamespace),
            ClassType ct => IsIntrinsicClrType(ct.DisplayName, ct.ContainingNamespace),
            AbstractType it => IsIntrinsicClrType(it.DisplayName, it.ContainingNamespace),

            _ => false,
        };
    }

    /// <summary>
    /// Checks if a type with the given display name and namespace is a known intrinsic
    /// that should be substituted.
    /// </summary>
    private static bool IsIntrinsicClrType(string displayName, string? ns)
    {
        if(ns is null)
        {
            return false;
        }

        var qualifiedName = $"{ns}.{displayName}";
        return Map.GetMistTypeName(qualifiedName) is not null;
    }
}