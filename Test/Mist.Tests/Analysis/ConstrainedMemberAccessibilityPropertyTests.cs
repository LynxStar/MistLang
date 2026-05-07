// Feature: generic-constraints, Property 5: Constrained Member Accessibility
//
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.7**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Represents a scenario for testing constrained member accessibility.
/// </summary>
public sealed class ConstrainedMemberAccessScenario
{
    public string Source { get; }
    public string Description { get; }
    public bool ShouldAccept { get; }

    public ConstrainedMemberAccessScenario(string source, string description, bool shouldAccept)
    {
        Source = source;
        Description = description;
        ShouldAccept = shouldAccept;
    }

    public override string ToString() => $"[{(ShouldAccept ? "ACCEPT" : "REJECT")}] {Description}";
}

/// <summary>
/// Property-based tests verifying constrained member accessibility.
///
/// Property 5: For any type parameter with interface, trait, or base class constraints,
/// all members defined by those constraints (and only those members) SHALL be accessible
/// on values of that type parameter within the generic body.
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.7**
/// </summary>
public class ConstrainedMemberAccessibilityPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static (ISemanticModel? Model, ImmutableArray<Diagnostic> AllDiagnostics) CompileSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if (parseResult.Root is null)
            return (null, lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray()));

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

        var allDiags = semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
        return (model, allDiags);
    }

    private static bool HasMemberAccessError(ImmutableArray<Diagnostic> diagnostics)
    {
        // Check for member-not-found diagnostics or type checker errors related to member access
        return diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            (d.Id.Value.Contains("MEMBER") ||
             d.Id.Value.Contains("TC") ||
             d.Message.Contains("does not contain") ||
             d.Message.Contains("not available") ||
             d.Message.Contains("no member") ||
             d.Message.Contains("cannot resolve")));
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] MethodNames = ["DoWork", "Execute", "Process"];

    /// <summary>
    /// Generates scenarios where interface constraint provides accessible method.
    /// Requirement 6.1: Interface constraint makes interface members accessible.
    /// </summary>
    private static Gen<ConstrainedMemberAccessScenario> GenInterfaceMethodAccessible()
    {
        return from methodName in Gen.Elements(MethodNames)
               select new ConstrainedMemberAccessScenario(
                   $@"
interface IWorker {{
    int {methodName}();
}}
class Handler<T> where T : IWorker {{
    public int Handle(T item) {{
        return item.{methodName}();
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   $"Interface method {methodName} accessible via constraint",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where trait constraint provides accessible method.
    /// Requirement 6.2: Trait constraint makes trait members accessible.
    /// </summary>
    private static Gen<ConstrainedMemberAccessScenario> GenTraitMethodAccessible()
    {
        return from methodName in Gen.Elements(MethodNames)
               select new ConstrainedMemberAccessScenario(
                   $@"
trait Worker {{
    int {methodName}();
}}
class Handler<T> where T : Worker {{
    public int Handle(T item) {{
        return item.{methodName}();
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   $"Trait method {methodName} accessible via constraint",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where base class constraint provides accessible method.
    /// Requirement 6.3: Base class constraint makes base class members accessible.
    /// </summary>
    private static Gen<ConstrainedMemberAccessScenario> GenBaseClassMethodAccessible()
    {
        return from methodName in Gen.Elements(MethodNames)
               select new ConstrainedMemberAccessScenario(
                   $@"
class BaseWorker {{
    public int {methodName}() {{
        return 42;
    }}
}}
class Handler<T> where T : BaseWorker {{
    public int Handle(T item) {{
        return item.{methodName}();
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   $"Base class method {methodName} accessible via constraint",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where multiple interface constraints provide accessible members.
    /// Requirement 6.4: Multiple interface constraints make all members accessible.
    /// </summary>
    private static Gen<ConstrainedMemberAccessScenario> GenMultipleInterfaceMethodsAccessible()
    {
        return Gen.Constant(new ConstrainedMemberAccessScenario(
            @"
interface IReader {
    int Read();
}
interface IWriter {
    int Write();
}
class Handler<T> where T : IReader, IWriter {
    public int HandleRead(T item) {
        return item.Read();
    }
    public int HandleWrite(T item) {
        return item.Write();
    }
}
class Program {
    public static void Main() { }
}",
            "Multiple interface methods accessible via multiple constraints",
            shouldAccept: true));
    }

    /// <summary>
    /// Generates scenarios where a member NOT provided by any constraint is accessed.
    /// Requirement 6.7: Member not available on unconstrained type parameter.
    /// </summary>
    private static Gen<ConstrainedMemberAccessScenario> GenUnavailableMemberRejected()
    {
        return from methodName in Gen.Elements("NonExistent", "Missing", "Unknown")
               select new ConstrainedMemberAccessScenario(
                   $@"
interface IWorker {{
    int DoWork();
}}
class Handler<T> where T : IWorker {{
    public int Handle(T item) {{
        return item.{methodName}();
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   $"Method {methodName} NOT in constraint should be rejected",
                   shouldAccept: false);
    }

    /// <summary>
    /// Generates scenarios where unconstrained type parameter has no accessible members.
    /// Requirement 6.7: No constraint means no members accessible.
    /// </summary>
    private static Gen<ConstrainedMemberAccessScenario> GenUnconstrainedMemberRejected()
    {
        return from methodName in Gen.Elements(MethodNames)
               select new ConstrainedMemberAccessScenario(
                   $@"
class Handler<T> {{
    public int Handle(T item) {{
        return item.{methodName}();
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   $"Method {methodName} on unconstrained T should be rejected",
                   shouldAccept: false);
    }

    /// <summary>
    /// Generates scenarios where base class inherited members are accessible.
    /// Requirement 6.3: Base class constraint includes inherited members.
    /// </summary>
    private static Gen<ConstrainedMemberAccessScenario> GenInheritedBaseClassMethodAccessible()
    {
        return from methodName in Gen.Elements(MethodNames)
               select new ConstrainedMemberAccessScenario(
                   $@"
class GrandParent {{
    public int {methodName}() {{
        return 1;
    }}
}}
class Parent : GrandParent {{ }}
class Handler<T> where T : Parent {{
    public int Handle(T item) {{
        return item.{methodName}();
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   $"Inherited method {methodName} accessible via base class constraint",
                   shouldAccept: true);
    }

    private static Gen<ConstrainedMemberAccessScenario> GenScenario()
    {
        return Gen.OneOf(
            GenInterfaceMethodAccessible(),
            GenTraitMethodAccessible(),
            GenBaseClassMethodAccessible(),
            GenMultipleInterfaceMethodsAccessible(),
            GenUnavailableMemberRejected(),
            GenUnconstrainedMemberRejected(),
            GenInheritedBaseClassMethodAccessible());
    }

    public static Arbitrary<ConstrainedMemberAccessScenario> ArbitraryConstrainedMemberAccessScenario() =>
        Arb.From(GenScenario());

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Constrained Member Accessibility
    //
    // For any type parameter with interface, trait, or base class constraints,
    // all members defined by those constraints (and only those members) SHALL
    // be accessible on values of that type parameter within the generic body.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConstrainedMemberAccessibilityPropertyTests) })]
    public Property ConstrainedMemberAccess_AcceptRejectMatchesConstraintMembers(ConstrainedMemberAccessScenario scenario)
    {
        var (model, diagnostics) = CompileSource(scenario.Source);

        var hasMemberError = HasMemberAccessError(diagnostics);

        // If we expect acceptance, there should be no member access errors
        // If we expect rejection, there should be a member access error
        var result = scenario.ShouldAccept ? !hasMemberError : hasMemberError;

        return result.ToProperty()
            .Label($"Expected {(scenario.ShouldAccept ? "ACCEPT" : "REJECT")}, " +
                   $"got {(hasMemberError ? "REJECT" : "ACCEPT")} " +
                   $"for: {scenario.Description}" +
                   (diagnostics.Any() ? $"\nDiagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}" : ""));
    }
}
