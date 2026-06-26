// Feature: enums, Property 11: C# emission structure
// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Describes a generated enum emission scenario for property testing.
/// Carries the Mist source text and a human-readable description.
/// </summary>
public sealed class EnumEmissionScenario
{
    public string MistSource { get; }
    public string Description { get; }
    public string EnumName { get; }
    public int MemberCount { get; }
    public string ExpectedAccessModifier { get; }

    public EnumEmissionScenario(
        string mistSource,
        string description,
        string enumName,
        int memberCount,
        string expectedAccessModifier)
    {
        MistSource = mistSource;
        Description = description;
        EnumName = enumName;
        MemberCount = memberCount;
        ExpectedAccessModifier = expectedAccessModifier;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests for C# emission structure of enum declarations.
///
/// Property 11: C# emission structure
///
/// For any valid enum declaration, the emitted C# SHALL contain a <c>record class</c>
/// with the correct access modifier, a primary constructor parameter of the underlying
/// type named <c>Value</c>, a <c>public static readonly</c> field for each member, and
/// an <c>implicit operator</c> converting to the underlying type.
///
/// Optimized: runs lex → parse → semantic model → emit in-process without any
/// disk I/O per iteration. Uses <see cref="CSharpEmitter.EmitToMemory"/>.
///
/// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3**
/// </summary>
public sealed class EnumEmissionPropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] EnumNames =
        ["Direction", "Color", "Status", "Priority", "Level"];

    private static readonly string[] MemberNamePool =
        ["North", "South", "East", "West", "Red", "Green", "Blue",
         "Active", "Inactive", "Pending", "High", "Low"];

    private static readonly string[] IntTypeAnnotations =
        ["int", "byte", "long", "short"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a public enum with integer-valued members.
    /// </summary>
    private static Gen<EnumEmissionScenario> GenPublicIntEnum()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from typeAnnotation in Gen.Elements(IntTypeAnnotations)
               let memberList = string.Join(",\n    ",
                   members.Select((m, i) => $"{m} = {i}"))
               let source = $"public enum {name} : {typeAnnotation} {{\n    {memberList}\n}}\nclass Program {{\n    static void Main() {{\n    }}\n}}"
               select new EnumEmissionScenario(
                   source,
                   $"public int-valued: enum {name} : {typeAnnotation} ({memberCount} members)",
                   name,
                   memberCount,
                   "public");
    }

