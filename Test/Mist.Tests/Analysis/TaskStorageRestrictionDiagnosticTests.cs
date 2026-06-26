// **Validates: Requirements 9.7, 9.8**

using System.Collections.Immutable;
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
/// Unit tests verifying that <c>Task&lt;T&gt;</c> and <c>ValueTask&lt;T&gt;</c>
/// are handled by the discipline-compatibility matrix after removal of MIST_TC_0110.
///
/// Task&lt;T&gt; has relevant discipline; ValueTask&lt;T&gt; has linear discipline.
/// The matrix determines which containers may hold them:
///   - Task in relevant container → ✓ (zero diagnostics)
///   - Task in linear container → ✓ (zero diagnostics)
///   - Task in affine container → ✗ (MIST_DROP_FIELD_NOT_OWNED)
///   - Task in unrestricted container → ✗ (MIST_DROP_FIELD_NOT_OWNED)
///   - ValueTask in linear container → ✓ (zero diagnostics)
///   - ValueTask in affine container → ✗ (MIST_DROP_FIELD_NOT_OWNED)
///   - ValueTask in relevant container → ✗ (MIST_DROP_FIELD_NOT_OWNED)
///   - ValueTask in unrestricted container → ✗ (MIST_DROP_FIELD_NOT_OWNED)
///
/// **Validates: Requirements 9.7, 9.8**
/// </summary>
public class TaskStorageRestrictionDiagnosticTests
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

    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_task_storage_" + Guid.NewGuid().ToString("N"));
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

    // ═══════════════════════════════════════════════════════════════════════
    // Task<T> in compatible containers (zero diagnostics)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TaskFieldInRelevantContainer_ZeroDiagnostics()
    {
        const string source = """
            using Standard;
            relevant class Holder {
                Task<int> myTask;
                bool dropped;
                public Holder(Task<int> t) { this.myTask = t; this.dropped = false; }
                public void Drop() { if (dropped) { return; } this.dropped = true; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    [Fact]
    public void TaskFieldInLinearContainer_ZeroDiagnostics()
    {
        const string source = """
            using Standard;
            linear class Holder {
                Task<int> myTask;
                public Holder(Task<int> t) { this.myTask = t; }
                public void Drop() { }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task<T> in incompatible containers (MIST_DROP_FIELD_NOT_OWNED)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TaskFieldInAffineContainer_EmitsDisciplineCompatibilityDiagnostic()
    {
        const string source = """
            using Standard;
            affine class Holder {
                Task<int> myTask;
                public Holder(Task<int> t) { this.myTask = t; }
                public void Drop() { }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
        Assert.Contains("Affine", diag.Message);
        Assert.Contains("relevant", diag.Message);
        Assert.Contains("myTask", diag.Message);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    [Fact]
    public void TaskFieldInUnrestrictedContainer_EmitsDisciplineCompatibilityDiagnostic()
    {
        const string source = """
            using Standard;
            class Holder {
                Task<int> myTask;
                public Holder(Task<int> t) { this.myTask = t; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
        Assert.Contains("relevant", diag.Message);
        Assert.Contains("myTask", diag.Message);
        Assert.Contains("no substructural discipline", diag.Message);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ValueTask<T> in compatible containers (zero diagnostics)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValueTaskFieldInLinearContainer_ZeroDiagnostics()
    {
        const string source = """
            using Standard;
            linear class Holder {
                ValueTask<int> myTask;
                public Holder(ValueTask<int> t) { this.myTask = t; }
                public void Drop() { }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ValueTask<T> in incompatible containers (MIST_DROP_FIELD_NOT_OWNED)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValueTaskFieldInAffineContainer_EmitsDisciplineCompatibilityDiagnostic()
    {
        const string source = """
            using Standard;
            affine class Holder {
                ValueTask<int> myTask;
                public Holder(ValueTask<int> t) { this.myTask = t; }
                public void Drop() { }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
        Assert.Contains("Affine", diag.Message);
        Assert.Contains("linear", diag.Message);
        Assert.Contains("myTask", diag.Message);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    [Fact]
    public void ValueTaskFieldInRelevantContainer_EmitsDisciplineCompatibilityDiagnostic()
    {
        const string source = """
            using Standard;
            relevant class Holder {
                ValueTask<int> myTask;
                bool dropped;
                public Holder(ValueTask<int> t) { this.myTask = t; this.dropped = false; }
                public void Drop() { if (dropped) { return; } this.dropped = true; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
        Assert.Contains("Relevant", diag.Message);
        Assert.Contains("linear", diag.Message);
        Assert.Contains("myTask", diag.Message);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    [Fact]
    public void ValueTaskFieldInUnrestrictedContainer_EmitsDisciplineCompatibilityDiagnostic()
    {
        const string source = """
            using Standard;
            class Holder {
                ValueTask<int> myTask;
                public Holder(ValueTask<int> t) { this.myTask = t; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
        Assert.Contains("linear", diag.Message);
        Assert.Contains("myTask", diag.Message);
        Assert.Contains("no substructural discipline", diag.Message);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0110 no longer appears anywhere
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TaskAsMethodReturnType_NoDiagnostics()
    {
        const string source = """
            using Standard;
            class Foo {
                public Task<int> GetTask() { return default; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
    }

    [Fact]
    public void TaskAsMethodParameter_NoDiagnostics()
    {
        const string source = """
            using Standard;
            class Foo {
                public void Process(Task<int> task) { }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
    }

    [Fact]
    public void TaskAsLocalVariable_NoDiagnostics()
    {
        const string source = """
            using Standard;
            class Foo {
                public async int Compute() { return 42; }
                public void Run() {
                    var t = async Compute();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    [Fact]
    public void NonTaskFieldDeclaration_NoDiagnostics()
    {
        const string source = """
            using Standard;
            class Foo {
                int value;
                string name;
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = Compile(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_DROP_FIELD_NOT_OWNED");
    }
}