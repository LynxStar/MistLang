// Feature: substructural-field-discipline, Property 4: Generic Field Containment
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7**

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
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying generic field containment rules (Property 4).
///
/// <para>
/// <b>Property 4: Generic Field Containment</b><br/>
/// For any substructural container (linear, affine, or relevant) that declares a field whose type
/// is a type parameter <c>T</c>: if <c>T</c> has no substructural constraint, the DropAnalyzer
/// SHALL emit a diagnostic indicating the parameter needs a constraint; if <c>T</c> has a
/// substructural constraint, the DropAnalyzer SHALL apply the discipline-compatibility matrix
/// using the constraint's discipline as the field's effective discipline.
/// </para>
///
/// <para>
/// Additionally, unrestricted containers with unconstrained type parameter fields do NOT produce
/// the "unconstrained type parameter" diagnostic (since unrestricted containers don't enforce
/// substructural constraints).
/// </para>
///
/// <b>Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7</b>
///
/// All work runs in-process using CompilerDriver — no external process is spawned.
/// </summary>
public class GenericFieldContainmentPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_gfc_prop_" + Guid.NewGuid().ToString("N"));
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
            return SharedDriver.Compile(options).Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>The container discipline.</summary>
    public enum ContainerDiscipline
    {
        Linear,
        Affine,
        Relevant,
        Unrestricted,
    }

    /// <summary>The substructural constraint on the type parameter (or none).</summary>
    public enum TypeParamConstraint
    {
        /// <summary>No substructural constraint on T.</summary>
        None,

        /// <summary><c>where T : linear</c></summary>
        Linear,

        /// <summary><c>where T : affine</c></summary>
        Affine,

        /// <summary><c>where T : relevant</c></summary>
        Relevant,
    }

    /// <summary>
    /// A scenario describing a generic container with a type parameter field.
    /// </summary>
    public record GenericFieldScenario(
        ContainerDiscipline Container,
        TypeParamConstraint Constraint,
        string ClassName,
        string TypeParamName)
    {
        /// <summary>
        /// Whether the scenario expects the "unconstrained type parameter" diagnostic.
        /// Only substructural containers with unconstrained type param fields produce this.
        /// </summary>
        public bool ExpectsUnconstrainedDiagnostic =>
            Container != ContainerDiscipline.Unrestricted && Constraint == TypeParamConstraint.None;

        /// <summary>
        /// When the type parameter IS constrained, whether the matrix permits the combination.
        /// Uses the constraint's discipline as the field's effective discipline.
        /// </summary>
        public bool IsMatrixPermitted
        {
            get
            {
                if(Constraint == TypeParamConstraint.None)
                {
                    return false; // Not applicable — unconstrained path takes over.
                }

                var fieldDiscipline = Constraint switch
                {
                    TypeParamConstraint.Linear => SubstructuralDiscipline.Linear,
                    TypeParamConstraint.Affine => SubstructuralDiscipline.Affine,
                    TypeParamConstraint.Relevant => SubstructuralDiscipline.Relevant,
                    _ => SubstructuralDiscipline.None,
                };

                return Container switch
                {
                    ContainerDiscipline.Linear => true, // Linear holds anything.
                    ContainerDiscipline.Affine => fieldDiscipline == SubstructuralDiscipline.Affine,
                    ContainerDiscipline.Relevant => fieldDiscipline == SubstructuralDiscipline.Relevant,
                    ContainerDiscipline.Unrestricted => fieldDiscipline == SubstructuralDiscipline.None,
                    _ => false,
                };
            }
        }

        /// <summary>
        /// Whether we expect zero MIST_DROP_FIELD_NOT_OWNED diagnostics for this scenario.
        /// </summary>
        public bool ExpectsZeroDiagnostics
        {
            get
            {
                // Unrestricted container with unconstrained T → no unconstrained-TP diagnostic.
                if(Container == ContainerDiscipline.Unrestricted && Constraint == TypeParamConstraint.None)
                {
                    return true;
                }

                // Substructural container with unconstrained T → diagnostic.
                if(ExpectsUnconstrainedDiagnostic)
                {
                    return false;
                }

                // Constrained T → matrix decides.
                return IsMatrixPermitted;
            }
        }

        public override string ToString()
        {
            return $"{Container} class {ClassName}<{TypeParamName}>" +
            (Constraint != TypeParamConstraint.None ? $" where {TypeParamName} : {Constraint.ToString().ToLowerInvariant()}" : "") +
            $" {{ {TypeParamName} field; }}" +
            $" → {(ExpectsZeroDiagnostics ? "OK" : "DIAGNOSTIC")}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Container", "Box", "Holder", "Wrapper", "Store", "Cell"];

    private static readonly string[] TypeParamNames =
        ["T", "TVal", "TItem", "TData"];

    public static Gen<GenericFieldScenario> GenScenario()
    {
        return from container in Gen.Elements(
                   ContainerDiscipline.Linear,
                   ContainerDiscipline.Affine,
                   ContainerDiscipline.Relevant,
                   ContainerDiscipline.Unrestricted)
               from constraint in Gen.Elements(
                   TypeParamConstraint.None,
                   TypeParamConstraint.Linear,
                   TypeParamConstraint.Affine,
                   TypeParamConstraint.Relevant)
               from className in Gen.Elements(ClassNames)
               from tpName in Gen.Elements(TypeParamNames)
               select new GenericFieldScenario(container, constraint, className, tpName);
    }

    public static Arbitrary<GenericFieldScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildSource(GenericFieldScenario scenario)
    {
        var modifier = scenario.Container switch
        {
            ContainerDiscipline.Linear => "linear ",
            ContainerDiscipline.Affine => "affine ",
            ContainerDiscipline.Relevant => "relevant ",
            ContainerDiscipline.Unrestricted => "",
            _ => "",
        };

        var constraintClause = scenario.Constraint switch
        {
            TypeParamConstraint.Linear => $" where {scenario.TypeParamName} : linear",
            TypeParamConstraint.Affine => $" where {scenario.TypeParamName} : affine",
            TypeParamConstraint.Relevant => $" where {scenario.TypeParamName} : relevant",
            TypeParamConstraint.None => "",
            _ => "",
        };

        // Build a Drop() method appropriate for the discipline.
        var dropMethod = scenario.Container switch
        {
            ContainerDiscipline.Linear =>
                "    public void Drop() { }",
            ContainerDiscipline.Affine =>
                "    public void Drop() { }",
            ContainerDiscipline.Relevant =>
                """
                    bool dropped;
                    public void Drop() { if (dropped) { return; } this.dropped = true; }
                """,
            _ => "", // Unrestricted types don't need Drop()
        };

        var ctorBody = scenario.Container == ContainerDiscipline.Relevant
            ? $"this.value = v; this.dropped = false;"
            : $"this.value = v;";

        return $$"""
            using Standard;
            {{modifier}}class {{scenario.ClassName}}<{{scenario.TypeParamName}}>{{constraintClause}} {
                {{scenario.TypeParamName}} value;
            {{dropMethod}}
                public {{scenario.ClassName}}({{scenario.TypeParamName}} v) { {{ctorBody}} }
            }
            class Program { public static void Main() {} }
            """;
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// For any substructural container with a type parameter field:
    /// - Unconstrained T → MIST_DROP_FIELD_NOT_OWNED diagnostic (Requirements 4.3, 4.4, 4.5, 4.6).
    /// - Constrained T → apply discipline-compatibility matrix (Requirements 4.1, 4.2, 4.7).
    /// - Unrestricted container with unconstrained T → no unconstrained-TP diagnostic.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(GenericFieldContainmentPropertyTests) }, MaxTest = 100)]
    public Property GenericFieldContainment_AppliesMatrixOrRejectsUnconstrained(GenericFieldScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = Compile(source);

        var fieldNotOwnedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED")
            .ToList();

        if(scenario.ExpectsZeroDiagnostics)
        {
            // Expect no MIST_DROP_FIELD_NOT_OWNED diagnostic.
            var hasNone = fieldNotOwnedDiags.Count == 0;
            return hasNone.ToProperty()
                .Label($"Expected zero MIST_DROP_FIELD_NOT_OWNED but got {fieldNotOwnedDiags.Count}.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"Diagnostics: [{string.Join(", ", fieldNotOwnedDiags.Select(d => d.Message))}]");
        }
        else if(scenario.ExpectsUnconstrainedDiagnostic)
        {
            // Expect the "unconstrained type parameter" diagnostic.
            var hasUnconstrainedDiag = fieldNotOwnedDiags.Count > 0 &&
                fieldNotOwnedDiags.Any(d => d.Message.Contains("unconstrained type parameter"));
            return hasUnconstrainedDiag.ToProperty()
                .Label($"Expected 'unconstrained type parameter' diagnostic but didn't find it.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
        else
        {
            // Constrained T with incompatible discipline → matrix rejection diagnostic.
            var hasFieldDiag = fieldNotOwnedDiags.Count > 0 &&
                fieldNotOwnedDiags.Any(d => !d.Message.Contains("unconstrained type parameter"));
            return hasFieldDiag.ToProperty()
                .Label($"Expected discipline-compatibility matrix rejection but didn't find it.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
    }
}