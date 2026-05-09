using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for <see cref="KeywordAliases"/>.
///
/// _Requirements: 7.1, 7.5_
/// </summary>
public class KeywordAliasesTests
{
    [Fact]
    public void Map_ContainsExactlySixteenEntries()
    {
        Assert.Equal(16, KeywordAliases.Map.Count);
    }

    [Fact]
    public void Map_ContainsAllAliasesFromRequirement7Point1()
    {
        // Requirement 7.1 + 1.2: the Keyword_Alias_Mapping is exactly these 16 pairs.
        var expected = new Dictionary<string, string>
        {
            ["int"]     = "Standard.Int32",
            ["uint"]    = "Standard.UInt32",
            ["long"]    = "Standard.Int64",
            ["ulong"]   = "Standard.UInt64",
            ["short"]   = "Standard.Int16",
            ["ushort"]  = "Standard.UInt16",
            ["byte"]    = "Standard.Byte",
            ["sbyte"]   = "Standard.SByte",
            ["float"]   = "Standard.Single",
            ["double"]  = "Standard.Double",
            ["decimal"] = "Standard.Decimal",
            ["bool"]    = "Standard.Boolean",
            ["char"]    = "Standard.Char",
            ["string"]  = "Standard.String",
            ["regex"]   = "Standard.Regex",
            ["void"]    = "Standard.Void",
        };

        Assert.Equal(expected.Count, KeywordAliases.Map.Count);
        foreach (var kvp in expected)
        {
            Assert.True(
                KeywordAliases.Map.TryGetValue(kvp.Key, out var actual),
                $"Expected alias for keyword '{kvp.Key}' was missing from KeywordAliases.Map.");
            Assert.Equal(kvp.Value, actual);
        }
    }
}
