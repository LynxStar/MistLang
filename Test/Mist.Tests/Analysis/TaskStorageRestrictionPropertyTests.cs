// Feature: substructural-field-discipline, Task/ValueTask discipline matrix
// **Validates: Requirements 9.7, 9.8**

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
/// Property-based tests verifying that <c>Task&lt;T&gt;</c> (relevant discipline) and
/// <c>ValueTask&lt;T&gt;</c> (linear discipline) are handled by the discipline-compatibility
/// matrix after MIST_TC_0110 removal.
///
/// **Validates: Requirements 9.7, 9.8**
///
/// For any container discipline and any task-like type stored as a field, the DropAnalyzer
/// SHALL emit <c>MIST_DROP_FIELD_NOT_OWNED</c> if and only if the matrix rejects the
/// combination. The obsolete <c>MIST_TC_0110</c> diagnostic SHALL never appear.
///
/// All work runs in-process using CompilerDriver — no external process is spawned.
/// </summary>
public class TaskStorageRestrictionPropertyTests
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
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_task_prop_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>The task-like type to store as a field.</summary>
    public enum TaskLikeType
    {
        /// <summary>Task&lt;T&gt; — relevant discipline</summary>
        Task,

        /// <summary>ValueTask&lt;T&gt; — linear discipline</summary>
        ValueTask,
    }

    /// <summary>
    /// A scenario describing a container holding a task-like field.
    /// </summary>
    public record TaskContainerScenario(
        ContainerDiscipline Container,
        TaskLikeType TaskType,
        string ClassName,
        string TypeArgument)
    {
        /// <summary>
        /// Whether the discipline-compatibility matrix permits this combination.
        /// Task (relevant): permitted in linear, relevant containers.
        /// ValueTask (linear): permitted only in linear containers.
        /// </summary>
        public bool IsPermitted => TaskType switch
        {
            TaskLikeType.Task => Container is ContainerDiscipline.Linear or ContainerDiscipline.Relevant,
            TaskLikeType.ValueTask => Container is ContainerDiscipline.Linear,
            _ => false,
        };

        public override string ToString()
        {
            return $"{Container} class {ClassName} {{ {TaskType}<{TypeArgument}> field }} → {(IsPermitted ? "OK" : "ERROR")}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Foo", "Bar", "Service", "Handler", "Cache", "Store"];

    private static readonly string[] TypeArguments =
        ["int", "string", "bool", "void"];

    public static Gen<TaskContainerScenario> GenScenario()
    {
        return from container in Gen.Elements(
                   ContainerDiscipline.Linear,
                   ContainerDiscipline.Affine,
                   ContainerDiscipline.Relevant,
                   ContainerDiscipline.Unrestricted)
               from taskType in Gen.Elements(TaskLikeType.Task, TaskLikeType.ValueTask)
               from className in Gen.Elements(ClassNames)
               from typeArg in Gen.Elements(TypeArguments)
               select new TaskContainerScenario(container, taskType, className, typeArg);
    }

    public static Arbitrary<TaskContainerScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildSource(TaskContainerScenario scenario)
    {
        var modifier = scenario.Container switch
        {
            ContainerDiscipline.Linear => "linear ",
            ContainerDiscipline.Affine => "affine ",
            ContainerDiscipline.Relevant => "relevant ",
            ContainerDiscipline.Unrestricted => "",
            _ => "",
        };

        var fieldTypeName = scenario.TaskType switch
        {
            TaskLikeType.Task => $"Task<{scenario.TypeArgument}>",
            TaskLikeType.ValueTask => $"ValueTask<{scenario.TypeArgument}>",
            _ => throw new ArgumentOutOfRangeException(),
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
            ? "this.myTask = t; this.dropped = false;"
            : "this.myTask = t;";

        return $$"""
            using Standard;
            {{modifier}}class {{scenario.ClassName}} {
                {{fieldTypeName}} myTask;
            {{dropMethod}}
                public {{scenario.ClassName}}({{fieldTypeName}} t) { {{ctorBody}} }
            }
            class Program { public static void Main() {} }
            """;
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// For any container discipline and task-like type, the DropAnalyzer emits
    /// MIST_DROP_FIELD_NOT_OWNED iff the discipline-compatibility matrix rejects
    /// the combination. MIST_TC_0110 never appears.
    ///
    /// **Validates: Requirements 9.7, 9.8**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TaskStorageRestrictionPropertyTests) }, MaxTest = 100)]
    public Property TaskLikeFieldDiagnostic_FollowsDisciplineMatrix(TaskContainerScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = Compile(source);

        // MIST_TC_0110 must NEVER appear (Requirement 9.8)
        var hasTc0110 = diagnostics.Any(d => d.Id.Value == "MIST_TC_0110");
        if(hasTc0110)
        {
            return false.ToProperty()
                .Label($"MIST_TC_0110 should never appear but was emitted.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        var fieldNotOwnedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED")
            .ToList();

        if(scenario.IsPermitted)
        {
            // Matrix permits → zero MIST_DROP_FIELD_NOT_OWNED diagnostics
            var hasNone = fieldNotOwnedDiags.Count == 0;
            return hasNone.ToProperty()
                .Label($"Expected zero MIST_DROP_FIELD_NOT_OWNED but got {fieldNotOwnedDiags.Count}.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"Diagnostics: [{string.Join(", ", fieldNotOwnedDiags.Select(d => d.Message))}]");
        }
        else
        {
            // Matrix rejects → at least one MIST_DROP_FIELD_NOT_OWNED diagnostic
            var hasFieldDiag = fieldNotOwnedDiags.Count > 0;
            return hasFieldDiag.ToProperty()
                .Label($"Expected MIST_DROP_FIELD_NOT_OWNED but got none.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
    }
}