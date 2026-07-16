// Feature: full-accessibility-model, Property 7: Private members are reachable only within the declaring type's domain
//
// **Validates: Requirements 7.1, 7.4, 11.3 (Property 7)**

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
/// Property-based tests for private member accessibility domain enforcement.
///
/// Property 7: Private members are reachable only within the declaring type's domain.
///
/// For any Private member, a reference from outside the accessibility domain of the
/// member's declaring type is reported as an accessibility-violation error, and a
/// reference from within that domain (including from a nested type of the same
/// declaring type) is accepted without an accessibility diagnostic.
///
/// **Validates: Requirements 7.1, 7.4, 11.3 (Property 7)**
/// </summary>
public class PrivateMemberDomainPropertyTests
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

    private static bool HasPrivateAccessError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            (d.Id.Value == "MIST_TC_0014" || d.Id.Value == "MIST_TC_0170"));
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
    /// A generated scenario for testing private member domain enforcement.
    /// </summary>
    public sealed record PrivateDomainScenario(
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

    private static readonly string[] MemberNames = ["secret", "hidden", "data", "info", "state"];
    private static readonly string[] MethodNames = ["GetSecret", "Compute", "Process", "Calculate", "Resolve"];
    private static readonly string[] ClassNames = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    /// <summary>
    /// Generates scenarios where a private field is accessed from within the declaring class.
    /// This should always be accepted (Req 7.4 -- reference within domain).
    /// </summary>
    private static Gen<PrivateDomainScenario> GenPrivateFieldAccessWithinDeclaringType()
    {
        return Gen.Elements(MemberNames).SelectMany(fieldName =>
            Gen.Elements(MethodNames).Select(methodName =>
                new PrivateDomainScenario(
                    @"class Owner {
    private int " + fieldName + @";
    public Owner() { " + fieldName + @" = 42; }
    public int " + methodName + @"() { return " + fieldName + @"; }
}" + MainClass(),
                    $"Private field '{fieldName}' accessed within declaring type via '{methodName}'",
                    ShouldAccept: true)));
    }

    /// <summary>
    /// Generates scenarios where a private method is accessed from within the declaring class.
    /// This should always be accepted (Req 7.4 -- reference within domain).
    /// </summary>
    private static Gen<PrivateDomainScenario> GenPrivateMethodAccessWithinDeclaringType()
    {
        return Gen.Elements(MethodNames).Select(privateName =>
            new PrivateDomainScenario(
                @"class Owner {
    private int " + privateName + @"() { return 1; }
    public int Caller() { return " + privateName + @"(); }
}" + MainClass(),
                $"Private method '{privateName}' called within declaring type",
                ShouldAccept: true));
    }

    /// <summary>
    /// Generates scenarios where a private field is accessed from a nested type of the
    /// same declaring type. This should be accepted (Req 11.3 -- nested types are
    /// within the declaring type's domain).
    /// </summary>
    private static Gen<PrivateDomainScenario> GenPrivateFieldAccessFromNestedType()
    {
        return Gen.Elements(MemberNames).Select(fieldName =>
            new PrivateDomainScenario(
                @"class Owner {
    private int " + fieldName + @";
    public Owner() { " + fieldName + @" = 10; }
    public class Nested {
        public int ReadOwner(Owner o) { return o." + fieldName + @"; }
    }
}" + MainClass(),
                $"Private field '{fieldName}' accessed from nested type of declaring class",
                ShouldAccept: true));
    }

    /// <summary>
    /// Generates scenarios where a private method is accessed from a nested type of the
    /// same declaring type. This should be accepted (Req 11.3).
    /// </summary>
    private static Gen<PrivateDomainScenario> GenPrivateMethodAccessFromNestedType()
    {
        return Gen.Elements(MethodNames).Select(methodName =>
            new PrivateDomainScenario(
                @"class Owner {
    private int " + methodName + @"() { return 99; }
    public class Nested {
        public int Invoke(Owner o) { return o." + methodName + @"(); }
    }
}" + MainClass(),
                $"Private method '{methodName}' called from nested type of declaring class",
                ShouldAccept: true));
    }

    /// <summary>
    /// Generates scenarios where a private field is accessed from an unrelated class.
    /// This should be rejected (Req 7.1 -- private outside domain).
    /// </summary>
    private static Gen<PrivateDomainScenario> GenPrivateFieldAccessFromUnrelatedType()
    {
        return Gen.Elements(MemberNames).SelectMany(fieldName =>
            Gen.Elements(ClassNames).Select(className =>
                new PrivateDomainScenario(
                    @"class Owner {
    private int " + fieldName + @";
    public Owner() { " + fieldName + @" = 5; }
}
class " + className + @" {
    public int Steal(Owner o) { return o." + fieldName + @"; }
}" + MainClass(),
                    $"Private field '{fieldName}' accessed from unrelated class '{className}'",
                    ShouldAccept: false)));
    }

    /// <summary>
    /// Generates scenarios where a private method is accessed from an unrelated class.
    /// This should be rejected (Req 7.1 -- private outside domain).
    /// </summary>
    private static Gen<PrivateDomainScenario> GenPrivateMethodAccessFromUnrelatedType()
    {
        return Gen.Elements(MethodNames).SelectMany(methodName =>
            Gen.Elements(ClassNames).Select(className =>
                new PrivateDomainScenario(
                    @"class Owner {
    private int " + methodName + @"() { return 7; }
}
class " + className + @" {
    public int Steal(Owner o) { return o." + methodName + @"(); }
}" + MainClass(),
                    $"Private method '{methodName}' called from unrelated class '{className}'",
                    ShouldAccept: false)));
    }

    /// <summary>
    /// Generates scenarios where a default (no modifier) field -- which defaults to private --
    /// is accessed from an unrelated class. Should be rejected.
    /// </summary>
    private static Gen<PrivateDomainScenario> GenDefaultFieldAccessFromUnrelatedType()
    {
        return Gen.Elements(MemberNames).SelectMany(fieldName =>
            Gen.Elements(ClassNames).Select(className =>
                new PrivateDomainScenario(
                    @"class Owner {
    int " + fieldName + @";
    public Owner() { " + fieldName + @" = 3; }
}
class " + className + @" {
    public int Steal(Owner o) { return o." + fieldName + @"; }
}" + MainClass(),
                    $"Default (private) field '{fieldName}' accessed from unrelated class '{className}'",
                    ShouldAccept: false)));
    }

    /// <summary>
    /// Generates scenarios where a private field is accessed from a derived (but
    /// not nested) class via an instance reference. Private members are not
    /// accessible from derived types (only protected are). Should be rejected.
    /// </summary>
    private static Gen<PrivateDomainScenario> GenPrivateFieldAccessFromDerivedType()
    {
        return Gen.Elements(MemberNames).Select(fieldName =>
            new PrivateDomainScenario(
                @"class Base {
    private int " + fieldName + @";
    public Base() { " + fieldName + @" = 0; }
}
class Derived : Base {
    public Derived() : base() { }
    public int Read(Base b) { return b." + fieldName + @"; }
}" + MainClass(),
                $"Private field '{fieldName}' accessed from derived (non-nested) class",
                ShouldAccept: false));
    }

    /// <summary>
    /// Combined generator that selects from all scenario types with weighting
    /// ensuring both accept and reject cases are exercised.
    /// </summary>
    private static Gen<PrivateDomainScenario> GenScenario()
    {
        return Gen.OneOf(
            GenPrivateFieldAccessWithinDeclaringType(),
            GenPrivateMethodAccessWithinDeclaringType(),
            GenPrivateFieldAccessFromNestedType(),
            GenPrivateMethodAccessFromNestedType(),
            GenPrivateFieldAccessFromUnrelatedType(),
            GenPrivateMethodAccessFromUnrelatedType(),
            GenDefaultFieldAccessFromUnrelatedType(),
            GenPrivateFieldAccessFromDerivedType());
    }

    public static Arbitrary<PrivateDomainScenario> ArbitraryPrivateDomainScenario()
    {
        return Arb.From(GenScenario());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Private members are reachable only within the declaring
    // type's domain
    //
    // For any Private member, a reference from outside the accessibility
    // domain of the member's declaring type is reported as an accessibility-
    // violation error (MIST_TC_0014 or MIST_TC_0170), and a reference from
    // within that domain (including from a nested type of the same declaring
    // type) is accepted without an accessibility diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(PrivateMemberDomainPropertyTests) })]
    public Property PrivateMembers_ReachableOnlyWithinDeclaringTypeDomain(PrivateDomainScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasPrivateAccessViolation = HasPrivateAccessError(diagnostics);

        if(scenario.ShouldAccept)
        {
            // Should not have a private access error.
            return (!hasPrivateAccessViolation).ToProperty()
                .Label($"Expected ACCEPT but got private-access error for: {scenario.Description}" +
                       $"\nDiagnostics: {string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
        }
        else
        {
            // Should have a private access error.
            return hasPrivateAccessViolation.ToProperty()
                .Label($"Expected REJECT but no private-access error for: {scenario.Description}" +
                       $"\nDiagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
        }
    }
}