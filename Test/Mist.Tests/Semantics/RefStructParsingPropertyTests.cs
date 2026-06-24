// Feature: ref-structs, Property 1: Ref struct parsing round-trip
// Feature: ref-structs, Property 10: Ref modifier on non-struct types produces diagnostic
// **Validates: Requirements 1.1, 1.2, 1.3, 1.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for ref struct parsing round-trip and ref modifier validation.
///
/// Property 1: Ref struct parsing round-trip
/// For any valid identifier and set of struct members (fields, methods, properties),
/// parsing a <c>ref struct</c> declaration SHALL produce a <c>StructType</c> with
/// <c>IsRefStruct = true</c>, and the type SHALL retain all declared members.
///
/// Property 10: Ref modifier on non-struct types produces diagnostic
/// For any class, abstract type, or enum declaration preceded by the <c>ref</c>
/// modifier, the compiler SHALL emit diagnostic <c>MIST2001</c>.
///
/// Uses in-process compilation via shared lexer, parser, and semantic model builder.
/// No per-iteration process spawning.
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.5**
/// </summary>
public sealed class RefStructParsingPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly MistLexer SharedLexer = new();
    private static readonly MistParser SharedParser = new();
    private static readonly SemanticModelBuilder SharedBuilder = new();

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) CompileSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var lexBag = new DiagnosticBag();
        var tokens = SharedLexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = SharedParser.Parse(tokens, parseBag);

        if (parseResult.Root is null)
        {
            var allDiags = lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
            return (null!, allDiags);
        }

        var semanticBag = new DiagnosticBag();
        var model = SharedBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semanticBag);

        var diagnostics = lexBag.ToImmutableArray()
            .AddRange(parseBag.ToImmutableArray())
            .AddRange(parseResult.Diagnostics)
            .AddRange(semanticBag.ToImmutableArray());

        return (model, diagnostics);
    }

    // ─── Scenario model ──────────────────────────────────────────────────

    /// <summary>
    /// Describes a generated ref struct scenario for property testing.
    /// </summary>
#pragma warning disable CA1034
    public sealed record RefStructScenario(
        string StructName,
        IReadOnlyList<string> FieldNames,
        IReadOnlyList<string> MethodNames,
        IReadOnlyList<string> PropertyNames)
    {
        public override string ToString() =>
            $"ref struct {StructName} ({FieldNames.Count} fields, {MethodNames.Count} methods, {PropertyNames.Count} properties)";

        public int TotalMemberCount => FieldNames.Count + MethodNames.Count + PropertyNames.Count;
    }