    /// <summary>
    /// Generates a private enum with integer-valued members.
    /// </summary>
    private static Gen<EnumEmissionScenario> GenPrivateIntEnum()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from typeAnnotation in Gen.Elements(IntTypeAnnotations)
               let memberList = string.Join(",\n    ",
                   members.Select((m, i) => $"{m} = {i}"))
               let source = $"private enum {name} : {typeAnnotation} {{\n    {memberList}\n}}\nclass Program {{\n    static void Main() {{\n    }}\n}}"
               select new EnumEmissionScenario(
                   source,
                   $"private int-valued: enum {name} : {typeAnnotation} ({memberCount} members)",
                   name,
                   memberCount,
                   "private");
    }

    /// <summary>
    /// Generates an enum with no access modifier (defaults to internal in C#).
    /// </summary>
    private static Gen<EnumEmissionScenario> GenDefaultVisibilityEnum()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from typeAnnotation in Gen.Elements(IntTypeAnnotations)
               let memberList = string.Join(",\n    ",
                   members.Select((m, i) => $"{m} = {i}"))
               let source = $"enum {name} : {typeAnnotation} {{\n    {memberList}\n}}\nclass Program {{\n    static void Main() {{\n    }}\n}}"
               select new EnumEmissionScenario(
                   source,
                   $"default-visibility int-valued: enum {name} : {typeAnnotation} ({memberCount} members)",
                   name,
                   memberCount,
                   "");
    }

    /// <summary>
    /// Generates a name-only enum (no explicit values, underlying type defaults to byte).
    /// </summary>
    private static Gen<EnumEmissionScenario> GenNameOnlyEnum()
    {
        return from name in Gen.Elements(EnumNames)
               from modifier in Gen.Elements("public ", "private ", "")
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               let memberList = string.Join(",\n    ", members)
               let source = $"{modifier}enum {name} {{\n    {memberList}\n}}\nclass Program {{\n    static void Main() {{\n    }}\n}}"
               let expectedMod = modifier.TrimEnd()
               select new EnumEmissionScenario(
                   source,
                   $"{(string.IsNullOrEmpty(expectedMod) ? "default" : expectedMod)} name-only: enum {name} ({memberCount} members)",
                   name,
                   memberCount,
                   expectedMod);
    }

    /// <summary>
    /// Generates an enum with string-valued members.
    /// </summary>
    private static Gen<EnumEmissionScenario> GenStringValuedEnum()
    {
        return from name in Gen.Elements(EnumNames)
               from modifier in Gen.Elements("public ", "private ", "")
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               let memberList = string.Join(",\n    ",
                   members.Select(m => $"{m} = \"{m.ToLowerInvariant()}\""))
               let source = $"{modifier}enum {name} : string {{\n    {memberList}\n}}\nclass Program {{\n    static void Main() {{\n    }}\n}}"
               let expectedMod = modifier.TrimEnd()
               select new EnumEmissionScenario(
                   source,
                   $"{(string.IsNullOrEmpty(expectedMod) ? "default" : expectedMod)} string-valued: enum {name} ({memberCount} members)",
                   name,
                   memberCount,
                   expectedMod);
    }

    public static Arbitrary<EnumEmissionScenario> ArbitraryEnumEmissionScenario()
    {
        return Gen.OneOf(
            GenPublicIntEnum(),
            GenPrivateIntEnum(),
            GenDefaultVisibilityEnum(),
            GenNameOnlyEnum(),
            GenStringValuedEnum()).ToArbitrary();
    }

    // ─── In-memory compilation (no disk I/O) ────────────────────────────

    private static (bool Succeeded, string CsContent, string DiagnosticText) CompileInMemory(
        string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            return (false, "", FormatDiagnostics(lexBag));
        }

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
        {
            return (false, "", FormatDiagnostics(parseBag));
        }

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);

        if(semBag.HasErrors)
        {
            return (false, "", FormatDiagnostics(semBag));
        }

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        if(tcBag.ToImmutableArray().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return (false, "", FormatDiagnostics(tcBag));
        }

        // Emit in-memory (no disk I/O)
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);

        if(emitBag.HasErrors)
        {
            return (false, "", FormatDiagnostics(emitBag));
        }

        // Concatenate all emitted .cs content
        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return (true, sb.ToString(), "");
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: C# emission structure
    //
    // **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3**
    //
    // For any valid enum declaration, the emitted C# SHALL contain a
    // record class with the correct access modifier, a primary constructor
    // parameter of the underlying type named Value, a public static readonly
    // field for each member, and an implicit operator converting to the
    // underlying type.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(EnumEmissionPropertyTests) })]
    public Property EnumEmission_ContainsRecordClassWithCorrectStructure(
        EnumEmissionScenario scenario)
    {
        var (succeeded, csContent, diagnosticText) = CompileInMemory(scenario.MistSource);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation failed: {scenario.Description}\nDiagnostics:\n{diagnosticText}");
        }

        if(string.IsNullOrWhiteSpace(csContent))
        {
            return false.ToProperty()
                .Label($"C# output was empty: {scenario.Description}");
        }

        // Requirement 9.1: Emitted C# contains a record class declaration
        var expectedRecordDecl = scenario.ExpectedAccessModifier switch
        {
            "public" => $"public record class {scenario.EnumName}(",
            "private" => $"private record class {scenario.EnumName}(",
            _ => $"record class {scenario.EnumName}(",
        };

        if(!csContent.Contains(expectedRecordDecl))
        {
            return false.ToProperty()
                .Label($"Missing record class declaration '{expectedRecordDecl}'\nEmitted:\n{csContent}");
        }

        // Requirement 9.1: Primary constructor has a Value parameter
        if(!csContent.Contains(" Value)"))
        {
            return false.ToProperty()
                .Label($"Missing 'Value' constructor parameter\nEmitted:\n{csContent}");
        }

        // Requirement 9.2: Each member has a public static readonly field
        var staticReadonlyCount = CountOccurrences(csContent, "public static readonly");
        if(staticReadonlyCount < scenario.MemberCount)
        {
            return false.ToProperty()
                .Label($"Expected at least {scenario.MemberCount} 'public static readonly' fields, found {staticReadonlyCount}\nEmitted:\n{csContent}");
        }

        // Requirement 9.3: Contains an implicit operator
        if(!csContent.Contains("public static implicit operator"))
        {
            return false.ToProperty()
                .Label($"Missing 'implicit operator'\nEmitted:\n{csContent}");
        }

        // Requirement 9.3: The implicit operator returns e.Value
        if(!csContent.Contains("=> e.Value;"))
        {
            return false.ToProperty()
                .Label($"Missing '=> e.Value;' in implicit operator\nEmitted:\n{csContent}");
        }

        // Requirement 9.4 / 10.1 / 10.2 / 10.3: Access modifier is correct
        // For public/private, the modifier should appear before "record class"
        // For default (no modifier), "record class" should appear without a preceding modifier
        if(!string.IsNullOrEmpty(scenario.ExpectedAccessModifier))
        {
            var modifierPattern = $"{scenario.ExpectedAccessModifier} record class {scenario.EnumName}";
            if(!csContent.Contains(modifierPattern))
            {
                return false.ToProperty()
                    .Label($"Expected access modifier '{scenario.ExpectedAccessModifier}' before record class\nEmitted:\n{csContent}");
            }
        }

        return true.ToProperty()
            .Label($"OK: {scenario.Description}");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}