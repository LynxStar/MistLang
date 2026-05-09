// Feature: enum-readonly-restriction, Property 1: Readonly modifier propagation
//
// **Validates: Requirements 1.15**
//
// For any type declaration (struct, class, interface, or trait) that includes
// the `readonly` modifier, the semantic model SHALL set `IsReadonly = true` on
// the resulting type, and for any type declaration without the `readonly`
// modifier, `IsReadonly` SHALL be `false`.
//
// Uses in-process compilation via SemanticModelBuilder — no process spawning.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Represents a single type declaration scenario for testing readonly propagation.
/// </summary>
public sealed class ReadonlyPropagationScenario
{
    public string TypeName { get; }
    public string TypeKind { get; }
    public bool HasReadonlyModifier { get; }
    public string Source { get; }

    public ReadonlyPropagationScenario(
        string typeName,
        string typeKind,
        bool hasReadonlyModifier,
        string source)
    {
        TypeName = typeName;
        TypeKind = typeKind;
        HasReadonlyModifier = hasReadonlyModifier;
        Source = source;
    }

    public override string ToString() =>
        $"{(HasReadonlyModifier ? "readonly " : "")}{TypeKind} {TypeName}";
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// FsCheck generators for readonly modifier propagation scenarios.
/// </summary>
public static class ReadonlyPropagationGenerators
{
    private static readonly string[] StructNames =
        ["Point", "Vector", "Cell", "Coord", "Pair"];

    private static readonly string[] ClassNames =
        ["Widget", "Service", "Handler", "Manager", "Store"];

    private static readonly string[] InterfaceNames =
        ["IReadable", "IService", "IStore", "IHandler", "IProcessor"];

    private static readonly string[] TraitNames =
        ["Printable", "Hashable", "Sortable", "Comparable", "Serializable"];

    private static readonly string[] AccessModifiers = ["", "public "];

    /// <summary>Generates a struct declaration with or without readonly.</summary>
    public static Gen<ReadonlyPropagationScenario> GenStructScenario()
    {
        return from name in Gen.Elements(StructNames)
               from access in Gen.Elements(AccessModifiers)
               from isReadonly in Gen.Elements(true, false)
               let readonlyMod = isReadonly ? "readonly " : ""
               let source = $"{access}{readonlyMod}struct {name} {{\n}}"
               select new ReadonlyPropagationScenario(name, "struct", isReadonly, source);
    }

    /// <summary>Generates a class declaration with or without readonly.</summary>
    public static Gen<ReadonlyPropagationScenario> GenClassScenario()
    {
        return from name in Gen.Elements(ClassNames)
               from access in Gen.Elements(AccessModifiers)
               from isReadonly in Gen.Elements(true, false)
               let readonlyMod = isReadonly ? "readonly " : ""
               let source = $"{access}{readonlyMod}class {name} {{\n}}"
               select new ReadonlyPropagationScenario(name, "class", isReadonly, source);
    }

    /// <summary>Generates an interface declaration with or without readonly.</summary>
    public static Gen<ReadonlyPropagationScenario> GenInterfaceScenario()
    {
        return from name in Gen.Elements(InterfaceNames)
               from access in Gen.Elements(AccessModifiers)
               from isReadonly in Gen.Elements(true, false)
               let readonlyMod = isReadonly ? "readonly " : ""
               let source = $"{access}{readonlyMod}interface {name} {{\n}}"
               select new ReadonlyPropagationScenario(name, "interface", isReadonly, source);
    }

    /// <summary>Generates a trait declaration with or without readonly.</summary>
    public static Gen<ReadonlyPropagationScenario> GenTraitScenario()
    {
        return from name in Gen.Elements(TraitNames)
               from isReadonly in Gen.Elements(true, false)
               let readonlyMod = isReadonly ? "readonly " : ""
               let source = $"{readonlyMod}trait {name} {{\n}}"
               select new ReadonlyPropagationScenario(name, "trait", isReadonly, source);
    }

    /// <summary>Combined generator for all type declaration scenarios.</summary>
    public static Gen<ReadonlyPropagationScenario> GenScenario()
    {
        return Gen.OneOf(
            GenStructScenario(),
            GenClassScenario(),
            GenInterfaceScenario(),
            GenTraitScenario());
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for readonly modifier propagation to the semantic model.
///
/// Property 1: For any type declaration (struct, class, interface, or trait) that
/// includes the `readonly` modifier, the semantic model SHALL set `IsReadonly = true`
/// on the resulting type, and for any type declaration without the `readonly`
/// modifier, `IsReadonly` SHALL be `false`.
///
/// **Validates: Requirements 1.15**
/// </summary>
public class ReadonlyModifierPropagationPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<ReadonlyPropagationScenario> ArbitraryScenario() =>
        ReadonlyPropagationGenerators.GenScenario().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses and builds the semantic model for the given source in-process.
    /// Returns the semantic model.
    /// </summary>
    private static ISemanticModel BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if (parseResult.Root is null)
            throw new InvalidOperationException(
                $"Parse failed for source:\n{source}\nErrors: {string.Join("; ", parseBag.ToImmutableArray().Select(d => d.Message))}");

        var buildBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildBag);

        return model;
    }

    /// <summary>
    /// Extracts the IsReadonly value from the type declared by the symbol with the given name.
    /// </summary>
    private static bool? GetIsReadonly(ISemanticModel model, string typeName)
    {
        var symbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == typeName &&
                s.Kind is SymbolKind.Class or SymbolKind.Struct
                    or SymbolKind.Interface or SymbolKind.Trait);

        if (symbol?.DeclaredType is null)
            return null;

        return symbol.DeclaredType switch
        {
            StructType st => st.IsReadonly,
            ClassType ct => ct.IsReadonly,
            InterfaceType it => it.IsReadonly,
            TraitType tt => tt.IsReadonly,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Readonly modifier propagation
    //
    // **Validates: Requirements 1.15**
    //
    // For any type declaration (struct, class, interface, or trait) that
    // includes the `readonly` modifier, the semantic model SHALL set
    // `IsReadonly = true` on the resulting type, and for any type declaration
    // without the `readonly` modifier, `IsReadonly` SHALL be `false`.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReadonlyModifierPropagationPropertyTests) })]
    public Property ReadonlyModifier_PropagatesCorrectly_ToSemanticModel(
        ReadonlyPropagationScenario scenario)
    {
        var model = BuildModel(scenario.Source);
        var isReadonly = GetIsReadonly(model, scenario.TypeName);

        if (isReadonly is null)
            return false.ToProperty()
                .Label($"Could not find type '{scenario.TypeName}' in semantic model.\nSource:\n{scenario.Source}");

        var expected = scenario.HasReadonlyModifier;
        var actual = isReadonly.Value;

        return (actual == expected).ToProperty()
            .Label($"Expected IsReadonly={expected} for {scenario}, got IsReadonly={actual}.\nSource:\n{scenario.Source}");
    }
}
