// Feature: substructural-field-discipline, Property 1: Discipline-Compatibility Matrix
//
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.7, 2.1, 2.2, 2.3, 2.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for the discipline-compatibility matrix (Property 1).
///
/// Feature: substructural-field-discipline, Property 1: Discipline-Compatibility Matrix
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.7, 2.1, 2.2, 2.3, 2.4**
///
/// For any container discipline C ∈ {linear, affine, relevant, unrestricted, owned} and any
/// field discipline F ∈ {linear, affine, relevant, unrestricted}, compiling a container of
/// discipline C with a field of discipline F SHALL produce zero MIST_DROP_FIELD_NOT_OWNED
/// diagnostics iff the compatibility matrix permits the (C, F) pair. When the matrix rejects
/// the pair, exactly one diagnostic SHALL be produced with a message that names the container,
/// the field, the field's discipline keyword, and the specific invariant violation.
///
/// All compilation runs in-process via CompilerDriver — zero external processes spawned.
/// </summary>
public sealed class DisciplineCompatibilityMatrixPropertyTests
{
    private const string FieldNotOwnedId = "MIST_DROP_FIELD_NOT_OWNED";

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

    // ─── Compile helper (in-process, zero external processes) ─────────────

    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_matrix_prop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
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

    /// <summary>Container discipline kinds.</summary>
    public enum ContainerDisciplineKind
    {
        Unrestricted,
        Owned,
        Affine,
        Linear,
        Relevant,
    }

    /// <summary>Field discipline kinds.</summary>
    public enum FieldDisciplineKind
    {
        Unrestricted,
        Affine,
        Linear,
        Relevant,
    }

    /// <summary>
    /// A generated container/field discipline combination together with whether the
    /// field-containment rule requires a MIST_DROP_FIELD_NOT_OWNED diagnostic.
    /// </summary>
    public sealed record MatrixScenario(
        ContainerDisciplineKind Container,
        FieldDisciplineKind Field,
        string ContainerClassName,
        string FieldTypeName,
        bool ExpectError)
    {
        public override string ToString() =>
            $"container={Container}, field={Field} → expectError={ExpectError}";
    }

#pragma warning restore CA1034

    // ─── Matrix logic ────────────────────────────────────────────────────

    /// <summary>
    /// Implements the discipline-compatibility matrix to determine expected error state.
    /// - Linear container → holds anything (linear, affine, relevant, unrestricted).
    /// - Affine container → holds only affine + unrestricted.
    /// - Relevant container → holds only relevant + unrestricted.
    /// - Unrestricted/Owned container → holds only unrestricted.
    /// </summary>
    private static bool MatrixExpectsError(ContainerDisciplineKind container, FieldDisciplineKind field)
    {
        if(field == FieldDisciplineKind.Unrestricted)
            return false;

        return container switch
        {
            ContainerDisciplineKind.Linear => false,
            ContainerDisciplineKind.Affine => field != FieldDisciplineKind.Affine,
            ContainerDisciplineKind.Relevant => field != FieldDisciplineKind.Relevant,
            _ => true, // Unrestricted/Owned holds only unrestricted.
        };
    }

