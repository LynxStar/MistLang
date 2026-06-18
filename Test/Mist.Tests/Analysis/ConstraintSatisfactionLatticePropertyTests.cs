// Feature: substructural-field-discipline, Property 5: Constraint Satisfaction Lattice
// **Validates: Requirements 5.1, 5.2, 5.3, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8**

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
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying the constraint satisfaction lattice at instantiation sites.
///
/// Property 5: For any instantiation site where a type argument with discipline D is provided
/// for a type parameter with substructural constraint R, the compiler SHALL accept the type
/// argument if and only if the satisfaction lattice permits (D, R):
///   - linear satisfies {linear, affine, relevant}
///   - affine satisfies only {affine}
///   - relevant satisfies only {relevant}
///   - unrestricted satisfies nothing
///
/// When the lattice rejects the pair, the compiler SHALL emit a MIST_CONSTRAINT_0018 diagnostic.
///
/// All work runs in-process using CompilerDriver — no external process is spawned.
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8**
/// </summary>
public class ConstraintSatisfactionLatticePropertyTests
{
    private static readonly Mist.Lexing.Contracts.ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly Mist.Parsing.Contracts.IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if (parseResult.Root is null)
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());

        var compilation = new Mist.Driver.Contracts.MistCompilation(
            ImmutableArray.Create(new Mist.Driver.Contracts.CompilationUnit(sourceText)));
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

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034 // Nested types should not be visible

    /// <summary>The discipline of the type argument being passed at the instantiation site.</summary>
    public enum ArgumentDiscipline
    {
        Linear,
        Affine,
        Relevant,
        Unrestricted,
    }

    /// <summary>The substructural constraint declared on the type parameter.</summary>
    public enum ConstraintDiscipline
    {
        Linear,
        Affine,
        Relevant,
    }

    /// <summary>
    /// A scenario representing (argument discipline, constraint discipline) at an instantiation site.
    /// </summary>
    public record ConstraintLatticeScenario(
        ArgumentDiscipline Argument,
        ConstraintDiscipline Constraint,
        string ArgClassName,
        string ContainerClassName)
    {
        /// <summary>
        /// Whether the constraint satisfaction lattice permits this combination.
        /// Linear satisfies {linear, affine, relevant}.
        /// Affine satisfies only {affine}.
        /// Relevant satisfies only {relevant}.
        /// Unrestricted satisfies nothing.
        /// </summary>
        public bool IsPermitted => Argument switch
        {
            ArgumentDiscipline.Linear => true, // linear satisfies all constraints
            ArgumentDiscipline.Affine => Constraint == ConstraintDiscipline.Affine,
            ArgumentDiscipline.Relevant => Constraint == ConstraintDiscipline.Relevant,
            ArgumentDiscipline.Unrestricted => false, // unrestricted satisfies nothing
            _ => false,
        };

        public override string ToString() =>
            $"{Argument} arg for {Constraint} constraint ({ArgClassName} → {ContainerClassName}<T>) → " +
            $"{(IsPermitted ? "OK" : "ERROR")}";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ArgClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] ContainerClassNames =
        ["Box", "Holder", "Wrapper", "Cell", "Slot", "Container"];

    public static Gen<ConstraintLatticeScenario> GenScenario()
    {
        return from argument in Gen.Elements(
                   ArgumentDiscipline.Linear,
                   ArgumentDiscipline.Affine,
                   ArgumentDiscipline.Relevant,
                   ArgumentDiscipline.Unrestricted)
               from constraint in Gen.Elements(
                   ConstraintDiscipline.Linear,
                   ConstraintDiscipline.Affine,
                   ConstraintDiscipline.Relevant)
               from argClass in Gen.Elements(ArgClassNames)
               from containerClass in Gen.Elements(ContainerClassNames)
               select new ConstraintLatticeScenario(argument, constraint, argClass, containerClass);
    }

    public static Arbitrary<ConstraintLatticeScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildSource(ConstraintLatticeScenario scenario)
    {
        // Build the argument type with the appropriate discipline modifier.
        var argModifier = scenario.Argument switch
        {
            ArgumentDiscipline.Linear => "linear ",
            ArgumentDiscipline.Affine => "affine ",
            ArgumentDiscipline.Relevant => "relevant ",
            ArgumentDiscipline.Unrestricted => "",
            _ => "",
        };

        // Build the Drop() method and related members for the argument type.
        var argMembers = scenario.Argument switch
        {
            ArgumentDiscipline.Linear =>
                "\n    public void Drop() { }",
            ArgumentDiscipline.Affine =>
                "\n    public void Drop() { }",
            ArgumentDiscipline.Relevant =>
                "\n    bool dropped;\n    public void Drop() { if (this.dropped) { return; } this.dropped = true; }",
            _ => "",
        };

        // Build the constraint keyword for the container's type parameter.
        var constraintKeyword = scenario.Constraint switch
        {
            ConstraintDiscipline.Linear => "linear",
            ConstraintDiscipline.Affine => "affine",
            ConstraintDiscipline.Relevant => "relevant",
            _ => throw new ArgumentOutOfRangeException(),
        };

        // Use a new-expression inside Main to trigger the instantiation-site constraint
        // satisfaction check. This follows the pattern from the existing
        // ConstraintSatisfactionPropertyTests which validates the check fires at `new`
        // expression sites.
        return $$"""
            {{argModifier}}class {{scenario.ArgClassName}} {{{argMembers}}
            }
            class {{scenario.ContainerClassName}}<T> where T : {{constraintKeyword}} {
            }
            class Program {
                public static void Main() {
                    {{scenario.ContainerClassName}}<{{scenario.ArgClassName}}> x = new {{scenario.ContainerClassName}}<{{scenario.ArgClassName}}>();
                }
            }
            """;
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 5: Constraint Satisfaction Lattice
    ///
    /// For any (argument discipline, constraint discipline) pair at an instantiation site,
    /// the compiler SHALL accept the type argument iff the satisfaction lattice permits:
    ///   - linear satisfies {linear, affine, relevant}
    ///   - affine satisfies only {affine}
    ///   - relevant satisfies only {relevant}
    ///   - unrestricted satisfies nothing
    ///
    /// When the lattice rejects the pair, the compiler SHALL emit MIST_CONSTRAINT_0018.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConstraintSatisfactionLatticePropertyTests) }, MaxTest = 100)]
    public Property ConstraintSatisfactionLattice_AcceptRejectMatchesLatticeRules(
        ConstraintLatticeScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = Compile(source);

        // Filter out MIST_TC_0017 (entry-point detection) which can fire spuriously.
        var constraintViolations = diagnostics
            .Where(d => d.Id.Value == "MIST_CONSTRAINT_0018")
            .ToList();

        if (scenario.IsPermitted)
        {
            // Lattice permits → zero MIST_CONSTRAINT_0018 diagnostics
            var hasNone = constraintViolations.Count == 0;
            return hasNone.ToProperty()
                .Label($"Expected zero MIST_CONSTRAINT_0018 but got {constraintViolations.Count}.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"Diagnostics: [{string.Join(", ", constraintViolations.Select(d => d.Message))}]");
        }
        else
        {
            // Lattice rejects → at least one MIST_CONSTRAINT_0018 diagnostic
            var hasViolation = constraintViolations.Count > 0;
            return hasViolation.ToProperty()
                .Label($"Expected MIST_CONSTRAINT_0018 but got none.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
    }
}
