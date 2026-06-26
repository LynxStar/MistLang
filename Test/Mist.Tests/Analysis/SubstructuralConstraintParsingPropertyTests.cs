// Feature: substructural-field-discipline, Property 2: Substructural Constraint Parsing
// **Validates: Requirements 3.1, 3.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that substructural constraint keywords
/// (<c>linear</c>, <c>affine</c>, <c>relevant</c>) in <c>where</c> clauses
/// are correctly parsed and stored on <see cref="TypeParameterConstraints"/>.
///
/// **Property 2: Substructural Constraint Parsing**
///
/// For any valid type declaration with a <c>where T : {discipline}</c> clause
/// (where discipline ∈ {linear, affine, relevant}), parsing the source and
/// building the semantic model SHALL produce a <see cref="TypeParameterType"/>
/// whose Constraints has the corresponding <c>Has{Discipline}Constraint</c>
/// boolean set to <c>true</c> and the other two set to <c>false</c>.
///
/// **Validates: Requirements 3.1, 3.2**
///
/// All work runs in-process using CompilerDriver — no external process is spawned.
/// </summary>
public class SubstructuralConstraintParsingPropertyTests
{
    // ─── Capturing semantic model builder ────────────────────────────────

    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();

        public ISemanticModel? CapturedModel { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            CapturedModel = model;
            return model;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static (ImmutableArray<Diagnostic> Diagnostics, ISemanticModel? Model) CompileAndCapture(string source)
    {
        var capture = new CapturingSemanticModelBuilder();
        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(
                new TypeCheckerAnalyzer(),
                new ReadonlyAnalyzer(),
                new OwnershipAnalyzer(),
                new DropAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var tempDir = Path.Combine(Path.GetTempPath(), "mist_constraint_parse_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);
            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);
            var result = driver.Compile(options);
            return (result.Diagnostics, capture.CapturedModel);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>The substructural discipline to use in the where clause.</summary>
    public enum SubstructuralDiscipline
    {
        Linear,
        Affine,
        Relevant,
    }

    /// <summary>
    /// A scenario describing a generic type declaration with a substructural constraint.
    /// </summary>
    public record ConstraintParsingScenario(
        SubstructuralDiscipline Discipline,
        string ClassName,
        string TypeParamName)
    {
        public string DisciplineKeyword => Discipline switch
        {
            SubstructuralDiscipline.Linear => "linear",
            SubstructuralDiscipline.Affine => "affine",
            SubstructuralDiscipline.Relevant => "relevant",
            _ => "linear",
        };

        public override string ToString()
        {
            return $"class {ClassName}<{TypeParamName}> where {TypeParamName} : {DisciplineKeyword}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Container", "Holder", "Wrapper", "Box", "Store", "Cell"];

    private static readonly string[] TypeParamNames =
        ["T", "U", "V", "TItem", "TValue", "TElement"];

    public static Gen<ConstraintParsingScenario> GenScenario()
    {
        return from discipline in Gen.Elements(
                   SubstructuralDiscipline.Linear,
                   SubstructuralDiscipline.Affine,
                   SubstructuralDiscipline.Relevant)
               from className in Gen.Elements(ClassNames)
               from typeParamName in Gen.Elements(TypeParamNames)
               select new ConstraintParsingScenario(discipline, className, typeParamName);
    }

    public static Arbitrary<ConstraintParsingScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Source builder ──────────────────────────────────────────────────

    private static string BuildSource(ConstraintParsingScenario scenario)
    {
        // The container discipline matches the constraint so the DropAnalyzer
        // doesn't reject the field (linear holds anything, affine holds affine,
        // relevant holds relevant). This lets us parse without unrelated errors.
        var containerModifier = scenario.Discipline switch
        {
            SubstructuralDiscipline.Linear => "linear ",
            SubstructuralDiscipline.Affine => "affine ",
            _ => "relevant ",
        };

        // Build a Drop() appropriate for the container discipline.
        var dropMethod = scenario.Discipline switch
        {
            SubstructuralDiscipline.Linear =>
                "    public void Drop() { }",
            SubstructuralDiscipline.Affine =>
                "    public void Drop() { }",
            _ =>
                """
                    bool dropped;
                    public void Drop() { if (dropped) { return; } this.dropped = true; }
                """,
        };

        var ctorBody = scenario.Discipline == SubstructuralDiscipline.Relevant
            ? $"this.value = val; this.dropped = false;"
            : $"this.value = val;";

        return $$"""
            using Standard;
            {{containerModifier}}class {{scenario.ClassName}}<{{scenario.TypeParamName}}> where {{scenario.TypeParamName}} : {{scenario.DisciplineKeyword}} {
                {{scenario.TypeParamName}} value;
            {{dropMethod}}
                public {{scenario.ClassName}}({{scenario.TypeParamName}} val) { {{ctorBody}} }
            }
            class Program { public static void Main() {} }
            """;
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// For any valid type declaration with a <c>where T : {discipline}</c> clause,
    /// the semantic model's <see cref="TypeParameterType.Constraints"/> SHALL have
    /// the corresponding <c>Has{Discipline}Constraint</c> boolean set to <c>true</c>
    /// and the other two set to <c>false</c>.
    ///
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(SubstructuralConstraintParsingPropertyTests) }, MaxTest = 100)]
    public Property SubstructuralConstraint_ParsedCorrectly(ConstraintParsingScenario scenario)
    {
        var source = BuildSource(scenario);
        var (diagnostics, model) = CompileAndCapture(source);

        // The source should compile without errors (we build compatible combinations).
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if(errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Expected zero errors but got {errors.Count}.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"Errors: [{string.Join(", ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        if(model is null)
        {
            return false.ToProperty()
                .Label($"Semantic model was not captured.\nScenario: {scenario}");
        }

        // Find the type parameter from the class type in the semantic model.
        var classSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == scenario.ClassName);

        if(classSymbol is null)
        {
            return false.ToProperty()
                .Label($"Could not find class symbol '{scenario.ClassName}' in the semantic model.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Available symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Kind}:{s.Name}"))}]");
        }

