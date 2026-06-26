// Feature: type-aliases, Property 4: Type resolution transparency
//
// **Validates: Requirements 5.1, 5.2**
//
// For any type alias pointing to a concrete type, using the alias name in a
// variable declaration SHALL resolve to the same IType as using the underlying
// type name directly. The TypeAliasType.UnderlyingType must equal the type
// obtained by declaring a variable with the primitive type directly.
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Describes a generated type alias resolution scenario for property testing.
/// </summary>
public sealed class TypeAliasResolutionScenario
{
    public string AliasName { get; }
    public string UnderlyingTypeName { get; }

    public TypeAliasResolutionScenario(string aliasName, string underlyingTypeName)
    {
        AliasName = aliasName;
        UnderlyingTypeName = underlyingTypeName;
    }

    public override string ToString()
    {
        return $"type {AliasName} = {UnderlyingTypeName};";
    }
}

/// <summary>
/// Property-based tests for type alias resolution transparency.
///
/// **Validates: Requirements 5.1, 5.2**
///
/// For any type alias pointing to a concrete (primitive) type, using the alias
/// name in a variable declaration SHALL resolve to the same IType as using the
/// underlying type name directly.
/// </summary>
public class TypeAliasResolutionPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] AliasNames =
        ["Foo", "Bar", "Baz", "Qux", "MyAlias", "Result", "Value", "Item"];

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double", "long", "char", "byte", "decimal", "short"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<TypeAliasResolutionScenario> GenScenario()
    {
        return from name in Gen.Elements(AliasNames)
               from target in Gen.Elements(PrimitiveTypes)
               select new TypeAliasResolutionScenario(name, target);
    }

    public static Arbitrary<TypeAliasResolutionScenario> ArbitraryTypeAliasResolutionScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a semantic model from the given Mist source text.
    /// Returns null if lex or parse errors occur.
    /// </summary>
    private static ISemanticModel? BuildModelFromSource(string source, out ImmutableArray<Diagnostic> diagnostics)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            diagnostics = lexBag.ToImmutableArray();
            return null;
        }

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
        {
            diagnostics = parseBag.ToImmutableArray();
            return null;
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        diagnostics = semBag.ToImmutableArray();
        return model;
    }

    /// <summary>
    /// Finds the first local variable symbol with the given name in the model.
    /// </summary>
    private static ISymbol? FindLocalVariable(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.LocalVariable);
    }

    /// <summary>
    /// Returns the default literal for a given primitive type name.
    /// </summary>
    private static string GetDefaultLiteral(string typeName)
    {
        return typeName switch
        {
            "int" => "0",
            "string" => "\"hello\"",
            "bool" => "true",
            "float" => "0.0",
            "double" => "0.0",
            "long" => "0",
            "char" => "'a'",
            "byte" => "0",
            "decimal" => "0.0",
            "short" => "0",
            _ => "0",
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Type resolution transparency
    //
    // **Validates: Requirements 5.1, 5.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any type alias pointing to a concrete type, the resolved underlying
    /// type of a variable declared with the alias SHALL have the same DisplayName
    /// and Kind as a variable declared with the underlying type directly.
    ///
    /// This verifies transparent resolution: using an alias is semantically
    /// identical to using the underlying type.
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(TypeAliasResolutionPropertyTests) },
        MaxTest = 100)]
    public Property TypeAlias_ResolvesToSameTypeAsDirectUsage(
        TypeAliasResolutionScenario scenario)
    {
        var literal = GetDefaultLiteral(scenario.UnderlyingTypeName);

        // Program A: declares a type alias and uses it for a variable
        var sourceWithAlias =
            $"type {scenario.AliasName} = {scenario.UnderlyingTypeName};\n" +
            "class Program {\n" +
            "    static void Main() {\n" +
            $"        {scenario.AliasName} aliasVar = {literal};\n" +
            "    }\n" +
            "}\n";

        // Program B: uses the underlying type directly for a variable
        var sourceWithDirect =
            "class Program {\n" +
            "    static void Main() {\n" +
            $"        {scenario.UnderlyingTypeName} directVar = {literal};\n" +
            "    }\n" +
            "}\n";

        // Build semantic models for both programs
        var modelA = BuildModelFromSource(sourceWithAlias, out var diagsA);
        if(modelA is null)
        {
            return false.ToProperty()
                .Label($"Failed to build model for alias program. Diagnostics: " +
                       $"{string.Join("; ", diagsA.Select(d => d.Message))}");
        }

        var modelB = BuildModelFromSource(sourceWithDirect, out var diagsB);
        if(modelB is null)
        {
            return false.ToProperty()
                .Label($"Failed to build model for direct program. Diagnostics: " +
                       $"{string.Join("; ", diagsB.Select(d => d.Message))}");
        }

        // Find the variable symbols
        var aliasVarSymbol = FindLocalVariable(modelA, "aliasVar");
        if(aliasVarSymbol is null)
        {
            return false.ToProperty()
                .Label($"Variable 'aliasVar' not found in alias program model. " +
                       $"All symbols: [{string.Join(", ", modelA.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        var directVarSymbol = FindLocalVariable(modelB, "directVar");
        if(directVarSymbol is null)
        {
            return false.ToProperty()
                .Label($"Variable 'directVar' not found in direct program model. " +
                       $"All symbols: [{string.Join(", ", modelB.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        // Get the declared types
        var aliasVarType = aliasVarSymbol.DeclaredType;
        var directVarType = directVarSymbol.DeclaredType;

        if(aliasVarType is null)
        {
            return false.ToProperty()
                .Label($"Variable 'aliasVar' has null DeclaredType. Scenario: {scenario}");
        }

        if(directVarType is null)
        {
            return false.ToProperty()
                .Label($"Variable 'directVar' has null DeclaredType. Scenario: {scenario}");
        }

        // The alias variable's type should be a TypeAliasType whose
        // UnderlyingType matches the direct variable's type.
        // Alternatively, if the type checker has already resolved the alias
        // transparently, the types should match directly.
        IType resolvedAliasType;
        if(aliasVarType is TypeAliasType aliasType)
        {
            resolvedAliasType = aliasType.UnderlyingType;
        }
        else
        {
            // The type was already resolved transparently
            resolvedAliasType = aliasVarType;
        }

        // Compare by DisplayName and Kind — these are the semantic identity
        // of a type in the Mist type system.
        var displayNameMatch = resolvedAliasType.DisplayName == directVarType.DisplayName;
        var kindMatch = resolvedAliasType.Kind == directVarType.Kind;

        if(!displayNameMatch || !kindMatch)
        {
            return false.ToProperty()
                .Label($"Type mismatch. Alias resolved to: {resolvedAliasType.DisplayName} (Kind={resolvedAliasType.Kind}), " +
                       $"Direct type: {directVarType.DisplayName} (Kind={directVarType.Kind}). " +
                       $"Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Type alias '{scenario.AliasName}' transparently resolves to '{scenario.UnderlyingTypeName}'");
    }
}