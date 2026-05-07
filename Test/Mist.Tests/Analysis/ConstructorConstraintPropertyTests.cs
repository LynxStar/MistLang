// Feature: generic-constraints, Property 4: Constructor Constraint new T(...) Validation
// **Validates: Requirements 6.5, 6.6**

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
/// Represents a scenario for testing new T(...) validation against constructor constraints.
/// </summary>
public sealed class ConstructorConstraintScenario
{
    public string Source { get; }
    public string Description { get; }
    public bool ShouldAccept { get; }

    public ConstructorConstraintScenario(string source, string description, bool shouldAccept)
    {
        Source = source;
        Description = description;
        ShouldAccept = shouldAccept;
    }

    public override string ToString() => $"[{(ShouldAccept ? "ACCEPT" : "REJECT")}] {Description}";
}

/// <summary>
/// Property-based tests verifying constructor constraint new T(...) validation.
///
/// Property 4: For any generic body containing a new T(...) expression where T has one
/// or more constructor constraints, the type checker SHALL accept the expression if and
/// only if the argument types match at least one of the declared constructor constraint
/// signatures.
///
/// **Validates: Requirements 6.5, 6.6**
/// </summary>
public class ConstructorConstraintPropertyTests
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

    private static bool HasNewTMismatch(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.Id.Value == "MIST_CONSTRAINT_0016");
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    /// <summary>
    /// Generates scenarios where new T() matches a parameterless constructor constraint.
    /// </summary>
    private static Gen<ConstructorConstraintScenario> GenNewTParameterlessAccepted()
    {
        return from className in Gen.Elements("Factory", "Builder", "Creator")
               select new ConstructorConstraintScenario(
                   $@"class {className}<T> where T : new() {{
    public T Create() {{
        return new T();
    }}
}}",
                   $"new T() with new() constraint in {className}",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where new T(int) matches a new(int) constructor constraint.
    /// </summary>
    private static Gen<ConstructorConstraintScenario> GenNewTSingleParamAccepted()
    {
        return from className in Gen.Elements("Factory", "Builder", "Creator")
               from paramType in Gen.Elements(PrimitiveTypes)
               select new ConstructorConstraintScenario(
                   $@"class {className}<T> where T : new({paramType}) {{
    public T Create({paramType} val) {{
        return new T(val);
    }}
}}",
                   $"new T({paramType}) with new({paramType}) constraint in {className}",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios where new T(args) does NOT match any declared constructor constraint.
    /// E.g., constraint is new(int) but call is new T(boolVal).
    /// </summary>
    private static Gen<ConstructorConstraintScenario> GenNewTMismatchRejected()
    {
        return from className in Gen.Elements("Factory", "Builder", "Creator")
               select new ConstructorConstraintScenario(
                   $@"class {className}<T> where T : new(int) {{
    public T Create(bool flag) {{
        return new T(flag);
    }}
}}",
                   $"new T(bool) with only new(int) constraint in {className} — mismatch",
                   shouldAccept: false);
    }

    /// <summary>
    /// Generates scenarios where new T() is called but only parameterized constraints exist.
    /// </summary>
    private static Gen<ConstructorConstraintScenario> GenNewTParameterlessRejectedWhenOnlyParameterized()
    {
        return from className in Gen.Elements("Factory", "Builder", "Creator")
               from paramType in Gen.Elements(PrimitiveTypes)
               select new ConstructorConstraintScenario(
                   $@"class {className}<T> where T : new({paramType}) {{
    public T Create() {{
        return new T();
    }}
}}",
                   $"new T() with only new({paramType}) constraint in {className} — mismatch",
                   shouldAccept: false);
    }

    /// <summary>
    /// Generates scenarios with multiple constructor constraints (OR semantics) where
    /// the call matches one of them.
    /// </summary>
    private static Gen<ConstructorConstraintScenario> GenNewTMultipleCtorAccepted()
    {
        return from className in Gen.Elements("Factory", "Builder", "Creator")
               select new ConstructorConstraintScenario(
                   $@"class {className}<T> where T : new(), new(int), new(bool) {{
    public T CreateFromInt(int val) {{
        return new T(val);
    }}
}}",
                   $"new T(int) with new(), new(int), new(bool) constraints in {className} — matches new(int)",
                   shouldAccept: true);
    }

    /// <summary>
    /// Generates scenarios with multiple constructor constraints (OR semantics) where
    /// the call matches none of them.
    /// </summary>
    private static Gen<ConstructorConstraintScenario> GenNewTMultipleCtorRejected()
    {
        return from className in Gen.Elements("Factory", "Builder", "Creator")
               select new ConstructorConstraintScenario(
                   $@"class {className}<T> where T : new(), new(int) {{
    public T CreateFromString(string s) {{
        return new T(s);
    }}
}}",
                   $"new T(string) with new(), new(int) constraints in {className} — no match",
                   shouldAccept: false);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ConstructorConstraintScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNewTParameterlessAccepted(),
            GenNewTSingleParamAccepted(),
            GenNewTMismatchRejected(),
            GenNewTParameterlessRejectedWhenOnlyParameterized(),
            GenNewTMultipleCtorAccepted(),
            GenNewTMultipleCtorRejected());
    }

    public static Arbitrary<ConstructorConstraintScenario> ArbitraryConstructorConstraintScenario() =>
        GenScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 4: Constructor Constraint new T(...) Validation
    ///
    /// For any generic body containing a new T(...) expression where T has one or more
    /// constructor constraints, the type checker SHALL accept the expression if and only
    /// if the argument types match at least one of the declared constructor constraint
    /// signatures.
    ///
    /// **Validates: Requirements 6.5, 6.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConstructorConstraintPropertyTests) })]
    public Property ConstructorConstraint_NewT_AcceptRejectMatchesSignatures(ConstructorConstraintScenario scenario)
    {
        var (_, diagnostics) = CompileSource(scenario.Source);
        var hasMismatch = HasNewTMismatch(diagnostics);

        if (scenario.ShouldAccept)
        {
            return (!hasMismatch).ToProperty()
                .Label($"Expected ACCEPT but got MIST_CONSTRAINT_0016.\nSource:\n{scenario.Source}\nDiagnostics: {string.Join("; ", diagnostics.Where(d => d.Id.Value.StartsWith("MIST_CONSTRAINT")).Select(d => d.Message))}");
        }
        else
        {
            return hasMismatch.ToProperty()
                .Label($"Expected REJECT (MIST_CONSTRAINT_0016) but none emitted.\nSource:\n{scenario.Source}");
        }
    }
}
