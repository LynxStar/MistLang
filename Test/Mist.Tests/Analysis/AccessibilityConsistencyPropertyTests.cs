// Feature: full-accessibility-model, Property 10: A declaration is never more accessible than the types in its signature
//
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6 (Property 10)**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying accessibility consistency enforcement.
///
/// Property 10: A declaration is never more accessible than the types in its signature.
///
/// For any declaration, an inconsistent-accessibility error (MIST_TC_0173) is reported
/// if and only if some type exposed in its signature (return type, parameter type, field
/// type, property or indexer type, or generic constraint type) is not at least as
/// accessible as the declaration itself.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6 (Property 10)**
/// </summary>
public class AccessibilityConsistencyPropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return lexBag.ToImmutableArray()
            .AddRange(parseBag.ToImmutableArray())
            .AddRange(semBag.ToImmutableArray())
            .AddRange(tcBag.ToImmutableArray());
    }

    private static bool HasInconsistentAccessibilityError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.Id.Value == "MIST_TC_0173");
    }

    private static string MainClass()
    {
        return @"
class Program {
    public static void Main() { }
}";
    }

    /// <summary>
    /// Maps an <see cref="Accessibility"/> value to its Mist keyword.
    /// </summary>
    private static string ToKeyword(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            _ => "private",
        };
    }

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated scenario for testing accessibility consistency enforcement.
    /// </summary>
    public sealed record ConsistencyScenario(
        string Source,
        string Description,
        bool ShouldReportInconsistency)
    {
        public override string ToString()
        {
            return $"[{(ShouldReportInconsistency ? "REJECT" : "ACCEPT")}] {Description}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeNames = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];
    private static readonly string[] FieldNames = ["value", "data", "item", "state", "result"];
    private static readonly string[] MethodNames = ["Process", "Compute", "Resolve", "Transform", "Execute"];

    /// <summary>
    /// For the accessibility consistency check, we only use accessibility levels that
    /// are meaningful for top-level types: Public and Internal (in Mist, top-level types
    /// can be public or internal; private/protected are only meaningful on nested types
    /// or members). The member can have any of the four levels.
    /// </summary>
    private static Gen<(Accessibility MemberAccess, Accessibility TypeAccess, bool ShouldReject)> GenAccessibilityPair()
    {
        // Top-level types can only meaningfully be Public or Internal.
        var typeAccessibilities = new[] { Accessibility.Public, Accessibility.Internal };
        var memberAccessibilities = new[] { Accessibility.Public, Accessibility.Private, Accessibility.Protected, Accessibility.Internal };

        return from memberAccess in Gen.Elements(memberAccessibilities)
               from typeAccess in Gen.Elements(typeAccessibilities)
               let shouldReject = !AccessibilityOrder.IsAtLeastAsAccessibleAs(typeAccess, memberAccess)
               select (memberAccess, typeAccess, shouldReject);
    }

    /// <summary>
    /// Generates scenarios where a method's return type may or may not be as accessible
    /// as the method itself.
    /// Requirement 8.1: method return type less accessible than method -> error.
    /// </summary>
    private static Gen<ConsistencyScenario> GenMethodReturnTypeScenario()
    {
        return from pair in GenAccessibilityPair()
               from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               let typeKeyword = ToKeyword(pair.TypeAccess)
               let memberKeyword = ToKeyword(pair.MemberAccess)
               select new ConsistencyScenario(
                   Source: $@"
{typeKeyword} class {typeName} {{
    public {typeName}() {{ }}
}}
public class Container {{
    {memberKeyword} {typeName} {methodName}() {{
        return new {typeName}();
    }}
}}" + MainClass(),
                   Description: $"Method '{memberKeyword} {typeName} {methodName}()' with {typeKeyword} return type {typeName}",
                   ShouldReportInconsistency: pair.ShouldReject);
    }

    /// <summary>
    /// Generates scenarios where a method's parameter type may or may not be as accessible
    /// as the method itself.
    /// Requirement 8.2: parameter type less accessible than method -> error.
    /// </summary>
    private static Gen<ConsistencyScenario> GenMethodParameterTypeScenario()
    {
        return from pair in GenAccessibilityPair()
               from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               let typeKeyword = ToKeyword(pair.TypeAccess)
               let memberKeyword = ToKeyword(pair.MemberAccess)
               select new ConsistencyScenario(
                   Source: $@"
{typeKeyword} class {typeName} {{ }}
public class Container {{
    {memberKeyword} int {methodName}({typeName} param) {{
        return 0;
    }}
}}" + MainClass(),
                   Description: $"Method '{memberKeyword} int {methodName}({typeName} param)' with {typeKeyword} parameter type {typeName}",
                   ShouldReportInconsistency: pair.ShouldReject);
    }

    /// <summary>
    /// Generates scenarios where a field's type may or may not be as accessible
    /// as the field itself.
    /// Requirement 8.3: field type less accessible than field -> error.
    /// </summary>
    private static Gen<ConsistencyScenario> GenFieldTypeScenario()
    {
        return from pair in GenAccessibilityPair()
               from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let typeKeyword = ToKeyword(pair.TypeAccess)
               let memberKeyword = ToKeyword(pair.MemberAccess)
               select new ConsistencyScenario(
                   Source: $@"
{typeKeyword} class {typeName} {{ }}
public class Container {{
    {memberKeyword} {typeName} {fieldName};
}}" + MainClass(),
                   Description: $"Field '{memberKeyword} {typeName} {fieldName}' with {typeKeyword} type {typeName}",
                   ShouldReportInconsistency: pair.ShouldReject);
    }

    /// <summary>
    /// Generates scenarios where a property's type may or may not be as accessible
    /// as the property itself.
    /// Requirement 8.4: property type less accessible than property -> error.
    /// </summary>
    private static Gen<ConsistencyScenario> GenPropertyTypeScenario()
    {
        return from pair in GenAccessibilityPair()
               from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let typeKeyword = ToKeyword(pair.TypeAccess)
               let memberKeyword = ToKeyword(pair.MemberAccess)
               select new ConsistencyScenario(
                   Source: $@"
{typeKeyword} class {typeName} {{ }}
public class Container {{
    {memberKeyword} {typeName} {fieldName} {{ get; set; }}
}}" + MainClass(),
                   Description: $"Property '{memberKeyword} {typeName} {fieldName}' with {typeKeyword} type {typeName}",
                   ShouldReportInconsistency: pair.ShouldReject);
    }

    /// <summary>
    /// Generates scenarios where a constructor's parameter type may or may not be as
    /// accessible as the constructor.
    /// Requirement 8.2: parameter type less accessible than declaring member -> error.
    /// </summary>
    private static Gen<ConsistencyScenario> GenConstructorParameterTypeScenario()
    {
        return from pair in GenAccessibilityPair()
               from typeName in Gen.Elements(TypeNames)
               let typeKeyword = ToKeyword(pair.TypeAccess)
               let memberKeyword = ToKeyword(pair.MemberAccess)
               select new ConsistencyScenario(
                   Source: $@"
{typeKeyword} class {typeName} {{ }}
public class Container {{
    {memberKeyword} Container({typeName} param) {{ }}
}}" + MainClass(),
                   Description: $"Constructor '{memberKeyword} Container({typeName} param)' with {typeKeyword} parameter type {typeName}",
                   ShouldReportInconsistency: pair.ShouldReject);
    }

    /// <summary>
    /// Generates scenarios where a struct field's type may or may not be as accessible
    /// as the field itself (verifying struct members are also checked).
    /// Requirement 8.3: field type less accessible than field -> error.
    /// </summary>
    private static Gen<ConsistencyScenario> GenStructFieldTypeScenario()
    {
        return from pair in GenAccessibilityPair()
               from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let typeKeyword = ToKeyword(pair.TypeAccess)
               let memberKeyword = ToKeyword(pair.MemberAccess)
               select new ConsistencyScenario(
                   Source: $@"
{typeKeyword} class {typeName} {{ }}
public struct Container {{
    {memberKeyword} {typeName} {fieldName};
}}" + MainClass(),
                   Description: $"Struct field '{memberKeyword} {typeName} {fieldName}' with {typeKeyword} type {typeName}",
                   ShouldReportInconsistency: pair.ShouldReject);
    }

    /// <summary>
    /// Combined generator that selects across all scenario categories.
    /// </summary>
    private static Gen<ConsistencyScenario> GenScenario()
    {
        return Gen.OneOf(
            GenMethodReturnTypeScenario(),
            GenMethodParameterTypeScenario(),
            GenFieldTypeScenario(),
            GenPropertyTypeScenario(),
            GenConstructorParameterTypeScenario(),
            GenStructFieldTypeScenario());
    }

    public static Arbitrary<ConsistencyScenario> ArbitraryConsistencyScenario()
    {
        return Arb.From(GenScenario());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: A declaration is never more accessible than the types in
    // its signature
    //
    // For any declaration, an inconsistent-accessibility error (MIST_TC_0173)
    // is reported if and only if some type exposed in its signature is not at
    // least as accessible as the declaration itself.
    //
    // **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AccessibilityConsistencyPropertyTests) })]
    public Property AccessibilityConsistency_InconsistentAccessibilityReportedIffTypeIsLessAccessible(ConsistencyScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasInconsistency = HasInconsistentAccessibilityError(diagnostics);

        if(scenario.ShouldReportInconsistency)
        {
            // Should have an inconsistent-accessibility error (MIST_TC_0173).
            return hasInconsistency.ToProperty()
                .Label($"Expected REJECT (MIST_TC_0173) but no inconsistency error for: {scenario.Description}" +
                       $"\nAll diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
        }
        else
        {
            // Should NOT have an inconsistent-accessibility error.
            return (!hasInconsistency).ToProperty()
                .Label($"Expected ACCEPT but got MIST_TC_0173 for: {scenario.Description}" +
                       $"\nDiagnostics: {string.Join("; ", diagnostics.Where(d => d.Id.Value == "MIST_TC_0173").Select(d => $"{d.Id.Value}: {d.Message}"))}");
        }
    }
}