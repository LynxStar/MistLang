// Feature: enum-readonly-restriction, Task 7.1: Bootstrap type readonly verification
// **Validates: Requirements 1.1–1.16, 4.5**
//
// Unit tests verifying:
// 1. All 14 bootstrap types have IsReadonly = true after semantic model construction.
// 2. Diagnostic IDs MIST_TC_0102 and MIST_TC_0103 are unique (don't collide with
//    any other diagnostic IDs declared in TypeCheckerAnalyzer).

using System.Collections.Immutable;
using System.Reflection;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for the enum readonly restriction feature, verifying that
/// bootstrap types are correctly marked readonly and that diagnostic IDs
/// are unique.
/// </summary>
public sealed class EnumReadonlyRestrictionTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    /// <summary>
    /// The 14 bootstrap types that must be marked readonly.
    /// </summary>
    private static readonly string[] BootstrapTypeNames =
    [
        "Int32", "Int16", "Int64",
        "UInt16", "UInt32", "UInt64",
        "Byte", "SByte",
        "Boolean", "String", "Char",
        "Single", "Double", "Decimal"
    ];

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up from the test assembly's output directory until a directory
    /// containing Mist.slnx is found.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root (Mist.slnx) from AppContext.BaseDirectory.");
    }

    /// <summary>
    /// Loads the stdlib, lexes, parses, and builds the semantic model.
    /// Returns the semantic model containing all bootstrap types.
    /// </summary>
    private static ISemanticModel BuildStdlibModel()
    {
        var repoRoot = FindRepoRoot();
        var stdlibRoot = Path.Combine(repoRoot, "Source", "Standard");

        var loaderDiagnostics = new DiagnosticBag();
        var units = StdlibLoader.LoadFrom(stdlibRoot, loaderDiagnostics);

        if (loaderDiagnostics.HasErrors)
            throw new InvalidOperationException(
                "Failed to load stdlib: " +
                string.Join("; ", loaderDiagnostics.ToImmutableArray().Select(d => d.Message)));

        var pipelineDiagnostics = new DiagnosticBag();
        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>(units.Length);

        foreach (var unit in units)
        {
            var tokens = Lexer.Lex(unit.Source, pipelineDiagnostics);
            var parseResult = Parser.Parse(tokens, pipelineDiagnostics);
            if (parseResult.Root is not null)
                asts.Add(parseResult.Root);
        }

        var compilation = new MistCompilation(units);
        var model = Builder.Build(
            compilation,
            asts.ToImmutable(),
            ambientTypeResolver: null,
            namespaceRoot: "Standard",
            projectFolder: stdlibRoot,
            pipelineDiagnostics);

        return model;
    }

    /// <summary>
    /// Extracts the IsReadonly value from a symbol's declared type.
    /// </summary>
    private static bool GetIsReadonly(ISymbol symbol)
    {
        return symbol.DeclaredType switch
        {
            StructType st => st.IsReadonly,
            ClassType ct => ct.IsReadonly,
            AbstractType at => at.IsReadonly,
            _ => false,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: All 14 bootstrap types have IsReadonly = true
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Int32")]
    [InlineData("Int16")]
    [InlineData("Int64")]
    [InlineData("UInt16")]
    [InlineData("UInt32")]
    [InlineData("UInt64")]
    [InlineData("Byte")]
    [InlineData("SByte")]
    [InlineData("Boolean")]
    [InlineData("String")]
    [InlineData("Char")]
    [InlineData("Single")]
    [InlineData("Double")]
    [InlineData("Decimal")]
    public void BootstrapType_HasIsReadonlyTrue(string typeName)
    {
        var model = BuildStdlibModel();

        var symbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == typeName &&
                s.Kind is SymbolKind.Struct or SymbolKind.Class);

        Assert.NotNull(symbol);
        Assert.True(
            GetIsReadonly(symbol!),
            $"Bootstrap type '{typeName}' should have IsReadonly = true, but it was false.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: All 14 bootstrap types are present in the semantic model
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllBootstrapTypes_ArePresent_InSemanticModel()
    {
        var model = BuildStdlibModel();

        var missingTypes = new List<string>();
        foreach (var typeName in BootstrapTypeNames)
        {
            var symbol = model.AllSymbols
                .FirstOrDefault(s => s.Name == typeName &&
                    s.Kind is SymbolKind.Struct or SymbolKind.Class);

            if (symbol is null)
                missingTypes.Add(typeName);
        }

        Assert.True(
            missingTypes.Count == 0,
            $"The following bootstrap types were not found in the semantic model: " +
            $"{string.Join(", ", missingTypes)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Diagnostic IDs MIST_TC_0102 and MIST_TC_0103 are unique
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DiagnosticId_MIST_TC_0102_IsUnique()
    {
        var allIds = GetAllTypeCheckerDiagnosticIds();

        var occurrences = allIds
            .Where(kvp => kvp.Value == "MIST_TC_0102")
            .Select(kvp => kvp.Key)
            .ToList();

        Assert.True(
            occurrences.Count == 1,
            $"Expected MIST_TC_0102 to appear exactly once in TypeCheckerAnalyzer, " +
            $"but found {occurrences.Count} occurrences: {string.Join(", ", occurrences)}");
    }

    [Fact]
    public void DiagnosticId_MIST_TC_0103_IsUnique()
    {
        var allIds = GetAllTypeCheckerDiagnosticIds();

        var occurrences = allIds
            .Where(kvp => kvp.Value == "MIST_TC_0103")
            .Select(kvp => kvp.Key)
            .ToList();

        Assert.True(
            occurrences.Count == 1,
            $"Expected MIST_TC_0103 to appear exactly once in TypeCheckerAnalyzer, " +
            $"but found {occurrences.Count} occurrences: {string.Join(", ", occurrences)}");
    }

    [Fact]
    public void DiagnosticIds_MIST_TC_0102_And_0103_DoNotCollideWithOtherIds()
    {
        var allIds = GetAllTypeCheckerDiagnosticIds();

        // Group by value to find any duplicates.
        var duplicates = allIds
            .GroupBy(kvp => kvp.Value)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "Found duplicate diagnostic ID values in TypeCheckerAnalyzer:\n" +
            string.Join("\n", duplicates.Select(g =>
                $"  ID '{g.Key}' is used by: {string.Join(", ", g.Select(kvp => kvp.Key))}")));
    }

    /// <summary>
    /// Uses reflection to extract all static readonly DiagnosticId fields
    /// from TypeCheckerAnalyzer and returns them as (fieldName, idValue) pairs.
    /// </summary>
    private static List<KeyValuePair<string, string>> GetAllTypeCheckerDiagnosticIds()
    {
        var type = typeof(TypeCheckerAnalyzer);
        var fields = type.GetFields(
            BindingFlags.Static | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);

        var result = new List<KeyValuePair<string, string>>();
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(DiagnosticId) && field.IsInitOnly)
            {
                var value = (DiagnosticId)field.GetValue(null)!;
                result.Add(new KeyValuePair<string, string>(field.Name, value.Value));
            }
        }

        return result;
    }
}
