// Feature: readonly-contexts, Property 1: Readonly Modifier Parsing Preservation
// Feature: readonly-contexts, Property 2: Readonly Block Parsing
// Feature: readonly-contexts, Property 19: Round-Trip Parsing

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.ReadonlyContexts;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// A generated Mist declaration with a readonly modifier for property testing.
/// </summary>
public sealed class ReadonlyModifierScenario
{
    public string Source { get; }
    public string DeclarationKind { get; }
    public string Description { get; }

    public ReadonlyModifierScenario(string source, string declarationKind, string description)
    {
        Source = source;
        DeclarationKind = declarationKind;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// A generated Mist readonly block statement for property testing.
/// </summary>
public sealed class ReadonlyBlockScenario
{
    public string Source { get; }
    public int ExpectedStatementCount { get; }
    public string Description { get; }

    public ReadonlyBlockScenario(string source, int expectedStatementCount, string description)
    {
        Source = source;
        ExpectedStatementCount = expectedStatementCount;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// A generated Mist readonly syntax construct for round-trip testing.
/// </summary>
public sealed class ReadonlyRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public ReadonlyRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// FsCheck generators for readonly syntax constructs.
/// </summary>
public static class ReadonlyParsingGenerators
{
    // ─── Name pools ─────────────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Foo", "Bar", "Baz", "Widget"];
    private static readonly string[] StructNames = ["Point", "Vector", "Cell"];
    private static readonly string[] InterfaceNames = ["IReadable", "IService", "IStore"];
    private static readonly string[] TraitNames = ["Printable", "Hashable", "Sortable"];
    private static readonly string[] FieldNames = ["x", "y", "count", "name"];
    private static readonly string[] MethodNames = ["Get", "Compute", "Process"];
    private static readonly string[] PropertyNames = ["Value", "Name", "Count"];
    private static readonly string[] ParamNames = ["item", "value", "data"];
    private static readonly string[] Types = ["int", "string", "bool", "float"];
    private static readonly string[] AccessModifiers = ["", "public ", "private "];

    // ─── Modifier combination generators ────────────────────────────────

    /// <summary>Generates a modifier string that always includes readonly.</summary>
    private static Gen<string> GenModifiersWithReadonly()
    {
        return Gen.Elements(AccessModifiers).Select(access => $"{access}readonly ");
    }

    // ─── Property 1: Readonly Modifier Parsing Preservation ─────────────

    /// <summary>Generates a readonly field declaration.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyField()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from fieldType in Gen.Elements(Types)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{fieldType} {fieldName};\n}}"
               select new ReadonlyModifierScenario(source, "field", $"readonly field: {className}.{fieldName}");
    }

    /// <summary>Generates a readonly property declaration.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyProperty()
    {
        return from className in Gen.Elements(ClassNames)
               from propName in Gen.Elements(PropertyNames)
               from propType in Gen.Elements(Types)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{propType} {propName} => 42;\n}}"
               select new ReadonlyModifierScenario(source, "property", $"readonly property: {className}.{propName}");
    }

    /// <summary>Generates a readonly method declaration.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyMethod()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(Types)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{returnType} {methodName}() {{\n        return 0;\n    }}\n}}"
               select new ReadonlyModifierScenario(source, "method", $"readonly method: {className}.{methodName}");
    }

