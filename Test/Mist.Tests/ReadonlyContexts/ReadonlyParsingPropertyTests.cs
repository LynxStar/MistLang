// readonly-contexts Properties 1, 2 and 19.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.ReadonlyContexts;

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

    public override string ToString()
    {
        return Description;
    }
}

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

    public override string ToString()
    {
        return Description;
    }
}

public sealed class ReadonlyRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public ReadonlyRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class ReadonlyParsingGenerators
{
    private static readonly string[] ClassNames = ["Foo", "Bar", "Baz", "Widget"];
    private static readonly string[] StructNames = ["Point", "Vector", "Cell"];
    private static readonly string[] AbstractNames = ["IReadable", "IService", "IStore"];
    private static readonly string[] FieldNames = ["x", "y", "count", "name"];
    private static readonly string[] MethodNames = ["Get", "Compute", "Process"];
    private static readonly string[] PropertyNames = ["Value", "Name", "Count"];
    private static readonly string[] ParamNames = ["item", "value", "data"];
    private static readonly string[] Types = ["int", "string", "bool", "float"];
    private static readonly string[] AccessModifiers = ["", "public ", "private "];

    private static Gen<string> GenModifiersWithReadonly()
    {
        return Gen.Elements(AccessModifiers).Select(access => $"{access}readonly ");
    }

    public static Gen<ReadonlyModifierScenario> GenReadonlyField()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from fieldType in Gen.Elements(Types)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{fieldType} {fieldName};\n}}"
               select new ReadonlyModifierScenario(source, "field", $"readonly field: {className}.{fieldName}");
    }

    public static Gen<ReadonlyModifierScenario> GenReadonlyProperty()
    {
        return from className in Gen.Elements(ClassNames)
               from propName in Gen.Elements(PropertyNames)
               from propType in Gen.Elements(Types)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{propType} {propName} => 42;\n}}"
               select new ReadonlyModifierScenario(source, "property", $"readonly property: {className}.{propName}");
    }

    public static Gen<ReadonlyModifierScenario> GenReadonlyMethod()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(Types)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{returnType} {methodName}() {{\n        return 0;\n    }}\n}}"
               select new ReadonlyModifierScenario(source, "method", $"readonly method: {className}.{methodName}");
    }

    public static Gen<ReadonlyModifierScenario> GenReadonlyConstructor()
    {
        return from className in Gen.Elements(ClassNames)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{className}() {{\n    }}\n}}"
               select new ReadonlyModifierScenario(source, "constructor", $"readonly constructor: {className}");
    }

    public static Gen<ReadonlyModifierScenario> GenReadonlyClass()
    {
        return from className in Gen.Elements(ClassNames)
               from access in Gen.Elements(AccessModifiers)
               let source = $"{access}readonly class {className} {{\n}}"
               select new ReadonlyModifierScenario(source, "class", $"readonly class: {className}");
    }

    public static Gen<ReadonlyModifierScenario> GenReadonlyStruct()
    {
        return from structName in Gen.Elements(StructNames)
               from access in Gen.Elements(AccessModifiers)
               let source = $"{access}readonly struct {structName} {{\n}}"
               select new ReadonlyModifierScenario(source, "struct", $"readonly struct: {structName}");
    }

    public static Gen<ReadonlyModifierScenario> GenReadonlyAbstract()
    {
        return from abstractName in Gen.Elements(AbstractNames)
               from access in Gen.Elements(AccessModifiers)
               let source = $"{access}readonly abstract {abstractName} {{\n}}"
               select new ReadonlyModifierScenario(source, "abstract", $"readonly abstract type: {abstractName}");
    }

    public static Gen<ReadonlyModifierScenario> GenReadonlyIndexer()
    {
        return from className in Gen.Elements(ClassNames)
               from indexType in Gen.Elements(Types)
               from mods in GenModifiersWithReadonly()
               let source = $"class {className} {{\n    {mods}{indexType} this[int i] {{\n        get {{\n            return 0;\n        }}\n    }}\n}}"
               select new ReadonlyModifierScenario(source, "indexer", $"readonly indexer: {className}");
    }

    public static Gen<ReadonlyModifierScenario> GenReadonlyModifierScenario()
    {
        return Gen.OneOf(
            GenReadonlyField(),
            GenReadonlyProperty(),
            GenReadonlyMethod(),
            GenReadonlyConstructor(),
            GenReadonlyClass(),
            GenReadonlyStruct(),
            GenReadonlyAbstract(),
            GenReadonlyIndexer());
    }

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

    public static Gen<ReadonlyRoundTripScenario> GenReadonlyMemberRoundTrip()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from fieldType in Gen.Elements(Types)
               from access in Gen.Elements(AccessModifiers)
               let source = $"class {className} {{\n    {access}readonly {fieldType} {fieldName};\n}}"
               select new ReadonlyRoundTripScenario(source, $"readonly member: {className}.{fieldName}");
    }

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

    public static Gen<ReadonlyRoundTripScenario> GenReadonlyBlockRoundTrip()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $"class {className} {{\n    void {methodName}() {{\n        readonly {{\n            var x = 1;\n        }}\n    }}\n}}"
               select new ReadonlyRoundTripScenario(source, $"readonly block: {className}.{methodName}");
    }

    public static Gen<ReadonlyRoundTripScenario> GenReadonlyParameterRoundTrip()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               from paramType in Gen.Elements(Types)
               let source = $"class {className} {{\n    void {methodName}(readonly {paramType} {paramName}) {{\n    }}\n}}"
               select new ReadonlyRoundTripScenario(source, $"readonly param: {className}.{methodName}({paramName})");
    }

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

