// Feature: generic-constraints, Property 6: Constraint Propagation Through Inheritance
// **Validates: Requirements 7.1, 7.2, 7.3**

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
/// Represents a scenario for testing constraint propagation through inheritance.
/// </summary>
public sealed class ConstraintPropagationScenario
{
    public string Source { get; }
    public string Description { get; }
    public bool ShouldAccept { get; }

    public ConstraintPropagationScenario(string source, string description, bool shouldAccept)
    {
        Source = source;
        Description = description;
        ShouldAccept = shouldAccept;
    }

    public override string ToString()
    {
        return $"[{(ShouldAccept ? "ACCEPT" : "REJECT")}] {Description}";
    }
}

/// <summary>
/// Property-based tests verifying constraint propagation through inheritance.
///
/// Property 6: For any generic type Derived&lt;T&gt; that inherits from a constrained
/// generic type Base&lt;T&gt;, the compiler SHALL accept the inheritance if and only if
/// the constraints on Derived's type parameter T are at least as restrictive as the
/// constraints on Base's type parameter T.
///
/// **Validates: Requirements 7.1, 7.2, 7.3**
/// </summary>
public class ConstraintPropagationPropertyTests
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

        if(parseResult.Root is null)
        {
            return (null!, lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray()));
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

        var allDiags = semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
        return (model, allDiags);
    }

    private static bool HasPropagationViolation(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.Id.Value == "MIST_CONSTRAINT_0017");
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios where derived type has the same class constraint as base — accepted.
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenClassConstraintPropagated()
    {
        return from baseName in Gen.Elements("Base", "Parent", "Root")
               from derivedName in Gen.Elements("Derived", "Child", "Sub")
               select new ConstraintPropagationScenario(
                   $@"class {baseName}<T> where T : class {{ }}
class {derivedName}<T> : {baseName}<T> where T : class {{ }}",
                   $"{derivedName}<T> : {baseName}<T> — both have class constraint",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where derived type is missing the class constraint from base — rejected.
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenClassConstraintMissing()
    {
        return from baseName in Gen.Elements("Base", "Parent", "Root")
               from derivedName in Gen.Elements("Derived", "Child", "Sub")
               select new ConstraintPropagationScenario(
                   $@"class {baseName}<T> where T : class {{ }}
class {derivedName}<T> : {baseName}<T> {{ }}",
                   $"{derivedName}<T> : {baseName}<T> — derived missing class constraint",
                   shouldAccept: false);
    }

    /// <summary>
    /// Generates scenarios where derived type has the same struct constraint as base — accepted.
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenStructConstraintPropagated()
    {
        return from baseName in Gen.Elements("Base", "Parent", "Root")
               from derivedName in Gen.Elements("Derived", "Child", "Sub")
               select new ConstraintPropagationScenario(
                   $@"class {baseName}<T> where T : struct {{ }}
class {derivedName}<T> : {baseName}<T> where T : struct {{ }}",
                   $"{derivedName}<T> : {baseName}<T> — both have struct constraint",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where derived type is missing the struct constraint from base — rejected.
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenStructConstraintMissing()
    {
        return from baseName in Gen.Elements("Base", "Parent", "Root")
               from derivedName in Gen.Elements("Derived", "Child", "Sub")
               select new ConstraintPropagationScenario(
                   $@"class {baseName}<T> where T : struct {{ }}
class {derivedName}<T> : {baseName}<T> {{ }}",
                   $"{derivedName}<T> : {baseName}<T> — derived missing struct constraint",
                   shouldAccept: false);
    }

    /// <summary>
    /// Generates scenarios where derived type has the same abstract type constraint as base — accepted.
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenAbstractConstraintPropagated()
    {
        return from baseName in Gen.Elements("Base", "Parent", "Root")
               from derivedName in Gen.Elements("Derived", "Child", "Sub")
               from abstractName in Gen.Elements("IFoo", "IBar", "IBaz")
               select new ConstraintPropagationScenario(
                   $@"abstract {abstractName} {{ }}
class {baseName}<T> where T : {abstractName} {{ }}
class {derivedName}<T> : {baseName}<T> where T : {abstractName} {{ }}",
                   $"{derivedName}<T> : {baseName}<T> — both have {abstractName} constraint",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where derived type is missing the abstract type constraint from base — rejected.
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenAbstractConstraintMissing()
    {
        return from baseName in Gen.Elements("Base", "Parent", "Root")
               from derivedName in Gen.Elements("Derived", "Child", "Sub")
               from abstractName in Gen.Elements("IFoo", "IBar", "IBaz")
               select new ConstraintPropagationScenario(
                   $@"abstract {abstractName} {{ }}
class {baseName}<T> where T : {abstractName} {{ }}
class {derivedName}<T> : {baseName}<T> {{ }}",
                   $"{derivedName}<T> : {baseName}<T> — derived missing {abstractName} constraint",
                   shouldAccept: false);
    }

    /// <summary>
    /// Generates scenarios where derived type has a more restrictive constraint set — accepted.
    /// (Derived has class + abstract type, base only has class.)
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenMoreRestrictiveDerived()
    {
        return from baseName in Gen.Elements("Base", "Parent", "Root")
               from derivedName in Gen.Elements("Derived", "Child", "Sub")
               from abstractName in Gen.Elements("IFoo", "IBar", "IBaz")
               select new ConstraintPropagationScenario(
                   $@"abstract {abstractName} {{ }}
class {baseName}<T> where T : class {{ }}
class {derivedName}<T> : {baseName}<T> where T : class, {abstractName} {{ }}",
                   $"{derivedName}<T> : {baseName}<T> — derived is more restrictive (class + {abstractName})",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where derived type has new() constraint matching base — accepted.
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenNewConstraintPropagated()
    {
        return from baseName in Gen.Elements("Base", "Parent", "Root")
               from derivedName in Gen.Elements("Derived", "Child", "Sub")
               select new ConstraintPropagationScenario(
                   $@"class {baseName}<T> where T : new() {{ }}
class {derivedName}<T> : {baseName}<T> where T : new() {{ }}",
                   $"{derivedName}<T> : {baseName}<T> — both have new() constraint",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where derived type is missing new() constraint from base — rejected.
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenNewConstraintMissing()
    {
        return from baseName in Gen.Elements("Base", "Parent", "Root")
               from derivedName in Gen.Elements("Derived", "Child", "Sub")
               select new ConstraintPropagationScenario(
                   $@"class {baseName}<T> where T : new() {{ }}
class {derivedName}<T> : {baseName}<T> {{ }}",
                   $"{derivedName}<T> : {baseName}<T> — derived missing new() constraint",
                   shouldAccept: false);
    }

    /// <summary>
    /// Generates scenarios for abstract type inheritance propagation — accepted.
    /// </summary>
    private static Gen<ConstraintPropagationScenario> GenAbstractInheritancePropagated()
    {
        return from baseAbstractName in Gen.Elements("IBase", "IParent", "IRoot")
               from derivedAbstractName in Gen.Elements("IDerived", "IChild", "ISub")
               from constraintAbstract in Gen.Elements("IFoo", "IBar", "IBaz")
               select new ConstraintPropagationScenario(
                   $@"abstract {constraintAbstract} {{ }}
abstract {baseAbstractName}<T> where T : {constraintAbstract} {{ }}
abstract {derivedAbstractName}<T> : {baseAbstractName}<T> where T : {constraintAbstract} {{ }}",
                   $"{derivedAbstractName}<T> : {baseAbstractName}<T> — abstract type inheritance with {constraintAbstract}",
                   shouldAccept: true);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ConstraintPropagationScenario> GenScenario()
    {
        return Gen.OneOf(
            GenClassConstraintPropagated(),
            GenClassConstraintMissing(),
            GenStructConstraintPropagated(),
            GenStructConstraintMissing(),
            GenAbstractConstraintPropagated(),
            GenAbstractConstraintMissing(),
            GenMoreRestrictiveDerived(),
            GenNewConstraintPropagated(),
            GenNewConstraintMissing(),
            GenAbstractInheritancePropagated());
    }

    public static Arbitrary<ConstraintPropagationScenario> ArbitraryConstraintPropagationScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 6: Constraint Propagation Through Inheritance
    ///
    /// For any generic type Derived&lt;T&gt; that inherits from a constrained generic type
    /// Base&lt;T&gt;, the compiler SHALL accept the inheritance if and only if the constraints
    /// on Derived's type parameter T are at least as restrictive as the constraints on
    /// Base's type parameter T.
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConstraintPropagationPropertyTests) })]
    public Property ConstraintPropagation_AcceptRejectMatchesRestrictiveness(ConstraintPropagationScenario scenario)
    {
        var (_, diagnostics) = CompileSource(scenario.Source);
        var hasViolation = HasPropagationViolation(diagnostics);

        if(scenario.ShouldAccept)
        {
            return (!hasViolation).ToProperty()
                .Label($"Expected ACCEPT but got MIST_CONSTRAINT_0017.\nSource:\n{scenario.Source}\nDiagnostics: {string.Join("; ", diagnostics.Where(d => d.Id.Value.StartsWith("MIST_CONSTRAINT")).Select(d => d.Message))}");
        }
        else
        {
            return hasViolation.ToProperty()
                .Label($"Expected REJECT (MIST_CONSTRAINT_0017) but none emitted.\nSource:\n{scenario.Source}");
        }
    }
}