    /// <summary>Generates a readonly constructor declaration.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyConstructor()
    {
        return from className in Gen.Elements(ClassNames)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{className}() {{\n    }}\n}}"
               select new ReadonlyModifierScenario(source, "constructor", $"readonly constructor: {className}");
    }

    /// <summary>Generates a readonly class declaration.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyClass()
    {
        return from className in Gen.Elements(ClassNames)
               from access in Gen.Elements(AccessModifiers)
               let source = $"{access}readonly class {className} {{\n}}"
               select new ReadonlyModifierScenario(source, "class", $"readonly class: {className}");
    }

    /// <summary>Generates a readonly struct declaration.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyStruct()
    {
        return from structName in Gen.Elements(StructNames)
               from access in Gen.Elements(AccessModifiers)
               let source = $"{access}readonly struct {structName} {{\n}}"
               select new ReadonlyModifierScenario(source, "struct", $"readonly struct: {structName}");
    }

    /// <summary>Generates a readonly interface declaration.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyInterface()
    {
        return from ifaceName in Gen.Elements(InterfaceNames)
               from access in Gen.Elements(AccessModifiers)
               let source = $"{access}readonly abstract {ifaceName} {{\n}}"
               select new ReadonlyModifierScenario(source, "interface", $"readonly interface: {ifaceName}");
    }

    /// <summary>Generates a readonly abstract declaration.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyTrait()
    {
        return from traitName in Gen.Elements(TraitNames)
               let source = $"readonly abstract {traitName} {{\n}}"
               select new ReadonlyModifierScenario(source, "trait", $"readonly trait: {traitName}");
    }

    /// <summary>Generates a readonly indexer declaration.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyIndexer()
    {
        return from className in Gen.Elements(ClassNames)
               from indexType in Gen.Elements(Types)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{indexType} this[int i] {{\n        get {{\n            return 0;\n        }}\n    }}\n}}"
               select new ReadonlyModifierScenario(source, "indexer", $"readonly indexer: {className}");
    }

    /// <summary>Combined generator for all readonly modifier scenarios.</summary>
    public static Gen<ReadonlyModifierScenario> GenReadonlyModifierScenario()
    {
        return Gen.OneOf(
            GenReadonlyField(),
            GenReadonlyProperty(),
            GenReadonlyMethod(),
            GenReadonlyConstructor(),
            GenReadonlyClass(),
            GenReadonlyStruct(),
            GenReadonlyInterface(),
            GenReadonlyTrait(),
            GenReadonlyIndexer());
    }

    // ─── Property 2: Readonly Block Parsing ─────────────────────────────

    /// <summary>Generates a readonly block with various statement counts.</summary>
    public static Gen<ReadonlyBlockScenario> GenReadonlyBlockScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from stmtCount in Gen.Choose(0, 3)
               let stmts = Enumerable.Range(0, stmtCount)
                   .Select(i => $"        var v{i} = {i};")
                   .ToArray()
               let stmtBlock = stmts.Length > 0 ? "\n" + string.Join("\n", stmts) + "\n    " : ""
               let source = $"class {className} {{\n    void {methodName}() {{\n        readonly {{{stmtBlock}}}\n    }}\n}}"
               select new ReadonlyBlockScenario(source, stmtCount, $"readonly block({stmtCount} stmts): {className}.{methodName}");
    }

    // ─── Property 19: Round-Trip Parsing ────────────────────────────────

    /// <summary>Generates a readonly member declaration for round-trip testing.</summary>
    public static Gen<ReadonlyRoundTripScenario> GenReadonlyMemberRoundTrip()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from fieldType in Gen.Elements(Types)
               from access in Gen.Elements(AccessModifiers)
               let source = $"class {className} {{\n    {access}readonly {fieldType} {fieldName};\n}}"
               select new ReadonlyRoundTripScenario(source, $"readonly member: {className}.{fieldName}");
    }

    /// <summary>Generates a readonly type declaration for round-trip testing.</summary>
    public static Gen<ReadonlyRoundTripScenario> GenReadonlyTypeRoundTrip()
    {
        return Gen.OneOf(
            from name in Gen.Elements(ClassNames)
               let source = $"readonly class {name} {{\n}}"
               select new ReadonlyRoundTripScenario(source, $"readonly class: {name}"),
            from name in Gen.Elements(StructNames)
               let source = $"readonly struct {name} {{\n}}"
               select new ReadonlyRoundTripScenario(source, $"readonly struct: {name}"));
    }

    /// <summary>Generates a readonly block statement for round-trip testing.</summary>
    public static Gen<ReadonlyRoundTripScenario> GenReadonlyBlockRoundTrip()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $"class {className} {{\n    void {methodName}() {{\n        readonly {{\n            var x = 1;\n        }}\n    }}\n}}"
               select new ReadonlyRoundTripScenario(source, $"readonly block: {className}.{methodName}");
    }

    /// <summary>Generates a readonly parameter for round-trip testing.</summary>
    public static Gen<ReadonlyRoundTripScenario> GenReadonlyParameterRoundTrip()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               from paramType in Gen.Elements(Types)
               let source = $"class {className} {{\n    void {methodName}(readonly {paramType} {paramName}) {{\n    }}\n}}"
               select new ReadonlyRoundTripScenario(source, $"readonly param: {className}.{methodName}({paramName})");
    }

    /// <summary>Generates a readonly/mutable generic constraint for round-trip testing.</summary>
    public static Gen<ReadonlyRoundTripScenario> GenReadonlyConstraintRoundTrip()
    {
        return Gen.OneOf(
            from className in Gen.Elements(ClassNames)
               let source = $"class {className}<T> where T : readonly {{\n}}"
               select new ReadonlyRoundTripScenario(source, $"readonly constraint: {className}<T>"),
            from className in Gen.Elements(ClassNames)
               let source = $"class {className}<T> where T : mutable {{\n}}"
               select new ReadonlyRoundTripScenario(source, $"mutable constraint: {className}<T>"));
    }

    /// <summary>Combined generator for all round-trip scenarios.</summary>
    public static Gen<ReadonlyRoundTripScenario> GenReadonlyRoundTripScenario()
    {
        return Gen.OneOf(
            GenReadonlyMemberRoundTrip(),
            GenReadonlyTypeRoundTrip(),
            GenReadonlyBlockRoundTrip(),
            GenReadonlyParameterRoundTrip(),
            GenReadonlyConstraintRoundTrip());
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for readonly parsing.
///
/// Properties 1, 2, and 19 from the readonly-contexts design document.
/// </summary>
public class ReadonlyParsingPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<ReadonlyModifierScenario> ArbitraryReadonlyModifierScenario() =>
        ReadonlyParsingGenerators.GenReadonlyModifierScenario().ToArbitrary();

    public static Arbitrary<ReadonlyBlockScenario> ArbitraryReadonlyBlockScenario() =>
        ReadonlyParsingGenerators.GenReadonlyBlockScenario().ToArbitrary();

    public static Arbitrary<ReadonlyRoundTripScenario> ArbitraryReadonlyRoundTripScenario() =>
        ReadonlyParsingGenerators.GenReadonlyRoundTripScenario().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    /// <summary>
    /// Recursively searches for a node of the specified type in the AST.
    /// </summary>
    private static bool ContainsNodeOfType<T>(Storm.Runtime.SyntaxNode node) where T : Storm.Runtime.SyntaxNode
    {
        if (node is T) return true;
        foreach (var child in node.Children())
        {
            if (child is not null && ContainsNodeOfType<T>(child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Finds the first node of the specified type in the AST.
    /// </summary>
    private static T? FindFirstNode<T>(Storm.Runtime.SyntaxNode node) where T : Storm.Runtime.SyntaxNode
    {
        if (node is T match) return match;
        foreach (var child in node.Children())
        {
            if (child is null) continue;
            var found = FindFirstNode<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Readonly Modifier Parsing Preservation
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9**
    //
    // For any valid declaration with any combination of modifiers including
    // `readonly`, parsing the source SHALL produce an AST node with a
    // `ReadonlyModifier` node attached.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReadonlyParsingPropertyTests) })]
    public Property ReadonlyModifier_IsParsedAndPreservedInAst(ReadonlyModifierScenario scenario)
    {
        var ast = ParseOrFail(scenario.Source);

        var hasReadonlyModifier = ContainsNodeOfType<ReadonlyModifierModifierNode>(ast);

        return hasReadonlyModifier.ToProperty()
            .Label($"No ReadonlyModifierModifierNode found in AST for {scenario.DeclarationKind}.\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Readonly Block Parsing
    //
    // **Validates: Requirements 2.1**
    //
    // For any valid list of statements wrapped in a `readonly { ... }` block,
    // parsing the source SHALL produce a `ReadonlyBlockStatement` node whose
    // body contains AST nodes corresponding to the enclosed statements.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReadonlyParsingPropertyTests) })]
    public Property ReadonlyBlock_IsParsedWithCorrectBody(ReadonlyBlockScenario scenario)
    {
        var ast = ParseOrFail(scenario.Source);

        var readonlyBlockNode = FindFirstNode<ReadonlyBlockStatementStatementNode>(ast);

        if (readonlyBlockNode is null)
            return false.ToProperty()
                .Label($"No ReadonlyBlockStatementStatementNode found.\nSource:\n{scenario.Source}");

        var actualStmtCount = readonlyBlockNode.ReadonlyBlock.Body.Statements.Length;

        return (actualStmtCount == scenario.ExpectedStatementCount).ToProperty()
            .Label($"Expected {scenario.ExpectedStatementCount} statements in readonly block body, got {actualStmtCount}.\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 19: Round-Trip Parsing
    //
    // **Validates: Requirements 20.1, 20.2**
    //
    // For any valid readonly syntax construct, parsing the source, printing
    // the AST back to source, then parsing again SHALL produce an equivalent
    // syntax tree (verified via idempotent pretty-print output).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReadonlyParsingPropertyTests) })]
    public Property RoundTrip_ReadonlySyntax_PrintParseIsIdempotent(ReadonlyRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if (string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label("printed1 was empty");

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify idempotence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}
