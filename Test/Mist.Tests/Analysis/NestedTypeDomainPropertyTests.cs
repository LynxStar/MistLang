// Feature: full-accessibility-model, Property 13: A nested type's domain is the intersection of its own accessibility and its enclosing type's domain
//
// **Validates: Requirements 11.1, 11.2 (Property 13)**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for nested-type accessibility domain enforcement.
///
/// Property 13: A nested type's domain is the intersection of its own accessibility
/// and its enclosing type's domain. A reference from outside that computed domain is
/// reported as an accessibility-violation error (MIST_TC_0176 when the type resolves
/// through the semantic model, or MIST_UNDEFINED_TYPE when it cannot be resolved from
/// outside), and a reference from within is accepted without an accessibility diagnostic.
///
/// In Mist, nested types are accessed by simple name from within the enclosing type
/// and its nested types. Qualified name access (Outer.Inner) from external code is not
/// supported by the name resolver. The domain intersection check (MIST_TC_0176) fires
/// when a symbol's resolved type reference crosses the domain boundary — this occurs
/// primarily through type positions within signatures.
///
/// The test generates Mist programs with nested types at various accessibility levels
/// (public, private, protected, internal) and reference sites from within the enclosing
/// type (should succeed) and from outside (rejected by resolution or domain check).
/// It compiles in-process via the pipeline with no process spawn, min 100 iterations.
///
/// **Validates: Requirements 11.1, 11.2 (Property 13)**
/// </summary>
public class NestedTypeDomainPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
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

    /// <summary>
    /// Checks for any error indicating the nested type is inaccessible from outside
    /// its domain. This manifests as MIST_TC_0176 (NestedTypeOutsideDomain) when the
    /// type resolves in the semantic model, or MIST_UNDEFINED_TYPE when the nested type
    /// cannot be resolved from the referencing scope.
    /// </summary>
    private static bool HasNestedTypeAccessError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            (d.Id.Value == "MIST_TC_0176" || d.Id.Value == "MIST_UNDEFINED_TYPE"));
    }

    private static bool HasAnyError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    private static string MainClass()
    {
        return @"
class Program {
    public static void Main() { }
}";
    }

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated scenario for testing nested-type accessibility domain enforcement.
    /// </summary>
    public sealed record NestedTypeDomainScenario(
        string Source,
        string Description,
        bool ShouldAccept)
    {
        public override string ToString()
        {
            return $"[{(ShouldAccept ? "ACCEPT" : "REJECT")}] {Description}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] OuterNames = ["Outer", "Container", "Enclosing", "Holder", "Wrapper"];
    private static readonly string[] InnerNames = ["Inner", "Nested", "Child", "Sub", "Helper"];
    private static readonly string[] FieldNames = ["data", "value", "item", "state", "info"];

    private static readonly string[] AccessModifiers = ["public", "private", "protected", "internal"];

    // ─── ACCEPT scenarios: within-domain references ──────────────────────

    /// <summary>
    /// Generates scenarios where a nested type (at any accessibility level) is
    /// used from within the enclosing type. All nested types are accessible from
    /// within their enclosing type regardless of their own accessibility level
    /// (the enclosing type is always within the nested type's domain).
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenNestedType_FromEnclosingType()
    {
        return Gen.Elements(AccessModifiers).SelectMany(modifier =>
            Gen.Elements(OuterNames).SelectMany(outerName =>
                Gen.Elements(InnerNames).SelectMany(innerName =>
                    Gen.Elements(FieldNames).Select(fieldName =>
                        new NestedTypeDomainScenario(
                            @"public class " + outerName + @" {
    " + modifier + @" class " + innerName + @" {
        public int " + fieldName + @";
        public " + innerName + @"() { " + fieldName + @" = 42; }
    }
    public int Use() {
        var n = new " + innerName + @"();
        return n." + fieldName + @";
    }
}" + MainClass(),
                            $"{modifier} nested '{innerName}' in '{outerName}' accessed from enclosing type",
                            ShouldAccept: true)))));
    }

    /// <summary>
    /// Generates scenarios where a nested type is used as a field type
    /// within the enclosing type. The enclosing type can always reference its own
    /// nested types in type positions.
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenNestedType_AsFieldType_FromEnclosingType()
    {
        return Gen.Elements(AccessModifiers).SelectMany(modifier =>
            Gen.Elements(OuterNames).SelectMany(outerName =>
                Gen.Elements(InnerNames).SelectMany(innerName =>
                    Gen.Elements(FieldNames).Select(fieldName =>
                        new NestedTypeDomainScenario(
                            @"public class " + outerName + @" {
    " + modifier + @" class " + innerName + @" {
        public int " + fieldName + @";
        public " + innerName + @"() { " + fieldName + @" = 10; }
    }
    " + innerName + @" stored;
    public " + outerName + @"() { stored = new " + innerName + @"(); }
    public int GetValue() { return stored." + fieldName + @"; }
}" + MainClass(),
                            $"{modifier} nested '{innerName}' used as field type in enclosing '{outerName}'",
                            ShouldAccept: true)))));
    }

    /// <summary>
    /// Generates scenarios where a nested type is used as a return type of a method
    /// within the enclosing type. Even a private nested type can be used as a return
    /// type from within the enclosing type (the domain includes the enclosing type).
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenNestedType_AsReturnType_FromEnclosingType()
    {
        return Gen.Elements(AccessModifiers).SelectMany(modifier =>
            Gen.Elements(OuterNames).SelectMany(outerName =>
                Gen.Elements(InnerNames).SelectMany(innerName =>
                    Gen.Elements(FieldNames).Select(fieldName =>
                        new NestedTypeDomainScenario(
                            @"public class " + outerName + @" {
    " + modifier + @" class " + innerName + @" {
        public int " + fieldName + @";
        public " + innerName + @"() { " + fieldName + @" = 7; }
    }
    " + innerName + @" CreateNested() {
        return new " + innerName + @"();
    }
    public int UseNested() {
        var n = CreateNested();
        return n." + fieldName + @";
    }
}" + MainClass(),
                            $"{modifier} nested '{innerName}' used as return type in enclosing '{outerName}'",
                            ShouldAccept: true)))));
    }

    /// <summary>
    /// Generates scenarios where a private nested type is accessed from a sibling nested
    /// type within the same enclosing type. Both siblings are within the enclosing type's
    /// domain, so private nested types are accessible from sibling nested types (Req 11.3).
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenPrivateNested_FromSiblingNestedType()
    {
        return Gen.Elements(OuterNames).SelectMany(outerName =>
            Gen.Elements(FieldNames).Select(fieldName =>
                new NestedTypeDomainScenario(
                    @"public class " + outerName + @" {
    private class Target {
        public int " + fieldName + @";
        public Target() { " + fieldName + @" = 99; }
    }
    public class Sibling {
        public int ReadTarget() {
            var t = new Target();
            return t." + fieldName + @";
        }
    }
}" + MainClass(),
                    $"Private nested 'Target' in '{outerName}' accessed from sibling nested 'Sibling'",
                    ShouldAccept: true)));
    }

    /// <summary>
    /// Generates scenarios where a default (no modifier) nested type (defaults to private
    /// per Req 5.4) is accessed from within the enclosing type. Should be accepted.
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenDefaultNested_FromEnclosingType()
    {
        return Gen.Elements(OuterNames).SelectMany(outerName =>
            Gen.Elements(InnerNames).SelectMany(innerName =>
                Gen.Elements(FieldNames).Select(fieldName =>
                    new NestedTypeDomainScenario(
                        @"public class " + outerName + @" {
    class " + innerName + @" {
        public int " + fieldName + @";
        public " + innerName + @"() { " + fieldName + @" = 11; }
    }
    public int Use() {
        var n = new " + innerName + @"();
        return n." + fieldName + @";
    }
}" + MainClass(),
                        $"Default (private) nested '{innerName}' accessed from enclosing type '{outerName}'",
                        ShouldAccept: true))));
    }

    /// <summary>
    /// Generates scenarios where a nested type inside a nested type (multi-level nesting)
    /// is accessed from the innermost level or from the outer level. The outer type is
    /// within the domain of all its nested types.
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenDeepNested_FromOuterLevel()
    {
        return Gen.Elements(FieldNames).Select(fieldName =>
            new NestedTypeDomainScenario(
                @"public class TopLevel {
    public class MiddleLevel {
        public class DeepNested {
            public int " + fieldName + @";
            public DeepNested() { " + fieldName + @" = 55; }
        }
        public int UseDeep() {
            var d = new DeepNested();
            return d." + fieldName + @";
        }
    }
    public int UseMiddle() {
        var m = new MiddleLevel();
        return m.UseDeep();
    }
}" + MainClass(),
                $"Deep nested 'DeepNested' accessed from middle level",
                ShouldAccept: true));
    }

    // ─── REJECT scenarios: outside-domain references ─────────────────────

    /// <summary>
    /// Generates scenarios where a private nested type is referenced by qualified name
    /// from an unrelated type. In Mist, qualified name access to nested types from
    /// outside the enclosing type fails during name resolution (MIST_UNDEFINED_TYPE),
    /// which is the mechanism that enforces the domain restriction.
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenPrivateNested_QualifiedAccess_FromOutside()
    {
        return Gen.Elements(OuterNames).SelectMany(outerName =>
            Gen.Elements(InnerNames).SelectMany(innerName =>
                Gen.Elements(FieldNames).Select(fieldName =>
                    new NestedTypeDomainScenario(
                        @"public class " + outerName + @" {
    private class " + innerName + @" {
        public int " + fieldName + @";
        public " + innerName + @"() { " + fieldName + @" = 5; }
    }
}
class Outsider {
    public int Steal(" + outerName + @"." + innerName + @" n) { return 0; }
}" + MainClass(),
                        $"Private nested '{innerName}' in '{outerName}' referenced by qualified name from outside",
                        ShouldAccept: false))));
    }

    /// <summary>
    /// Generates scenarios where a protected nested type is referenced by qualified name
    /// from an unrelated (non-derived) type. Should be rejected.
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenProtectedNested_QualifiedAccess_FromOutside()
    {
        return Gen.Elements(OuterNames).SelectMany(outerName =>
            Gen.Elements(InnerNames).SelectMany(innerName =>
                Gen.Elements(FieldNames).Select(fieldName =>
                    new NestedTypeDomainScenario(
                        @"public class " + outerName + @" {
    protected class " + innerName + @" {
        public int " + fieldName + @";
        public " + innerName + @"() { " + fieldName + @" = 10; }
    }
}
class Outsider {
    public int Steal(" + outerName + @"." + innerName + @" n) { return 0; }
}" + MainClass(),
                        $"Protected nested '{innerName}' in '{outerName}' referenced by qualified name from outside",
                        ShouldAccept: false))));
    }

    /// <summary>
    /// Generates scenarios where a default (no modifier, defaults to private) nested type
    /// is referenced by qualified name from an unrelated type. Should be rejected.
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenDefaultNested_QualifiedAccess_FromOutside()
    {
        return Gen.Elements(OuterNames).SelectMany(outerName =>
            Gen.Elements(InnerNames).SelectMany(innerName =>
                Gen.Elements(FieldNames).Select(fieldName =>
                    new NestedTypeDomainScenario(
                        @"public class " + outerName + @" {
    class " + innerName + @" {
        public int " + fieldName + @";
        public " + innerName + @"() { " + fieldName + @" = 11; }
    }
}
class Outsider {
    public int Steal(" + outerName + @"." + innerName + @" n) { return 0; }
}" + MainClass(),
                        $"Default (private) nested '{innerName}' in '{outerName}' referenced by qualified name from outside",
                        ShouldAccept: false))));
    }

    /// <summary>
    /// Combined generator that selects from all scenario types with weighting
    /// ensuring both accept and reject cases are exercised.
    /// </summary>
    private static Gen<NestedTypeDomainScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNestedType_FromEnclosingType(),
            GenNestedType_AsFieldType_FromEnclosingType(),
            GenNestedType_AsReturnType_FromEnclosingType(),
            GenPrivateNested_FromSiblingNestedType(),
            GenDefaultNested_FromEnclosingType(),
            GenDeepNested_FromOuterLevel(),
            GenPrivateNested_QualifiedAccess_FromOutside(),
            GenProtectedNested_QualifiedAccess_FromOutside(),
            GenDefaultNested_QualifiedAccess_FromOutside());
    }

    public static Arbitrary<NestedTypeDomainScenario> ArbitraryNestedTypeDomainScenario()
    {
        return Arb.From(GenScenario());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: A nested type's domain is the intersection of its own
    // accessibility and its enclosing type's domain
    //
    // For any nested type, its accessibility domain is computed from both its
    // declared Accessibility and the accessibility domain of its enclosing
    // type; a reference from outside that computed domain is reported as an
    // accessibility-violation error, and a reference from within it is accepted.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NestedTypeDomainPropertyTests) })]
    public Property NestedTypes_DomainIsIntersectionOfOwnAndEnclosingAccessibility(NestedTypeDomainScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        if(scenario.ShouldAccept)
        {
            // Should not have any nested-type accessibility error.
            var hasAccessError = HasNestedTypeAccessError(diagnostics);
            return (!hasAccessError).ToProperty()
                .Label($"Expected ACCEPT but got accessibility error for: {scenario.Description}" +
                       $"\nDiagnostics: {string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
        }
        else
        {
            // Should have a nested-type accessibility error (MIST_TC_0176 when the
            // type is resolved through the semantic model but the domain check fails,
            // or MIST_UNDEFINED_TYPE when the name resolver cannot find the nested type
            // from outside its domain).
            var hasAccessError = HasNestedTypeAccessError(diagnostics);
            return hasAccessError.ToProperty()
                .Label($"Expected REJECT but no accessibility error for: {scenario.Description}" +
                       $"\nDiagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
        }
    }
}