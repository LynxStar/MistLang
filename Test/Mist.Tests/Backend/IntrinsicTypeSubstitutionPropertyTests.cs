// Feature: assembly-type-provider, Property 4: Intrinsic Type Substitution Round-Trip
// **Validates: Requirements 7.1, 7.4, 7.6**

using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for intrinsic type substitution round-trip.
///
/// **Validates: Requirements 7.1, 7.4, 7.6**
///
/// Property 4: For any entry in the <see cref="CSharpTypeMap.BackingTypes"/> dictionary,
/// reversing the mapping (CLR name → Mist name) and then looking up the CLR type during
/// projection SHALL produce the corresponding Standard Library IType instance. The reversed
/// map SHALL be the single source of truth — no separate mapping table exists.
/// </summary>
public sealed class IntrinsicTypeSubstitutionPropertyTests
{
    private static readonly IntrinsicTypeMap Map = new();
    private static readonly IStdlibTypeProvider Stdlib = FallbackStdlibTypeProvider.Instance;

    /// <summary>
    /// Well-known mapping from C# keywords to their full CLR type names,
    /// mirroring IntrinsicTypeMap.KeywordToClrFullName.
    /// </summary>
    private static readonly Dictionary<string, string> KeywordToClrFullName = new(StringComparer.Ordinal)
    {
        ["int"] = "System.Int32",
        ["uint"] = "System.UInt32",
        ["long"] = "System.Int64",
        ["ulong"] = "System.UInt64",
        ["short"] = "System.Int16",
        ["ushort"] = "System.UInt16",
        ["byte"] = "System.Byte",
        ["sbyte"] = "System.SByte",
        ["float"] = "System.Single",
        ["double"] = "System.Double",
        ["decimal"] = "System.Decimal",
        ["bool"] = "System.Boolean",
        ["char"] = "System.Char",
        ["string"] = "System.String",
        ["void"] = "System.Void",
    };

    /// <summary>
    /// Represents a single BackingTypes entry for property test generation.
    /// Contains the Mist Standard Library name and the C# backing name.
    /// </summary>
    private record BackingTypeEntry(string MistName, string CSharpName);

    /// <summary>
    /// Generates random entries from <see cref="CSharpTypeMap.BackingTypes"/>
    /// that have a "Standard." prefix (the canonical entries).
    /// </summary>
    private static Gen<BackingTypeEntry> GenBackingTypeEntry()
    {
        var canonicalEntries = CSharpTypeMap.BackingTypes
            .Where(kvp => kvp.Key.StartsWith("Standard.", StringComparison.Ordinal))
            .Select(kvp => new BackingTypeEntry(kvp.Key, kvp.Value))
            .ToArray();

        return Gen.Elements(canonicalEntries);
    }