public class ReadonlyParsingPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // Arbitrary registration

    public static Arbitrary<ReadonlyModifierScenario> ArbitraryReadonlyModifierScenario()
    {
        return ReadonlyParsingGenerators.GenReadonlyModifierScenario().ToArbitrary();
    }

    public static Arbitrary<ReadonlyBlockScenario> ArbitraryReadonlyBlockScenario()
    {
        return ReadonlyParsingGenerators.GenReadonlyBlockScenario().ToArbitrary();
    }

    public static Arbitrary<ReadonlyRoundTripScenario> ArbitraryReadonlyRoundTripScenario()
    {
        return ReadonlyParsingGenerators.GenReadonlyRoundTripScenario().ToArbitrary();
    }

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    private static bool ContainsNodeOfType<T>(Storm.Runtime.SyntaxNode node) where T : Storm.Runtime.SyntaxNode
    {
        if(node is T)
        {
            return true;
        }

        foreach(var child in node.Children())
        {
            if(child is not null && ContainsNodeOfType<T>(child))
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindFirstNode<T>(Storm.Runtime.SyntaxNode node) where T : Storm.Runtime.SyntaxNode
    {
        if(node is T match)
        {
            return match;
        }

        foreach(var child in node.Children())
        {
            if(child is null)
            {
                continue;
            }

            var found = FindFirstNode<T>(child);
            if(found is not null)
            {
                return found;
            }
        }

        return null;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReadonlyParsingPropertyTests) })]
    public Property ReadonlyModifier_IsParsedAndPreservedInAst(ReadonlyModifierScenario scenario)
    {
        var ast = ParseOrFail(scenario.Source);

        var hasReadonlyModifier = ContainsNodeOfType<ReadonlyModifierModifierNode>(ast);

        return hasReadonlyModifier.ToProperty()
            .Label($"No ReadonlyModifierModifierNode found in AST for {scenario.DeclarationKind}.\nSource:\n{scenario.Source}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReadonlyParsingPropertyTests) })]
    public Property ReadonlyBlock_IsParsedWithCorrectBody(ReadonlyBlockScenario scenario)
    {
        var ast = ParseOrFail(scenario.Source);

        var readonlyBlockNode = FindFirstNode<ReadonlyBlockStatementStatementNode>(ast);

        if(readonlyBlockNode is null)
        {
            return false.ToProperty()
                .Label($"No ReadonlyBlockStatementStatementNode found.\nSource:\n{scenario.Source}");
        }

        var actualStmtCount = readonlyBlockNode.ReadonlyBlock.Body.Statements.Length;

        return (actualStmtCount == scenario.ExpectedStatementCount).ToProperty()
            .Label($"Expected {scenario.ExpectedStatementCount} statements in readonly block body, got {actualStmtCount}.\nSource:\n{scenario.Source}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReadonlyParsingPropertyTests) })]
    public Property RoundTrip_ReadonlySyntax_PrintParseIsIdempotent(ReadonlyRoundTripScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var ast2 = ParseOrFail(printed1);

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}