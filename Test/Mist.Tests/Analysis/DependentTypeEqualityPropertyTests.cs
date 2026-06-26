// Feature: dependent-types, Property 4: Dependent Type Equality

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying dependent type equality semantics.
///
/// **Validates: Requirements 8.1, 8.2**
///
/// For any two Constructed_Dependent_Types with the same base generic type,
/// they SHALL be considered equal by the type checker if and only if all their
/// dependent type argument values are identical (using value equality).
///
/// This test exercises the type identity mechanism by constructing
/// <see cref="ConstructedType"/> instances with <see cref="DependentValueArgument"/>
/// values and verifying that:
/// 1. Types with the same dependent values have equal DisplayNames (type identity)
/// 2. Types with different dependent values have different DisplayNames (type distinction)
/// 3. The DependentArgumentsEqual comparison correctly identifies same/different values
///
/// No processes are spawned — all verification is in-process.
/// </summary>
public class DependentTypeEqualityPropertyTests
{
    // ─── Shared type infrastructure ──────────────────────────────────────

    private static readonly IScope EmptyScope = new Scope(null);

    private static readonly ClassType IntType = new("Int32", EmptyScope, null);
    private static readonly ClassType UintType = new("UInt32", EmptyScope, null);
    private static readonly ClassType LongType = new("Int64", EmptyScope, null);
    private static readonly ClassType ShortType = new("Int16", EmptyScope, null);
    private static readonly ClassType ByteType = new("Byte", EmptyScope, null);
    private static readonly ClassType BoolType = new("Boolean", EmptyScope, null);
    private static readonly ClassType CharType = new("Char", EmptyScope, null);

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ClassType MakeGenericClass(string name)
    {
        return new ClassType(name, EmptyScope, null);
    }

    private static ConstructedType MakeConstructedType(
        ClassType genericDef, IType valueType, DependentValueArgument depArg)
    {
        return new ConstructedType(
            genericDef,
            new List<IType> { valueType },
            new List<DependentValueArgument?> { depArg });
    }

    /// <summary>
    /// Tests type equality using the same mechanism as the type checker:
    /// two ConstructedTypes are equal iff they have the same generic definition,
    /// same type arguments, and same dependent value arguments.
    /// This mirrors the TypesEqual logic in TypeCheckerAnalyzer (Requirements 8.1, 8.2).
    /// </summary>
    private static bool AreTypesEqual(ConstructedType a, ConstructedType b)
    {
        // Same generic definition
        if(!ReferenceEquals(a.GenericDefinition, b.GenericDefinition))
        {
            return false;
        }

        // Same type argument count
        if(a.TypeArguments.Count != b.TypeArguments.Count)
        {
            return false;
        }

        // Same type arguments (by display name since we use separate instances)
        for(var i = 0; i < a.TypeArguments.Count; i++)
        {
            if(a.TypeArguments[i].DisplayName != b.TypeArguments[i].DisplayName)
            {
                return false;
            }
        }

        // Same dependent value arguments (Requirements 8.1, 8.2)
        var depsA = a.DependentArguments;
        var depsB = b.DependentArguments;

        if(depsA is null && depsB is null)
        {
            return true;
        }

        if(depsA is null || depsB is null)
        {
            return false;
        }

        if(depsA.Count != depsB.Count)
        {
            return false;
        }

        for(var i = 0; i < depsA.Count; i++)
        {
            var argA = depsA[i];
            var argB = depsB[i];
            if(argA is null && argB is null)
            {
                continue;
            }

            if(argA is null || argB is null)
            {
                return false;
            }

            if(!Equals(argA.Value, argB.Value))
            {
                return false;
            }
        }

        return true;
    }

    // ─── Value generators ────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage", "Channel", "Segment"];

    private record TypedValue(string TypeName, IType Type, object Value, string Display);

    private static Gen<TypedValue> GenIntTypedValue()
    {
        return Gen.Choose(-100, 100).Select(n =>
            new TypedValue("Int32", IntType, n, n.ToString()));
    }

    private static Gen<TypedValue> GenUintTypedValue()
    {
        return Gen.Choose(0, 1000).Select(n =>
            new TypedValue("UInt32", UintType, (uint)n, n.ToString()));
    }

    private static Gen<TypedValue> GenLongTypedValue()
    {
        return Gen.Choose(-1000, 1000).Select(n =>
            new TypedValue("Int64", LongType, (long)n, n.ToString()));
    }

    private static Gen<TypedValue> GenShortTypedValue()
    {
        return Gen.Choose(-100, 100).Select(n =>
            new TypedValue("Int16", ShortType, (short)n, n.ToString()));
    }

    private static Gen<TypedValue> GenByteTypedValue()
    {
        return Gen.Choose(0, 255).Select(n =>
            new TypedValue("Byte", ByteType, (byte)n, n.ToString()));
    }

    private static Gen<TypedValue> GenBoolTypedValue()
    {
        return Gen.Elements(true, false).Select(b =>
            new TypedValue("Boolean", BoolType, b, b.ToString().ToLowerInvariant()));
    }

    private static Gen<TypedValue> GenCharTypedValue()
    {
        return Gen.Elements('a', 'b', 'c', 'x', 'y', 'z', '0', '1', '9').Select(c =>
            new TypedValue("Char", CharType, c, $"'{c}'"));
    }

