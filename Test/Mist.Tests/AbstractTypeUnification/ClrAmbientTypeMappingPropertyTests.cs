// Feature: abstract-type-unification, Property 13: CLR ambient type mapping

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.AbstractTypeUnification;

/// <summary>
/// Deterministic example-based tests verifying that CLR types from referenced
/// assemblies are properly mapped by the ambient type provider.
///
/// Property 13: CLR ambient type mapping — For any CLR interface in a referenced
/// assembly, the ambient type provider SHALL expose it as an AbstractType with all
/// interface members as required slots. For any CLR abstract method on a class, it
/// SHALL appear as a required slot. For any CLR virtual method with an implementation,
/// it SHALL appear as a fillable but not required slot.
///
/// Since we cannot easily generate random CLR assemblies, this test uses well-known
/// .NET interfaces (IDisposable, IComparable&lt;T&gt;) that are always available from
/// the SDK reference assemblies. The test verifies that a Mist type can implement
/// these CLR interfaces by filling their required slots and that compilation succeeds.
///
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
/// Spawns 0 external processes.
///
/// **Validates: Requirements 8.1, 8.2, 8.3**
/// </summary>
public sealed class ClrAmbientTypeMappingPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public ClrAmbientTypeMappingPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistClrAmbientPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Returns all emitted C# content concatenated, or null if no files were emitted.
    /// </summary>
    private static string? GetAllEmittedCSharp(CompilerResult result)
    {
        if (result.EmittedFiles is null || result.EmittedFiles.Count == 0)
            return null;

        return string.Join("\n\n", result.EmittedFiles.Values);
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// A Mist type implementing System.IDisposable (a CLR interface with a single
    /// required slot: void Dispose()) should compile without errors. This verifies
    /// the ambient type provider correctly maps the CLR interface to an abstract type
    /// with Dispose() as a required slot.
    ///
    /// **Validates: Requirements 8.1**
    /// </summary>
    [Fact]
    public void ImplementingIDisposable_CompilesWithoutErrors()
    {
        var source = """
            using Standard;
            using System;

            namespace TestProject;

            public class MyDisposable : IDisposable
            {
                public void Dispose()
                {
                }
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Expected no compilation errors when implementing IDisposable.\n" +
            $"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        // Verify C# was emitted
        var emittedCSharp = GetAllEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        // The emitted C# should reference IDisposable in the class declaration
        Assert.Contains("IDisposable", emittedCSharp);
    }

    /// <summary>
    /// A Mist type implementing System.IComparable&lt;T&gt; (a generic CLR interface
    /// with a single required slot: int CompareTo(T other)) should compile without errors.
    /// This verifies the ambient type provider correctly maps generic CLR interfaces.
    ///
    /// **Validates: Requirements 8.1**
    /// </summary>
    [Fact]
    public void ImplementingIComparable_CompilesWithoutErrors()
    {
        var source = """
            using Standard;
            using System;

            namespace TestProject;

            public class Temperature : IComparable<Temperature>
            {
                private int degrees;

                public Temperature(int degrees)
                {
                    this.degrees = degrees;
                }

                public int CompareTo(Temperature other)
                {
                    return this.degrees - other.degrees;
                }
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Expected no compilation errors when implementing IComparable<T>.\n" +
            $"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        // Verify C# was emitted
        var emittedCSharp = GetAllEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        // The emitted C# should reference IComparable in the class declaration
        Assert.Contains("IComparable", emittedCSharp);
    }

    /// <summary>
    /// A Mist type that does NOT fill a required CLR interface slot (missing Dispose())
    /// should produce a compilation error. This verifies the ambient type provider
    /// correctly classifies CLR interface members as required slots.
    ///
    /// **Validates: Requirements 8.1**
    /// </summary>
    [Fact]
    public void MissingClrInterfaceSlot_ProducesError()
    {
        var source = """
            using Standard;
            using System;

            namespace TestProject;

            public class BadDisposable : IDisposable
            {
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        // Should fail because Dispose() slot is not filled
        Assert.True(errors.Count > 0,
            "Expected compilation errors when CLR interface slot is not filled, " +
            "but compilation succeeded without errors.");

        // The error should reference the missing member
        var errorMessages = string.Join(" ", errors.Select(d => d.Message));
        Assert.True(
            errorMessages.Contains("Dispose", StringComparison.OrdinalIgnoreCase),
            $"Expected error message to reference 'Dispose'. Actual errors:\n" +
            string.Join("\n", errors.Select(d => $"  {d.Id.Value}: {d.Message}")));
    }

    /// <summary>
    /// Filling Object.ToString() (a CLR virtual method on a base class) should NOT
    /// be required — the type should compile without providing its own ToString().
    /// This verifies CLR virtual methods with implementations are exposed as fillable
    /// but not required slots (slots with defaults).
    ///
    /// **Validates: Requirements 8.3**
    /// </summary>
    [Fact]
    public void VirtualMethodNotRequired_CompilesWithoutOverride()
    {
        var source = """
            namespace TestProject;

            public class SimpleClass
            {
                private int value;

                public SimpleClass(int value)
                {
                    this.value = value;
                }
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        // Should compile without errors — virtual methods (ToString, GetHashCode, Equals)
        // are not required to be filled
        Assert.True(errors.Count == 0,
            $"Expected no compilation errors for class that doesn't override virtual methods.\n" +
            $"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// A Mist type implementing a CLR interface with multiple members
    /// (System.Collections.Generic.IComparer&lt;T&gt; has Compare(T,T)) should compile
    /// when all slots are filled. This verifies multi-parameter CLR interface methods
    /// are correctly mapped.
    ///
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Fact]
    public void ImplementingIComparer_CompilesWithoutErrors()
    {
        var source = """
            using Standard;
            using System.Collections.Generic;

            namespace TestProject;

            public class IntComparer : IComparer<int>
            {
                public int Compare(int x, int y)
                {
                    return x - y;
                }
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Expected no compilation errors when implementing IComparer<int>.\n" +
            $"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        // Verify C# was emitted
        var emittedCSharp = GetAllEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        // The emitted C# should reference IComparer in the class declaration
        Assert.Contains("IComparer", emittedCSharp);
    }
}