        // Get the TypeParameterType from the class's TypeParameters.
        var classType = classSymbol.DeclaredType as ClassType;
        if(classType is null)
        {
            return false.ToProperty()
                .Label($"Class symbol '{scenario.ClassName}' does not have a ClassType DeclaredType.\n" +
                       $"Scenario: {scenario}");
        }

        var typeParam = classType.TypeParameters
            .OfType<TypeParameterType>()
            .FirstOrDefault(tp => tp.Name == scenario.TypeParamName);

        if(typeParam is null)
        {
            return false.ToProperty()
                .Label($"Could not find type parameter '{scenario.TypeParamName}' on class '{scenario.ClassName}'.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Available type params: [{string.Join(", ", classType.TypeParameters.Select(t => t.DisplayName))}]");
        }

        var constraints = typeParam.Constraints;
        if(constraints is null)
        {
            return false.ToProperty()
                .Label($"Type parameter '{scenario.TypeParamName}' has null Constraints.\n" +
                       $"Scenario: {scenario}");
        }

        // Check that the correct constraint is set and the others are not.
        var (expectedLinear, expectedAffine, expectedRelevant) = scenario.Discipline switch
        {
            SubstructuralDiscipline.Linear => (true, false, false),
            SubstructuralDiscipline.Affine => (false, true, false),
            _ => (false, false, true),
        };

        var linearOk = constraints.HasLinearConstraint == expectedLinear;
        var affineOk = constraints.HasAffineConstraint == expectedAffine;
        var relevantOk = constraints.HasRelevantConstraint == expectedRelevant;

        if(!linearOk || !affineOk || !relevantOk)
        {
            return false.ToProperty()
                .Label($"Constraint booleans mismatch for '{scenario.DisciplineKeyword}' constraint.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Expected: HasLinear={expectedLinear}, HasAffine={expectedAffine}, HasRelevant={expectedRelevant}\n" +
                       $"Actual:   HasLinear={constraints.HasLinearConstraint}, HasAffine={constraints.HasAffineConstraint}, HasRelevant={constraints.HasRelevantConstraint}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario}");
    }
}