    private static Gen<TypedValue> GenAnyTypedValue()
    {
        return Gen.OneOf(
            GenIntTypedValue(),
            GenUintTypedValue(),
            GenLongTypedValue(),
            GenShortTypedValue(),
            GenByteTypedValue(),
            GenBoolTypedValue(),
            GenCharTypedValue());
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034
    public record SameValueScenario(
        string ClassName,
        string TypeName,
        object Value,
        string Display)
    {
        public override string ToString() =>
            $"{ClassName}<{Display}> == {ClassName}<{Display}> (type: {TypeName})";
    }

    public record DifferentValueScenario(
        string ClassName,
        string TypeName,
        object ValueA,
        string DisplayA,
        object ValueB,
        string DisplayB)
    {
        public override string ToString() =>
            $"{ClassName}<{DisplayA}> != {ClassName}<{DisplayB}> (type: {TypeName})";
    }
#pragma warning restore CA1034

    // ─── Scenario generators ─────────────────────────────────────────────

    private static Gen<SameValueScenario> GenSameValueScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from tv in GenAnyTypedValue()
               select new SameValueScenario(className, tv.TypeName, tv.Value, tv.Display);
    }

    private static Gen<DifferentValueScenario> GenDifferentIntValueScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from a in Gen.Choose(-100, -1)
               from b in Gen.Choose(1, 100)
               select new DifferentValueScenario(
                   className, "Int32", a, a.ToString(), b, b.ToString());
    }

    private static Gen<DifferentValueScenario> GenDifferentBoolValueScenario()
    {
        return from className in Gen.Elements(ClassNames)
               select new DifferentValueScenario(
                   className, "Boolean", true, "true", false, "false");
    }

    private static Gen<DifferentValueScenario> GenDifferentCharValueScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from chars in Gen.Shuffle(new[] { 'a', 'b', 'c', 'x', 'y', 'z' })
               where chars.Length >= 2 && chars[0] != chars[1]
               select new DifferentValueScenario(
                   className, "Char", chars[0], $"'{chars[0]}'", chars[1], $"'{chars[1]}'");
    }

    private static Gen<DifferentValueScenario> GenDifferentValueScenario()
    {
        return Gen.OneOf(
            GenDifferentIntValueScenario(),
            GenDifferentBoolValueScenario(),
            GenDifferentCharValueScenario());
    }

    public static Arbitrary<SameValueScenario> ArbitrarySameValueScenario() =>
        GenSameValueScenario().ToArbitrary();

    public static Arbitrary<DifferentValueScenario> ArbitraryDifferentValueScenario() =>
        GenDifferentValueScenario().ToArbitrary();

    // ─── Helpers for type resolution ─────────────────────────────────────

    private static IType GetValueType(string typeName) => typeName switch
    {
        "Int32" => IntType,
        "UInt32" => UintType,
        "Int64" => LongType,
        "Int16" => ShortType,
        "Byte" => ByteType,
        "Boolean" => BoolType,
        "Char" => CharType,
        _ => throw new ArgumentException($"Unknown type: {typeName}")
    };

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 4 (same values): For any two constructed dependent types with the
    /// same base generic type and the same dependent value, they SHALL be considered
    /// equal by the type checker.
    ///
    /// Verifies:
    /// - DisplayName identity (the primary type identity mechanism)
    /// - DependentArguments value equality
    /// - Symmetric equality (A==B implies B==A)
    ///
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DependentTypeEqualityPropertyTests) })]
    public bool SameDependent_Values_AreEqual(SameValueScenario scenario)
    {
        var genericDef = MakeGenericClass(scenario.ClassName);
        var valueType = GetValueType(scenario.TypeName);

        var depArgA = new DependentValueArgument(scenario.Value, valueType, scenario.Display);
        var depArgB = new DependentValueArgument(scenario.Value, valueType, scenario.Display);

        var typeA = MakeConstructedType(genericDef, valueType, depArgA);
        var typeB = MakeConstructedType(genericDef, valueType, depArgB);

        // Same base type + same dependent value → types are equal
        var displayNamesEqual = typeA.DisplayName == typeB.DisplayName;
        var typesEqual = AreTypesEqual(typeA, typeB);
        var symmetric = AreTypesEqual(typeB, typeA);

        return displayNamesEqual && typesEqual && symmetric;
    }

    /// <summary>
    /// Property 4 (different values): For any two constructed dependent types with the
    /// same base generic type but different dependent values, they SHALL be considered
    /// distinct by the type checker.
    ///
    /// Verifies:
    /// - DisplayName distinction (different values produce different type identities)
    /// - DependentArguments value inequality
    /// - Symmetric inequality (A!=B implies B!=A)
    ///
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DependentTypeEqualityPropertyTests) })]
    public bool DifferentDependent_Values_AreNotEqual(DifferentValueScenario scenario)
    {
        var genericDef = MakeGenericClass(scenario.ClassName);
        var valueType = GetValueType(scenario.TypeName);

        var depArgA = new DependentValueArgument(scenario.ValueA, valueType, scenario.DisplayA);
        var depArgB = new DependentValueArgument(scenario.ValueB, valueType, scenario.DisplayB);

        var typeA = MakeConstructedType(genericDef, valueType, depArgA);
        var typeB = MakeConstructedType(genericDef, valueType, depArgB);

        // Same base type + different dependent values → types are distinct
        var displayNamesDistinct = typeA.DisplayName != typeB.DisplayName;
        var typesNotEqual = !AreTypesEqual(typeA, typeB);
        var symmetric = !AreTypesEqual(typeB, typeA);

        return displayNamesDistinct && typesNotEqual && symmetric;
    }
}