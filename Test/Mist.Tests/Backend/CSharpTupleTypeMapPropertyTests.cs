// Feature: value-tuples, Property 6: C# type name mapping for TupleType
// **Validates: Requirements 6.1, 6.2, 8.4**

using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for <see cref="CSharpTypeMap.GetTypeName"/> with
/// <see cref="TupleType"/> instances (Property 6).
/// </summary>
public class CSharpTupleTypeMapPropertyTests
{
    /// <summary>
    /// Property 6: For any TupleType, GetTypeName produces a C# tuple type
    /// string that starts with '(' and ends with ')'.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool GetTypeName_TupleType_IsParenthesized(TupleType tuple)
    {
        var result = CSharpTypeMap.GetTypeName(tuple);
        return result.StartsWith('(') && result.EndsWith(')');
    }

    /// <summary>
    /// Property 6: For any TupleType with all unnamed elements, GetTypeName
    /// produces bare type names separated by commas.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool GetTypeName_UnnamedTuple_ContainsNoColons(TupleType tuple)
    {
        if(tuple.ElementNames is not null && tuple.ElementNames.Any(n => n is not null))
        {
            return true; // Skip named tuples for this property.
        }

        var result = CSharpTypeMap.GetTypeName(tuple);
        // Unnamed tuples should not contain element names (no "type name" pairs).
        // They should just be "(type1, type2, ...)".
        return !result.Contains(':');
    }

    /// <summary>
    /// Property 6: For any TupleType, GetTypeName contains each element
    /// type's C# name as a substring.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool GetTypeName_ContainsAllElementTypeNames(TupleType tuple)
    {
        var result = CSharpTypeMap.GetTypeName(tuple);
        for(var i = 0; i < tuple.Arity; i++)
        {
            var elementTypeName = CSharpTypeMap.GetTypeName(tuple.ElementTypes[i]);
            if(!result.Contains(elementTypeName))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Property 6: For any TupleType with named elements, GetTypeName
    /// contains each declared element name as a substring.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool GetTypeName_NamedTuple_ContainsAllElementNames(TupleType tuple)
    {
        if(tuple.ElementNames is null)
        {
            return true; // No names to check.
        }

        var result = CSharpTypeMap.GetTypeName(tuple);
        foreach(var name in tuple.ElementNames)
        {
            if(name is not null && !result.Contains(name))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Property 6: For any TupleType, GetTypeName is idempotent — calling
    /// it twice produces the same result.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool GetTypeName_IsIdempotent(TupleType tuple)
    {
        var result1 = CSharpTypeMap.GetTypeName(tuple);
        var result2 = CSharpTypeMap.GetTypeName(tuple);
        return result1 == result2;
    }
}