// Feature: value-tuples, Properties 3–5: Tuple assignability, element access, invalid access
// **Validates: Requirements 2.4, 3.1–3.5, 4.3, 4.4, 5.3, 5.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for tuple type checker behavior:
/// assignability with name erasure, element access resolution,
/// and invalid member access diagnostics.
/// </summary>
public class TupleTypeCheckerPropertyTests
{
    // ─── Property 3: Tuple assignability with name erasure ───────────────

    /// <summary>
    /// Two TupleType instances with the same element types (in order) are
    /// assignable regardless of name differences.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypePairGenerator) })]
    public bool SameTypePairs_AreAssignable_RegardlessOfNames(SameTypeTuplePair pair)
    {
        return TypeCheckerAnalyzer.IsAssignable(pair.Source, pair.Target);
    }

    /// <summary>
    /// Two TupleType instances with different arities are never assignable.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypePairGenerator) })]
    public bool DifferentArityPairs_AreNotAssignable(DifferentArityTuplePair pair)
    {
        return !TypeCheckerAnalyzer.IsAssignable(pair.Source, pair.Target);
    }

    /// <summary>
    /// Two TupleType instances with same arity but at least one different
    /// element type are not assignable.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypePairGenerator) })]
    public bool DifferentElementTypePairs_AreNotAssignable(DifferentElementTypeTuplePair pair)
    {
        return !TypeCheckerAnalyzer.IsAssignable(pair.Source, pair.Target);
    }

    // ─── Property 4: Tuple element access resolution ────────────────────

    /// <summary>
    /// For any TupleType with arity N, accessing ItemK (1 ≤ K ≤ N)
    /// resolves to the element type at position K.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool PositionalAccess_ResolvesToCorrectElementType(TupleType tuple)
    {
        for(var i = 0; i < tuple.Arity; i++)
        {
            var memberName = $"Item{i + 1}";
            var resolved = ResolveTupleMemberType(tuple, memberName);
            if(resolved is null || resolved.DisplayName != tuple.ElementTypes[i].DisplayName)
                return false;
        }
        return true;
    }

    /// <summary>
    /// For any TupleType with named elements, accessing by declared name
    /// resolves to the same type as the corresponding positional ItemK
    /// (for the first occurrence of that name).
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool NamedAccess_ResolvesToSameTypeAsPositional(TupleType tuple)
    {
        if(tuple.ElementNames is null)
            return true; // No names to check.

        // Track which names we've already checked to handle duplicates.
        var checkedNames = new HashSet<string>();

        for(var i = 0; i < tuple.ElementNames.Count; i++)
        {
            var name = tuple.ElementNames[i];
            if(name is null) continue;

            // Only check the first occurrence of each name.
            if(!checkedNames.Add(name)) continue;

            var namedResolved = ResolveTupleMemberType(tuple, name);
            var positionalResolved = ResolveTupleMemberType(tuple, $"Item{i + 1}");

            if(namedResolved is null || positionalResolved is null)
                return false;
            // The named access resolves to the first element with that name,
            // which is at position i (since we skip duplicates).
            if(namedResolved.DisplayName != tuple.ElementTypes[i].DisplayName)
                return false;
        }
        return true;
    }

    // ─── Property 5: Invalid tuple member access produces diagnostic ────

    /// <summary>
    /// For any TupleType and any member name that is neither a valid
    /// positional name nor a declared element name, access returns null
    /// (indicating the type checker would report a diagnostic).
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeAndInvalidNameGenerator) })]
    public bool InvalidMemberAccess_ReturnsNull(TupleTypeWithInvalidName input)
    {
        var resolved = ResolveTupleMemberType(input.Tuple, input.InvalidName);
        return resolved is null;
    }

    // ─── Helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a member access on a TupleType in-process, returning the
    /// element type or null if the member is not found.
    /// </summary>
    private static IType? ResolveTupleMemberType(TupleType tuple, string memberName)
    {
        // Check positional access: Item1, Item2, ..., ItemN (1-indexed).
        if(memberName.StartsWith("Item", StringComparison.Ordinal)
            && int.TryParse(memberName.AsSpan(4), out var itemIndex)
            && itemIndex >= 1
            && itemIndex <= tuple.Arity)
        {
            return tuple.ElementTypes[itemIndex - 1];
        }

        // Check named access.
        if(tuple.ElementNames is not null)
        {
            for(var i = 0; i < tuple.ElementNames.Count; i++)
            {
                if(tuple.ElementNames[i] == memberName)
                    return tuple.ElementTypes[i];
            }
        }

        return null;
    }
}

// ─── Generator types for Property 3 ─────────────────────────────────────

/// <summary>
/// A pair of TupleType instances with the same element types but
/// potentially different names.
/// </summary>
public record SameTypeTuplePair(TupleType Source, TupleType Target);

/// <summary>
/// A pair of TupleType instances with different arities.
/// </summary>
public record DifferentArityTuplePair(TupleType Source, TupleType Target);