    /// <summary>
    /// Returns the expected diagnostic message for a rejected (container, field) pair.
    /// Must match the DropAnalyzer's actual message templates exactly (Req 2.1–2.4).
    /// </summary>
    private static string ExpectedMessage(
        ContainerDisciplineKind container,
        string containerName,
        FieldDisciplineKind field,
        string fieldName)
    {
        var modifier = field switch
        {
            FieldDisciplineKind.Linear => "linear",
            FieldDisciplineKind.Affine => "affine",
            FieldDisciplineKind.Relevant => "relevant",
            _ => "unrestricted",
        };

        return container switch
        {
            ContainerDisciplineKind.Affine =>
                $"Affine type '{containerName}' cannot contain {modifier} field '{fieldName}'. " +
                "The container may never be dropped, violating the field's requirement of at least one drop.",
            ContainerDisciplineKind.Relevant =>
                $"Relevant type '{containerName}' cannot contain {modifier} field '{fieldName}'. " +
                "The container may be dropped multiple times, violating the field's at-most-once drop requirement.",
            _ =>
                $"Type '{containerName}' cannot contain {modifier} field '{fieldName}'. " +
                "The container has no substructural discipline and cannot guarantee the field's drop requirements.",
        };
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<MatrixScenario> GenScenario()
    {
        return from container in Gen.Elements(
                   ContainerDisciplineKind.Unrestricted,
                   ContainerDisciplineKind.Owned,
                   ContainerDisciplineKind.Affine,
                   ContainerDisciplineKind.Linear,
                   ContainerDisciplineKind.Relevant)
               from field in Gen.Elements(
                   FieldDisciplineKind.Unrestricted,
                   FieldDisciplineKind.Affine,
                   FieldDisciplineKind.Linear,
                   FieldDisciplineKind.Relevant)
               let expectError = MatrixExpectsError(container, field)
               select new MatrixScenario(container, field, "Container", FieldTypeNameFor(field), expectError);
    }

    private static string FieldTypeNameFor(FieldDisciplineKind field) => field switch
    {
        FieldDisciplineKind.Linear => "LinearField",
        FieldDisciplineKind.Affine => "AffineField",
        FieldDisciplineKind.Relevant => "RelevantField",
        _ => "PlainField",
    };

    public static Arbitrary<MatrixScenario> ArbitraryMatrixScenario() =>
        GenScenario().ToArbitrary();

    // ─── Source builder ──────────────────────────────────────────────────

    private static string BuildSource(MatrixScenario scenario)
    {
        var containerKeyword = scenario.Container switch
        {
            ContainerDisciplineKind.Linear => "linear class",
            ContainerDisciplineKind.Affine => "affine class",
            ContainerDisciplineKind.Relevant => "relevant class",
            ContainerDisciplineKind.Owned => "owned class",
            _ => "class",
        };

        // Build a Drop() method appropriate for the container discipline.
        var dropMethod = scenario.Container switch
        {
            ContainerDisciplineKind.Linear =>
                "    public void Drop() { }",
            ContainerDisciplineKind.Affine =>
                "    public void Drop() { }",
            ContainerDisciplineKind.Relevant =>
                """
                    bool dropped;
                    public void Drop() { if (dropped) { return; } this.dropped = true; }
                """,
            _ => "",
        };

        return $$"""
            class PlainField
            {
            }
            affine class AffineField
            {
                public void Drop() { }
            }
            linear class LinearField
            {
                public void Drop() { }
            }
            relevant class RelevantField
            {
                bool dropped;
                public void Drop() { if (dropped) { return; } this.dropped = true; }
            }
            {{containerKeyword}} {{scenario.ContainerClassName}}
            {
                {{scenario.FieldTypeName}} member;
            {{dropMethod}}
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Discipline-Compatibility Matrix
    //
    // A MIST_DROP_FIELD_NOT_OWNED diagnostic is present IFF the discipline-
    // compatibility matrix rejects the (container, field) pair; otherwise absent.
    // When present, the message matches the expected template from Req 2.1–2.4.
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.7, 2.1, 2.2, 2.3, 2.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DisciplineCompatibilityMatrixPropertyTests) })]
    public Property DisciplineMatrix_ErrorIffMatrixRejects_WithCorrectMessage(
        MatrixScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = Compile(source);

        var fieldNotOwnedDiags = diagnostics
            .Where(d => d.Id.Value == FieldNotOwnedId)
            .ToList();

        if(scenario.ExpectError)
        {
            // Matrix rejects → exactly one MIST_DROP_FIELD_NOT_OWNED diagnostic.
            if(fieldNotOwnedDiags.Count != 1)
            {
                return false.ToProperty()
                    .Label($"Expected exactly 1 {FieldNotOwnedId} for {scenario}, got {fieldNotOwnedDiags.Count}.\n"
                        + $"Source:\n{source}\n"
                        + $"All diags: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
            }

            // Validate diagnostic message content matches Requirement 2 templates.
            var actualMessage = fieldNotOwnedDiags[0].Message;
            var expected = ExpectedMessage(
                scenario.Container, scenario.ContainerClassName, scenario.Field, "member");

            return (actualMessage == expected).ToProperty()
                .Label($"Message mismatch for {scenario}.\n"
                    + $"Expected: {expected}\n"
                    + $"Actual:   {actualMessage}");
        }

        // Matrix permits → zero MIST_DROP_FIELD_NOT_OWNED diagnostics.
        return (fieldNotOwnedDiags.Count == 0).ToProperty()
            .Label($"Expected zero {FieldNotOwnedId} for {scenario}, got {fieldNotOwnedDiags.Count}.\n"
                + $"Source:\n{source}\n"
                + $"Diags: [{string.Join(", ", fieldNotOwnedDiags.Select(d => d.Message))}]");
    }
}