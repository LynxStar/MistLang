// Feature: stack-only-types, Property 13: Length-erasing emission
// **Validates: Requirements 6.2, 6.3, 7.1, 7.2**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for <see cref="CSharpTypeMap.GetTypeName"/> with
/// <see cref="SpanType"/> instances (Property 13: Length-erasing emission).
///
/// Asserts that all <see cref="SpanType"/> variants (unknown, known, symbolic length)
/// erase the Length type argument and emit <c>System.Span&lt;C&gt;</c> (mutable)
/// or <c>System.ReadOnlySpan&lt;C&gt;</c> (read-only), where <c>C</c> is the
/// Backing_Type_Map CLR name of the element type.
/// </summary>
public class SpanLengthErasingEmissionPropertyTests
{
    /// <summary>
    /// Property 13: For any SpanType, GetTypeName produces the correct
    /// <c>System.Span&lt;C&gt;</c> or <c>System.ReadOnlySpan&lt;C&gt;</c>
    /// string based on IsReadOnly, where C is the CLR name of the element type.
    /// The Length is always erased regardless of its state (unknown, known, symbolic).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SpanTypeGenerator) })]
    public Property SpanType_EmitsCorrectClrTypeName(SpanType span)
    {
        var result = CSharpTypeMap.GetTypeName(span);
        var expectedElement = CSharpTypeMap.GetTypeName(span.ElementType);
        var expected = span.IsReadOnly
            ? $"System.ReadOnlySpan<{expectedElement}>"
            : $"System.Span<{expectedElement}>";

        return (result == expected)
            .ToProperty()
            .Label($"Expected: {expected}, Got: {result} " +
                   $"(element: {span.ElementType.DisplayName}, isReadOnly: {span.IsReadOnly}, " +
                   $"knownLength: {span.KnownLength?.ToString() ?? "null"}, " +
                   $"symbolicId: {span.SymbolicLengthId?.ToString() ?? "null"})");
    }

    /// <summary>
    /// Property 13: For any SpanType, the emitted C# type string never contains
    /// the length — it has exactly one top-level generic type argument (the element type).
    /// This verifies the length-erasure property directly.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SpanTypeGenerator) })]
    public Property SpanType_LengthIsAlwaysErased(SpanType span)
    {
        var result = CSharpTypeMap.GetTypeName(span);

        // The output should be System.Span<element> or System.ReadOnlySpan<element>
        // with exactly one top-level generic argument (no comma at the outermost level).
        var prefix = span.IsReadOnly ? "System.ReadOnlySpan<" : "System.Span<";
        var startsCorrectly = result.StartsWith(prefix);
        var endsCorrectly = result.EndsWith(">");

        // Count angle-bracket depth to find top-level commas
        var innerContent = result[prefix.Length..^1];
        var hasNoTopLevelComma = !HasTopLevelComma(innerContent);

        return (startsCorrectly && endsCorrectly && hasNoTopLevelComma)
            .ToProperty()
            .Label($"Result: '{result}', starts with '{prefix}': {startsCorrectly}, " +
                   $"ends with '>': {endsCorrectly}, no top-level comma: {hasNoTopLevelComma}");
    }

    /// <summary>
    /// Property 13: GetTypeName on SpanType is idempotent — calling it twice
    /// produces the same result.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SpanTypeGenerator) })]
    public bool SpanType_GetTypeName_IsIdempotent(SpanType span)
    {
        var result1 = CSharpTypeMap.GetTypeName(span);
        var result2 = CSharpTypeMap.GetTypeName(span);
        return result1 == result2;
    }

    /// <summary>
    /// Property 13: Mutable SpanType always emits "System.Span&lt;" prefix.
    /// Read-only SpanType always emits "System.ReadOnlySpan&lt;" prefix.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SpanTypeGenerator) })]
    public bool SpanType_PrefixMatchesReadOnlyFlag(SpanType span)
    {
        var result = CSharpTypeMap.GetTypeName(span);
        return span.IsReadOnly
            ? result.StartsWith("System.ReadOnlySpan<")
            : result.StartsWith("System.Span<");
    }

    /// <summary>
    /// Checks whether the given string contains a comma at the top level
    /// (not nested inside angle brackets).
    /// </summary>
    private static bool HasTopLevelComma(string content)
    {
        var depth = 0;
        foreach (var c in content)
        {
            switch (c)
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ',' when depth == 0: return true;
            }
        }
        return false;
    }
}