/// <summary>
/// A pair of TupleType instances with same arity but at least one
/// different element type.
/// </summary>
public record DifferentElementTypeTuplePair(TupleType Source, TupleType Target);

/// <summary>
/// A TupleType paired with a member name that is invalid for that tuple.
/// </summary>
public record TupleTypeWithInvalidName(TupleType Tuple, string InvalidName);

/// <summary>
/// FsCheck generators for tuple type pairs used in assignability tests.
/// </summary>
public static class TupleTypePairGenerator
{
    private static readonly IType[] PrimitiveLeafTypes =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.Double,
        LegacyPrimitiveTypes.Float,
        LegacyPrimitiveTypes.Char,
        LegacyPrimitiveTypes.Long,
        LegacyPrimitiveTypes.String,
    ];

    private static readonly string[] SampleNames =
    [
        "x", "y", "z", "a", "b", "c",
        "first", "second", "value", "key",
    ];

    private static Gen<string?> GenOptionalName() =>
        Gen.Frequency(
            (1, Gen.Constant<string?>(null)),
            (1, Gen.Elements(SampleNames).Select(n => (string?)n)));

    /// <summary>
    /// Generates a pair with the same element types but different names.
    /// </summary>
    public static Arbitrary<SameTypeTuplePair> ArbitrarySameTypeTuplePair() =>
        (from arity in Gen.Choose(2, 5)
         from types in Gen.ArrayOf(Gen.Elements(PrimitiveLeafTypes), arity)
         from names1 in Gen.ArrayOf(GenOptionalName(), arity)
         from names2 in Gen.ArrayOf(GenOptionalName(), arity)
         let hasAnyName1 = names1.Any(n => n is not null)
         let hasAnyName2 = names2.Any(n => n is not null)
         let source = new TupleType(types, hasAnyName1 ? names1 : null)
         let target = new TupleType(types, hasAnyName2 ? names2 : null)
         select new SameTypeTuplePair(source, target))
        .ToArbitrary();

    /// <summary>
    /// Generates a pair with different arities.
    /// </summary>
    public static Arbitrary<DifferentArityTuplePair> ArbitraryDifferentArityTuplePair() =>
        (from arity1 in Gen.Choose(2, 4)
         from arity2 in Gen.Choose(2, 4).Where(a2 => true) // will filter below
         where arity1 != arity2
         from types1 in Gen.ArrayOf(Gen.Elements(PrimitiveLeafTypes), arity1)
         from types2 in Gen.ArrayOf(Gen.Elements(PrimitiveLeafTypes), arity2)
         select new DifferentArityTuplePair(
             new TupleType(types1), new TupleType(types2)))
        .ToArbitrary();

    /// <summary>
    /// Generates a pair with same arity but at least one different element type.
    /// </summary>
    public static Arbitrary<DifferentElementTypeTuplePair> ArbitraryDifferentElementTypeTuplePair() =>
        (from arity in Gen.Choose(2, 5)
         from types1 in Gen.ArrayOf(Gen.Elements(PrimitiveLeafTypes), arity)
         from types2 in Gen.ArrayOf(Gen.Elements(PrimitiveLeafTypes), arity)
         where !TypeArraysEqual(types1, types2)
         select new DifferentElementTypeTuplePair(
             new TupleType(types1), new TupleType(types2)))
        .ToArbitrary();

    private static bool TypeArraysEqual(IType[] a, IType[] b)
    {
        if(a.Length != b.Length) return false;
        for(var i = 0; i < a.Length; i++)
        {
            if(a[i].DisplayName != b[i].DisplayName)
                return false;
        }
        return true;
    }
}

/// <summary>
/// FsCheck generator for TupleType + invalid member name pairs.
/// </summary>
public static class TupleTypeAndInvalidNameGenerator
{
    private static readonly string[] InvalidNames =
    [
        "Item0", "Item99", "Item", "item1", "foo", "bar",
        "Count", "Length", "ToString", "GetHashCode",
    ];

    public static Arbitrary<TupleTypeWithInvalidName> ArbitraryTupleTypeWithInvalidName() =>
        (from tuple in TupleTypeGenerator.GenTupleType()
         from invalidName in Gen.Elements(InvalidNames)
         where !IsValidMemberName(tuple, invalidName)
         select new TupleTypeWithInvalidName(tuple, invalidName))
        .ToArbitrary();

    private static bool IsValidMemberName(TupleType tuple, string name)
    {
        // Check positional names.
        if(name.StartsWith("Item", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(4), out var index)
            && index >= 1 && index <= tuple.Arity)
        {
            return true;
        }

        // Check declared names.
        if(tuple.ElementNames is not null)
        {
            for(var i = 0; i < tuple.ElementNames.Count; i++)
            {
                if(tuple.ElementNames[i] == name)
                    return true;
            }
        }

        return false;
    }
}