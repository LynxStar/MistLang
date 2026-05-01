using System.Collections.Immutable;

namespace Mist.Semantics.Model;

/// <summary>
/// The single canonical location of the Keyword_Alias_Mapping that binds each
/// Primitive_Keyword to its fully-qualified Standard_Library type name.
///
/// Requirement 7.5 mandates the mapping live in exactly one place; this is
/// that place. Resolution of a Primitive_Keyword (e.g., <c>int</c>) bypasses
/// all lexical, namespace, and using-directive scoping — it always targets
/// the type named on the right-hand side of this map in the
/// <c>Standard</c> namespace.
///
/// See design § 2 and Requirement 7.1 for the full list of aliases.
/// </summary>
public static class KeywordAliases
{
    /// <summary>
    /// Maps each Primitive_Keyword to its fully-qualified Stdlib_Type_Name.
    /// </summary>
    public static readonly ImmutableDictionary<string, string> Map =
        new Dictionary<string, string>
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
        }.ToImmutableDictionary();
}