#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] StructNamePool =
        ["Span", "Buffer", "Slice", "Window", "Frame", "Chunk", "Region", "Segment"];

    private static readonly string[] FieldNamePool =
        ["length", "offset", "capacity", "count", "start", "end", "size", "index"];

    private static readonly string[] MethodNamePool =
        ["GetValue", "SetValue", "Clear", "Reset", "Advance", "Rewind", "DoSlice", "CopyTo"];

    private static readonly string[] PropertyNamePool =
        ["Length", "IsEmpty", "Count", "Capacity", "First", "Last", "Current", "Position"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<RefStructScenario> GenScenario()
    {
        return from structName in Gen.Elements(StructNamePool)
               from fieldCount in Gen.Choose(0, 4)
               from methodCount in Gen.Choose(0, 3)
               from propertyCount in Gen.Choose(0, 3)
               from fields in Gen.Shuffle(FieldNamePool).Select(f => f.Take(fieldCount).ToList())
               from methods in Gen.Shuffle(MethodNamePool).Select(m => m.Take(methodCount).ToList())
               from properties in Gen.Shuffle(PropertyNamePool).Select(p => p.Take(propertyCount).ToList())
               where fieldCount + methodCount + propertyCount > 0
               select new RefStructScenario(
                   structName,
                   (IReadOnlyList<string>)fields,
                   (IReadOnlyList<string>)methods,
                   (IReadOnlyList<string>)properties);
    }

    public static Arbitrary<RefStructScenario> ArbitraryRefStructScenario() =>
        GenScenario().ToArbitrary();

    // ─── Source generation ───────────────────────────────────────────────

    private static string BuildSource(RefStructScenario scenario)
    {
        var members = new System.Text.StringBuilder();

        // Fields
        foreach (var field in scenario.FieldNames)
        {
            members.AppendLine($"    public int {field};");
        }

        // Methods
        foreach (var method in scenario.MethodNames)
        {
            members.AppendLine($"    public int {method}()");
            members.AppendLine("    {");
            members.AppendLine("        return 0;");
            members.AppendLine("    }");
        }

        // Properties (get-only)
        foreach (var prop in scenario.PropertyNames)
        {
            members.AppendLine($"    public int {prop}");
            members.AppendLine("    {");
            members.AppendLine("        get");
            members.AppendLine("        {");
            members.AppendLine("            return 0;");
            members.AppendLine("        }");
            members.AppendLine("    }");
        }

        return $$"""
            namespace TestProgram;

            public ref struct {{scenario.StructName}}
            {
            {{members}}}

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Ref struct parsing round-trip
    //
    // **Validates: Requirements 1.1, 1.2, 1.3**
    //
    // For any valid identifier and set of struct members, parsing a
    // `ref struct` declaration SHALL produce a StructType with
    // IsRefStruct = true retaining all declared members.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(RefStructParsingPropertyTests) },
        MaxTest = 100)]
    public Property RefStructDeclaration_ProducesStructTypeWithIsRefStructTrue_AndRetainsMembers(
        RefStructScenario scenario)
    {
        var source = BuildSource(scenario);
        var (model, diagnostics) = CompileSource(source);

        if (model is null)
        {
            var errorText = string.Join("\n", diagnostics.Select(e => $"  {e.Id.Value}: {e.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed (null model) for {scenario}:\n{errorText}");
        }

        // Check for errors (excluding any that are expected from the semantic model
        // not having stdlib loaded — we only care about parsing and registration).
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            var errorText = string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"));
            return false.ToProperty()
                .Label($"Compilation produced errors for {scenario}:\n{errorText}");
        }

        // Find the struct symbol in the semantic model.
        var structSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.StructName && s.Kind == SymbolKind.Struct);

        if (structSymbol is null)
        {
            return false.ToProperty()
                .Label($"Struct '{scenario.StructName}' not found in semantic model. " +
                       $"All symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        // Requirement 1.3: StructType with IsRefStruct = true
        if (structSymbol.DeclaredType is not StructType structType)
        {
            return false.ToProperty()
                .Label($"'{scenario.StructName}' DeclaredType is not StructType, " +
                       $"got: {structSymbol.DeclaredType?.GetType().Name ?? "null"}");
        }

        // Requirement 1.1: IsRefStruct must be true
        if (!structType.IsRefStruct)
        {
            return false.ToProperty()
                .Label($"'{scenario.StructName}' has IsRefStruct = false, expected true");
        }

        // Requirement 1.2: All declared members are retained
        var memberScope = structType.MemberScope;
        var memberNames = memberScope.Members
            .Where(m => m.Origin == SymbolOrigin.UserCode)
            .Select(m => m.Name)
            .ToHashSet();

        // Verify fields
        foreach (var field in scenario.FieldNames)
        {
            if (!memberNames.Contains(field))
            {
                return false.ToProperty()
                    .Label($"Field '{field}' not found in struct '{scenario.StructName}' members. " +
                           $"Available: [{string.Join(", ", memberNames)}]");
            }
        }

        // Verify methods
        foreach (var method in scenario.MethodNames)
        {
            if (!memberNames.Contains(method))
            {
                return false.ToProperty()
                    .Label($"Method '{method}' not found in struct '{scenario.StructName}' members. " +
                           $"Available: [{string.Join(", ", memberNames)}]");
            }
        }

        // Verify properties
        foreach (var prop in scenario.PropertyNames)
        {
            if (!memberNames.Contains(prop))
            {
                return false.ToProperty()
                    .Label($"Property '{prop}' not found in struct '{scenario.StructName}' members. " +
                           $"Available: [{string.Join(", ", memberNames)}]");
            }
        }

        return true.ToProperty()
            .Label($"ref struct {scenario.StructName} correctly parsed with " +
                   $"{scenario.FieldNames.Count} fields, {scenario.MethodNames.Count} methods, " +
                   $"{scenario.PropertyNames.Count} properties");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Ref modifier on non-struct types produces diagnostic
    //
    // For any class, abstract type, or enum declaration preceded by the
    // `ref` modifier, the compiler SHALL emit diagnostic MIST2001.
    //
    // **Validates: Requirements 1.5**
    // ═══════════════════════════════════════════════════════════════════════

    // ─── Scenario type for Property 10 ───────────────────────────────────

#pragma warning disable CA1034
    /// <summary>Scenario for Property 10: ref modifier on non-struct type declarations.</summary>
    public sealed record RefOnNonStructScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }
#pragma warning restore CA1034

    // ─── Identifier generation helpers ───────────────────────────────────

    private static readonly char[] IdentStartChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    private static readonly char[] IdentRestChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    private static Gen<string> GenValidIdentifier()
    {
        return from first in Gen.Elements(IdentStartChars)
               from restLen in Gen.Choose(2, 8)
               from rest in Gen.ArrayOf(Gen.Elements(IdentRestChars), restLen)
               select first + new string(rest);
    }

    // ─── Arbitrary for Property 10 ───────────────────────────────────────

    public static Arbitrary<RefOnNonStructScenario> ArbitraryRefOnNonStruct()
    {
        // Generate ref class, ref abstract type, and ref enum declarations
        // with random valid identifiers. All should produce MIST2001.
        var classGen =
            from name in GenValidIdentifier()
            let source = $$"""
                ref class {{name}} {
                }
                class Program {
                    public static void Main() {
                    }
                }
                """
            select new RefOnNonStructScenario(source, $"ref class {name}");

        var abstractGen =
            from name in GenValidIdentifier()
            let source = $$"""
                ref abstract {{name}} {
                }
                class Program {
                    public static void Main() {
                    }
                }
                """
            select new RefOnNonStructScenario(source, $"ref abstract {name}");

        var enumGen =
            from name in GenValidIdentifier()
            let source = $$"""
                ref enum {{name}} {
                    A,
                    B
                }
                class Program {
                    public static void Main() {
                    }
                }
                """
            select new RefOnNonStructScenario(source, $"ref enum {name}");

        var combined = Gen.OneOf(classGen, abstractGen, enumGen);
        return combined.ToArbitrary();
    }

    // ─── Property 10 test ────────────────────────────────────────────────

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefStructParsingPropertyTests) })]
    public Property RefModifierOnNonStructType_ProducesMIST2001(RefOnNonStructScenario scenario)
    {
        var (_, diagnostics) = CompileSource(scenario.Source);

        var hasMist2001 = diagnostics.Any(d => d.Id.Value == "MIST2001");

        return hasMist2001.ToProperty()
            .Label($"Expected MIST2001 for {scenario.Label}. " +
                   $"Got: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