    /// <summary>
    /// Helper to run a property over generated samples without process spawning.
    /// </summary>
    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for(var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch(Exception ex)
            {
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: {samples[idx]}\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    // ── Property 4a: GetMistTypeName round-trip via C# keyword ──────────
    // For any canonical BackingTypes entry whose C# name is a keyword,
    // GetMistTypeName(keyword) returns the correct Standard Library name.

    [Fact]
    public void P4a_GetMistTypeName_CSharpKeyword_ReturnsCorrectMistName()
    {
        var keywordEntries = GenBackingTypeEntry()
            .Where(e => KeywordToClrFullName.ContainsKey(e.CSharpName));

        RunProperty(keywordEntries, entry =>
        {
            var result = Map.GetMistTypeName(entry.CSharpName);
            Assert.Equal(entry.MistName, result);
        });
    }

    // ── Property 4b: GetMistTypeName round-trip via full CLR name ───────
    // For any canonical BackingTypes entry whose C# name is a keyword,
    // GetMistTypeName(fullClrName) also returns the correct Standard Library name.

    [Fact]
    public void P4b_GetMistTypeName_FullClrName_ReturnsCorrectMistName()
    {
        var keywordEntries = GenBackingTypeEntry()
            .Where(e => KeywordToClrFullName.ContainsKey(e.CSharpName));

        RunProperty(keywordEntries, entry =>
        {
            var fullClrName = KeywordToClrFullName[entry.CSharpName];
            var result = Map.GetMistTypeName(fullClrName);
            Assert.Equal(entry.MistName, result);
        });
    }

    // ── Property 4c: GetMistTypeName round-trip via qualified C# name ───
    // For any canonical BackingTypes entry whose C# name is NOT a keyword
    // (e.g., "System.Type", "System.Console"), GetMistTypeName returns the
    // correct Standard Library name.

    [Fact]
    public void P4c_GetMistTypeName_QualifiedCSharpName_ReturnsCorrectMistName()
    {
        var qualifiedEntries = GenBackingTypeEntry()
            .Where(e => !KeywordToClrFullName.ContainsKey(e.CSharpName));

        RunProperty(qualifiedEntries, entry =>
        {
            var result = Map.GetMistTypeName(entry.CSharpName);
            Assert.Equal(entry.MistName, result);
        });
    }

    // ── Property 4d: SubstituteIntrinsics round-trip for struct types ───
    // For any BackingTypes entry that maps to a struct-like CLR type
    // (numeric primitives, bool, char, void), creating a projected StructType
    // with the CLR namespace and name, then calling SubstituteIntrinsics,
    // produces the correct Standard Library IType.

    [Fact]
    public void P4d_SubstituteIntrinsics_StructTypes_ProducesCorrectStdlibType()
    {
        // Struct-like entries: those whose CLR full name starts with "System."
        // and maps to a StructType in the fallback provider.
        var structEntries = GenBackingTypeEntry()
            .Where(e => KeywordToClrFullName.ContainsKey(e.CSharpName)
                        && e.CSharpName != "string"); // string is a ClassType

        RunProperty(structEntries, entry =>
        {
            var fullClrName = KeywordToClrFullName[entry.CSharpName];
            var parts = fullClrName.Split('.');
            var ns = string.Join(".", parts[..^1]);
            var name = parts[^1];

            // Create a projected CLR type (as if read from assembly metadata)
            var projectedType = new StructType(name, new Scope(null), containingNamespace: ns);

            // Substitute intrinsics
            var result = Map.SubstituteIntrinsics(projectedType, Stdlib);

            // The result should be the fallback stdlib type
            var expectedSimpleName = entry.MistName.Split('.')[^1];
            var expectedType = Stdlib.LookupStandardType(expectedSimpleName);
            Assert.Same(expectedType, result);
        });
    }

    // ── Property 4e: SubstituteIntrinsics round-trip for class types ────
    // For BackingTypes entries that map to class-like CLR types (string),
    // creating a projected ClassType and calling SubstituteIntrinsics
    // produces the correct Standard Library IType.

    [Fact]
    public void P4e_SubstituteIntrinsics_ClassTypes_ProducesCorrectStdlibType()
    {
        // "string" maps to System.String which is a ClassType
        var classEntries = GenBackingTypeEntry()
            .Where(e => e.CSharpName == "string");

        RunProperty(classEntries, entry =>
        {
            var fullClrName = KeywordToClrFullName[entry.CSharpName];
            var parts = fullClrName.Split('.');
            var ns = string.Join(".", parts[..^1]);
            var name = parts[^1];

            // Create a projected CLR type (as if read from assembly metadata)
            var projectedType = new ClassType(name, new Scope(null), containingNamespace: ns);

            // Substitute intrinsics
            var result = Map.SubstituteIntrinsics(projectedType, Stdlib);

            // The result should be the fallback stdlib type
            var expectedSimpleName = entry.MistName.Split('.')[^1];
            var expectedType = Stdlib.LookupStandardType(expectedSimpleName);
            Assert.Same(expectedType, result);
        });
    }

    // ── Property 4f: SubstituteIntrinsics for qualified utility types ───
    // For BackingTypes entries with qualified C# names (System.Type,
    // System.Console, etc.), creating a projected ClassType and calling
    // SubstituteIntrinsics produces the correct Standard Library IType
    // (when the fallback provider supports it).

    [Fact]
    public void P4f_SubstituteIntrinsics_QualifiedUtilityTypes_ProducesCorrectStdlibType()
    {
        // Only test entries that the FallbackStdlibTypeProvider supports
        var supportedSimpleNames = new HashSet<string> { "Type" };

        var qualifiedEntries = GenBackingTypeEntry()
            .Where(e => !KeywordToClrFullName.ContainsKey(e.CSharpName)
                        && supportedSimpleNames.Contains(e.MistName.Split('.')[^1]));

        RunProperty(qualifiedEntries, entry =>
        {
            var parts = entry.CSharpName.Split('.');
            var ns = string.Join(".", parts[..^1]);
            var name = parts[^1];

            // Create a projected CLR type (as if read from assembly metadata)
            var projectedType = new ClassType(name, new Scope(null), containingNamespace: ns);

            // Substitute intrinsics
            var result = Map.SubstituteIntrinsics(projectedType, Stdlib);

            // The result should be the fallback stdlib type
            var expectedSimpleName = entry.MistName.Split('.')[^1];
            var expectedType = Stdlib.LookupStandardType(expectedSimpleName);
            Assert.Same(expectedType, result);
        });
    }

    // ── Property 4g: Non-intrinsic types are NOT substituted ────────────
    // For any type name that does NOT appear in the reversed BackingTypes map,
    // SubstituteIntrinsics returns the original type unchanged (Req 7.6).

    [Fact]
    public void P4g_SubstituteIntrinsics_NonIntrinsicTypes_ReturnedUnchanged()
    {
        var nonIntrinsicNames = Gen.Elements(
            ("List", "System.Collections.Generic"),
            ("File", "System.IO"),
            ("HttpClient", "System.Net.Http"),
            ("StringBuilder", "System.Text"),
            ("DateTime", "System"),
            ("Guid", "System"),
            ("TimeSpan", "System"),
            ("Uri", "System"),
            ("Random", "System"),
            ("Environment", "System"));

        RunProperty(nonIntrinsicNames, entry =>
        {
            var (name, ns) = entry;

            // Verify GetMistTypeName returns null for these
            var fullName = $"{ns}.{name}";
            Assert.Null(Map.GetMistTypeName(fullName));

            // Create a projected CLR type
            var projectedType = new ClassType(name, new Scope(null), containingNamespace: ns);

            // SubstituteIntrinsics should return the same instance
            var result = Map.SubstituteIntrinsics(projectedType, Stdlib);
            Assert.Same(projectedType, result);
        });
    }

    // ── Property 4h: Single source of truth — reversed map matches BackingTypes ─
    // For every canonical "Standard.*" entry in BackingTypes, the IntrinsicTypeMap
    // must have a reverse entry. This verifies no entries are lost during reversal.

    [Fact]
    public void P4h_ReversedMap_CoversAllCanonicalBackingTypeEntries()
    {
        var gen = GenBackingTypeEntry();

        RunProperty(gen, entry =>
        {
            // The C# name (keyword or qualified) must resolve back to the Mist name
            var result = Map.GetMistTypeName(entry.CSharpName);
            Assert.NotNull(result);
            Assert.Equal(entry.MistName, result);
        });
    }
}