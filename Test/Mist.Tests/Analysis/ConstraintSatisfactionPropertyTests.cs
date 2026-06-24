// Feature: generic-constraints, Property 2: Constraint Satisfaction Correctness
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10**

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
/// Represents a scenario for testing constraint satisfaction correctness.
/// </summary>
public sealed class ConstraintSatisfactionScenario
{
    public string Source { get; }
    public string Description { get; }
    public bool ShouldAccept { get; }

    public ConstraintSatisfactionScenario(string source, string description, bool shouldAccept)
    {
        Source = source;
        Description = description;
        ShouldAccept = shouldAccept;
    }

    public override string ToString() => $"[{(ShouldAccept ? "ACCEPT" : "REJECT")}] {Description}";
}

/// <summary>
/// Property-based tests verifying constraint satisfaction correctness.
///
/// Property 2: For any constrained generic type and any type argument, the type checker
/// SHALL accept the type argument if and only if it satisfies all declared constraints
/// (class/struct kind check, base class derivation, abstract type implementation
/// satisfaction, and at least one matching constructor for OR-semantics constructor constraints).
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10**
/// </summary>
public class ConstraintSatisfactionPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> AllDiagnostics) CompileSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if (parseResult.Root is null)
            return (null!, lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray()));

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

    private static bool HasConstraintViolation(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Id.Value is "MIST_CONSTRAINT_0009" or "MIST_CONSTRAINT_0010"
                or "MIST_CONSTRAINT_0011" or "MIST_CONSTRAINT_0012"
                or "MIST_CONSTRAINT_0013" or "MIST_CONSTRAINT_0014"
                or "MIST_CONSTRAINT_0015");
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNames = ["IFoo", "IBar", "IBaz"];
    private static readonly string[] ClassNames = ["Animal", "Vehicle", "Shape"];

    /// <summary>Generates scenarios where a class constraint is satisfied (reference type arg).</summary>
    private static Gen<ConstraintSatisfactionScenario> GenClassConstraintSatisfied()
    {
        return from className in Gen.Elements("MyClass", "Widget", "Service")
               select new ConstraintSatisfactionScenario(
                   $@"class {className} {{ }}
class Container<T> where T : class {{ }}
class Program {{
    public static void Main() {{
        Container<{className}> c = new Container<{className}>();
    }}
}}",
                   $"class constraint satisfied by {className}",
                   shouldAccept: true);
    }

    /// <summary>Generates scenarios where a class constraint is violated (struct type arg).</summary>
    private static Gen<ConstraintSatisfactionScenario> GenClassConstraintViolated()
    {
        return from structName in Gen.Elements("Point", "Coord", "Pixel")
               select new ConstraintSatisfactionScenario(
                   $@"struct {structName} {{ }}
class Container<T> where T : class {{ }}
class Program {{
    public static void Main() {{
        Container<{structName}> c = new Container<{structName}>();
    }}
}}",
                   $"class constraint violated by struct {structName}",
                   shouldAccept: false);
    }

    /// <summary>Generates scenarios where a struct constraint is satisfied.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenStructConstraintSatisfied()
    {
        return from structName in Gen.Elements("Point", "Coord", "Pixel")
               select new ConstraintSatisfactionScenario(
                   $@"struct {structName} {{ }}
class Container<T> where T : struct {{ }}
class Program {{
    public static void Main() {{
        Container<{structName}> c = new Container<{structName}>();
    }}
}}",
                   $"struct constraint satisfied by {structName}",
                   shouldAccept: true);
    }

    /// <summary>Generates scenarios where a struct constraint is violated.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenStructConstraintViolated()
    {
        return from className in Gen.Elements("MyClass", "Widget", "Service")
               select new ConstraintSatisfactionScenario(
                   $@"class {className} {{ }}
class Container<T> where T : struct {{ }}
class Program {{
    public static void Main() {{
        Container<{className}> c = new Container<{className}>();
    }}
}}",
                   $"struct constraint violated by class {className}",
                   shouldAccept: false);
    }

    /// <summary>Generates scenarios where an abstract type constraint is satisfied.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenAbstractConstraintSatisfied()
    {
        return from abstractName in Gen.Elements(AbstractNames)
               from implName in Gen.Elements("Impl", "Concrete", "Real")
               select new ConstraintSatisfactionScenario(
                   $@"abstract {abstractName} {{ }}
class {implName} : {abstractName} {{ }}
class Container<T> where T : {abstractName} {{ }}
class Program {{
    public static void Main() {{
        Container<{implName}> c = new Container<{implName}>();
    }}
}}",
                   $"abstract type constraint {abstractName} satisfied by {implName}",
                   shouldAccept: true);
    }

    /// <summary>Generates scenarios where an abstract type constraint is violated.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenAbstractConstraintViolated()
    {
        return from abstractName in Gen.Elements(AbstractNames)
               from nonImplName in Gen.Elements("Unrelated", "Other", "Wrong")
               select new ConstraintSatisfactionScenario(
                   $@"abstract {abstractName} {{ }}
class {nonImplName} {{ }}
class Container<T> where T : {abstractName} {{ }}
class Program {{
    public static void Main() {{
        Container<{nonImplName}> c = new Container<{nonImplName}>();
    }}
}}",
                   $"abstract type constraint {abstractName} violated by {nonImplName}",
                   shouldAccept: false);
    }

    /// <summary>Generates scenarios where a base class constraint is satisfied.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenBaseClassConstraintSatisfied()
    {
        return from baseName in Gen.Elements(ClassNames)
               from derivedName in Gen.Elements("Dog", "Car", "Circle")
               select new ConstraintSatisfactionScenario(
                   $@"class {baseName} {{ }}
class {derivedName} : {baseName} {{ }}
class Container<T> where T : {baseName} {{ }}
class Program {{
    public static void Main() {{
        Container<{derivedName}> c = new Container<{derivedName}>();
    }}
}}",
                   $"base class constraint {baseName} satisfied by {derivedName}",
                   shouldAccept: true);
    }

    /// <summary>Generates scenarios where a base class constraint is violated.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenBaseClassConstraintViolated()
    {
        return from baseName in Gen.Elements(ClassNames)
               from unrelatedName in Gen.Elements("Unrelated", "Other", "Wrong")
               select new ConstraintSatisfactionScenario(
                   $@"class {baseName} {{ }}
class {unrelatedName} {{ }}
class Container<T> where T : {baseName} {{ }}
class Program {{
    public static void Main() {{
        Container<{unrelatedName}> c = new Container<{unrelatedName}>();
    }}
}}",
                   $"base class constraint {baseName} violated by {unrelatedName}",
                   shouldAccept: false);
    }

    /// <summary>Generates scenarios where a new() constraint is satisfied.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenNewConstraintSatisfied()
    {
        return from className in Gen.Elements("Widget", "Service", "Handler")
               select new ConstraintSatisfactionScenario(
                   $@"class {className} {{ }}
class Factory<T> where T : new() {{ }}
class Program {{
    public static void Main() {{
        Factory<{className}> f = new Factory<{className}>();
    }}
}}",
                   $"new() constraint satisfied by {className} (implicit parameterless ctor)",
                   shouldAccept: true);
    }

    /// <summary>Generates scenarios where a parameterized new(int) constraint is satisfied.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenParameterizedNewConstraintSatisfied()
    {
        return from className in Gen.Elements("Widget", "Service", "Handler")
               select new ConstraintSatisfactionScenario(
                   $@"class {className} {{
    public {className}(int x) {{ }}
}}
class Factory<T> where T : new(int) {{ }}
class Program {{
    public static void Main() {{
        Factory<{className}> f = new Factory<{className}>();
    }}
}}",
                   $"new(int) constraint satisfied by {className}",
                   shouldAccept: true);
    }

    /// <summary>Generates scenarios where a parameterized new(int) constraint is violated.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenParameterizedNewConstraintViolated()
    {
        return from className in Gen.Elements("Widget", "Service", "Handler")
               select new ConstraintSatisfactionScenario(
                   $@"class {className} {{ }}
class Factory<T> where T : new(int) {{ }}
class Program {{
    public static void Main() {{
        Factory<{className}> f = new Factory<{className}>();
    }}
}}",
                   $"new(int) constraint violated by {className} (no int ctor)",
                   shouldAccept: false);
    }

    /// <summary>Generates scenarios where multiple constructor constraints are all satisfied.</summary>
    private static Gen<ConstraintSatisfactionScenario> GenMultipleCtorConstraintsSatisfied()
    {
        return from className in Gen.Elements("Widget", "Service", "Handler")
               select new ConstraintSatisfactionScenario(
                   $@"class {className} {{
    public {className}() {{ }}
    public {className}(int x) {{ }}
    public {className}(bool b) {{ }}
}}
class Factory<T> where T : new(), new(int), new(bool) {{ }}
class Program {{
    public static void Main() {{
        Factory<{className}> f = new Factory<{className}>();
    }}
}}",
                   $"multiple ctor constraints: {className} has new(), new(int), new(bool) — all satisfied",
                   shouldAccept: true);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ConstraintSatisfactionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenClassConstraintSatisfied(),
            GenClassConstraintViolated(),
            GenStructConstraintSatisfied(),
            GenStructConstraintViolated(),
            GenAbstractConstraintSatisfied(),
            GenAbstractConstraintViolated(),
            GenBaseClassConstraintSatisfied(),
            GenBaseClassConstraintViolated(),
            GenNewConstraintSatisfied(),
            GenParameterizedNewConstraintSatisfied(),
            GenParameterizedNewConstraintViolated(),
            GenMultipleCtorConstraintsSatisfied());
    }

    public static Arbitrary<ConstraintSatisfactionScenario> ArbitraryConstraintSatisfactionScenario() =>
        GenScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 2: Constraint Satisfaction Correctness
    ///
    /// For any constrained generic type and any type argument, the type checker SHALL
    /// accept the type argument if and only if it satisfies all declared constraints.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConstraintSatisfactionPropertyTests) })]
    public Property ConstraintSatisfaction_AcceptRejectMatchesConstraintRules(ConstraintSatisfactionScenario scenario)
    {
        var (_, diagnostics) = CompileSource(scenario.Source);
        var hasViolation = HasConstraintViolation(diagnostics);

        if (scenario.ShouldAccept)
        {
            return (!hasViolation).ToProperty()
                .Label($"Expected ACCEPT but got constraint violation.\nSource:\n{scenario.Source}\nDiagnostics: {string.Join("; ", diagnostics.Where(d => d.Id.Value.StartsWith("MIST_CONSTRAINT")).Select(d => d.Message))}");
        }
        else
        {
            return hasViolation.ToProperty()
                .Label($"Expected REJECT but no constraint violation was emitted.\nSource:\n{scenario.Source}");
        }
    }
}
