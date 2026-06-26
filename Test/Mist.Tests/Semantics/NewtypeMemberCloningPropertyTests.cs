// Feature: newtypes, Property 7: Member cloning completeness
// **Validates: Requirements 3.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying member cloning completeness for newtypes.
///
/// Property 7: For any source type with N members (across all categories: fields,
/// properties, methods, constructors, operators, cast operators, indexers), the
/// newtype's member scope SHALL contain exactly N cloned members.
///
/// **Validates: Requirements 3.2**
/// </summary>
public sealed class NewtypeMemberCloningPropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a generated source type with a known member count and a newtype declared from it.
    /// </summary>
    public sealed record MemberCloningScenario(
        string Source,
        string SourceTypeName,
        string NewtypeName,
        int ExpectedMemberCount,
        string Description)
    {
        public override string ToString() => Description;
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] SourceTypeNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] NewtypeNames =
        ["MyAlpha", "MyBeta", "MyGamma", "MyDelta", "MyEpsilon"];

    private static readonly string[] FieldNames =
        ["x", "y", "z", "w", "count", "value", "data", "flag"];

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform"];

    /// <summary>
    /// Generates a scenario with a class source type containing varying numbers of
    /// fields, methods, and constructors, then declares a newtype from it.
    /// </summary>
    public static Gen<MemberCloningScenario> GenClassScenario()
    {
        return from sourceIdx in Gen.Choose(0, SourceTypeNames.Length - 1)
               from newtypeIdx in Gen.Choose(0, NewtypeNames.Length - 1)
               from fieldCount in Gen.Choose(0, 4)
               from methodCount in Gen.Choose(0, 4)
               from hasConstructor in Gen.Elements(true, false)
               let sourceName = SourceTypeNames[sourceIdx]
               let newtypeName = NewtypeNames[newtypeIdx]
               where sourceName != newtypeName
               let fields = Enumerable.Range(0, fieldCount)
                   .Select(i => $"    public int {FieldNames[i]};")
                   .ToArray()
               let methods = Enumerable.Range(0, methodCount)
                   .Select(i => $"    public int {MethodNames[i]}() {{ return 0; }}")
                   .ToArray()
               let ctorLines = hasConstructor
                   ? new[] { $"    public {sourceName}() {{ }}" }
                   : Array.Empty<string>()
               let allMembers = string.Join("\n", fields.Concat(methods).Concat(ctorLines))
               let expectedCount = fieldCount + methodCount + (hasConstructor ? 1 : 0)
               let source = $"namespace TestNs;\n\npublic class {sourceName}\n{{\n{allMembers}\n}}\n\ntype {newtypeName} = new {sourceName};\n"
               select new MemberCloningScenario(
                   source,
                   sourceName,
                   newtypeName,
                   expectedCount,
                   $"class {sourceName} ({fieldCount}F, {methodCount}M, {(hasConstructor ? "1C" : "0C")}) -> {newtypeName}");
    }

    /// <summary>
    /// Generates a scenario with a struct source type containing varying numbers of
    /// fields and methods, then declares a newtype from it.
    /// </summary>
    public static Gen<MemberCloningScenario> GenStructScenario()
    {
        return from sourceIdx in Gen.Choose(0, SourceTypeNames.Length - 1)
               from newtypeIdx in Gen.Choose(0, NewtypeNames.Length - 1)
               from fieldCount in Gen.Choose(0, 4)
               from methodCount in Gen.Choose(0, 3)
               from hasConstructor in Gen.Elements(true, false)
               let sourceName = SourceTypeNames[sourceIdx]
               let newtypeName = NewtypeNames[newtypeIdx]
               where sourceName != newtypeName
               let fields = Enumerable.Range(0, fieldCount)
                   .Select(i => $"    public int {FieldNames[i]};")
                   .ToArray()
               let methods = Enumerable.Range(0, methodCount)
                   .Select(i => $"    public int {MethodNames[i]}() {{ return 0; }}")
                   .ToArray()
               let ctorParams = fieldCount > 0
                   ? string.Join(", ", Enumerable.Range(0, fieldCount).Select(i => $"int p{i}"))
                   : "int dummy"
               let ctorBody = fieldCount > 0
                   ? string.Join("\n", Enumerable.Range(0, fieldCount).Select(i => $"        this.{FieldNames[i]} = p{i};"))
                   : "        // no-op"
               let ctorLines = hasConstructor
                   ? new[] { $"    public {sourceName}({ctorParams})\n    {{\n{ctorBody}\n    }}" }
                   : Array.Empty<string>()
               let allMembers = string.Join("\n", fields.Concat(methods).Concat(ctorLines))
               let expectedCount = fieldCount + methodCount + (hasConstructor ? 1 : 0)
               let source = $"namespace TestNs;\n\npublic struct {sourceName}\n{{\n{allMembers}\n}}\n\ntype {newtypeName} = new {sourceName};\n"
               select new MemberCloningScenario(
                   source,
                   sourceName,
                   newtypeName,
                   expectedCount,
                   $"struct {sourceName} ({fieldCount}F, {methodCount}M, {(hasConstructor ? "1C" : "0C")}) -> {newtypeName}");
    }

    /// <summary>
    /// Combined generator covering both class and struct source types.
    /// </summary>
    public static Gen<MemberCloningScenario> GenScenario()
    {
        return Gen.OneOf(GenClassScenario(), GenStructScenario());
    }

    /// <summary>FsCheck Arbitrary registration.</summary>
    public static Arbitrary<MemberCloningScenario> ArbitraryMemberCloningScenario() =>
        GenScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return (null!, lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray()));
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

        return (model, semBag.ToImmutableArray());
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 7: For any source type with N members, the newtype's member scope
    /// SHALL contain exactly N cloned members.
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NewtypeMemberCloningPropertyTests) }, MaxTest = 100)]
    public Property MemberCloningCompleteness(MemberCloningScenario scenario)
    {
        var (model, diagnostics) = BuildModel(scenario.Source);

        // If the model failed to build, report the diagnostics.
        if(model is null)
        {
            return false.ToProperty()
                .Label($"Model build failed. Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        // Find the source type symbol.
        var sourceSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.SourceTypeName &&
                (s.Kind == SymbolKind.Class || s.Kind == SymbolKind.Struct));

        if(sourceSymbol is null)
        {
            return false.ToProperty()
                .Label($"Source type '{scenario.SourceTypeName}' not found in symbol table.");
        }

        // Get the source type's member scope.
        var sourceType = sourceSymbol.DeclaredType;
        IScope? sourceMemberScope = sourceType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if(sourceMemberScope is null)
        {
            return false.ToProperty()
                .Label($"Source type '{scenario.SourceTypeName}' has no member scope.");
        }

        // Find the newtype symbol.
        var newtypeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.NewtypeName && s.Kind == SymbolKind.Newtype);

        if(newtypeSymbol is null)
        {
            return false.ToProperty()
                .Label($"Newtype '{scenario.NewtypeName}' not found in symbol table. " +
                       $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        // Get the newtype's member scope.
        if(newtypeSymbol.DeclaredType is not NewtypeType newtypeType)
        {
            return false.ToProperty()
                .Label($"Newtype '{scenario.NewtypeName}' DeclaredType is not NewtypeType.");
        }

        var newtypeMemberScope = newtypeType.MemberScope;

        // Compare member counts: the newtype's member scope should have exactly
        // the same number of members as the source type's scope.
        var sourceMemberCount = sourceMemberScope.Members.Count;
        var newtypeMemberCount = newtypeMemberScope.Members.Count;

        return (newtypeMemberCount == sourceMemberCount).ToProperty()
            .Label($"Member count mismatch: source '{scenario.SourceTypeName}' has {sourceMemberCount} members, " +
                   $"newtype '{scenario.NewtypeName}' has {newtypeMemberCount} members. " +
                   $"Expected {scenario.ExpectedMemberCount}. " +
                   $"Source members: [{string.Join(", ", sourceMemberScope.Members.Select(m => $"{m.Kind}:{m.Name}"))}]. " +
                   $"Newtype members: [{string.Join(", ", newtypeMemberScope.Members.Select(m => $"{m.Kind}:{m.Name}"))}].");
    }
}