// Feature: optional-types + as-operator interaction
// Validates that direct `.` member access on T | void is rejected,
// requiring `?.` for safe access. Prevents regression of the void-skip
// hack that was removed from CheckUnionMemberAccess.

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
/// Tests verifying that direct <c>.</c> member access on an optional type
/// (<c>T | void</c>) is rejected by the type checker, and that <c>?.</c>
/// is required to safely unwrap.
///
/// These tests guard against re-introduction of the void-skip hack in
/// <c>CheckUnionMemberAccess</c> that previously allowed bare <c>.</c>
/// on <c>T | void</c> receivers.
/// </summary>
public sealed class OptionalMemberAccessSafetyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_opt_safety_" + Guid.NewGuid().ToString("N"));
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
                EmitToDisk: false,
                GlobalUsings: ImmutableArray.Create("Standard"));

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void AssertNoDiagnosticErrors(ImmutableArray<Diagnostic> diagnostics, string context = "")
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors{(context.Length > 0 ? $" ({context})" : "")} but got:\n" +
            string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}")));
    }

    private static void AssertHasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string expectedId, string context = "")
    {
        var matching = diagnostics.Where(d => d.Id.Value == expectedId).ToList();
        Assert.True(matching.Count > 0,
            $"Expected diagnostic {expectedId}{(context.Length > 0 ? $" ({context})" : "")} but got:\n" +
            string.Join("\n", diagnostics.Select(e => $"  {e.Id.Value}: {e.Message}")));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DIRECT . ON T | void IS REJECTED
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Direct method call via <c>.</c> on a non-provable <c>as</c> result
    /// (typed as <c>T | void</c>) must be rejected.
    /// </summary>
    [Fact]
    public void DirectMethodCall_OnNonProvableAs_IsRejected()
    {
        const string source = """
            class Animal { }
            class Dog : Animal {
                public void Bark() { }
            }
            class Program {
                public static void Main() {
                    Animal animal = new Dog();
                    var maybeDog = animal as Dog;
                    maybeDog.Bark();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertHasDiagnostic(diagnostics, "MIST_TC_0073", "direct . on non-provable as result");
    }

    /// <summary>
    /// Direct property access via <c>.</c> on a typed <c>T?</c> variable
    /// must be rejected.
    /// </summary>
    [Fact]
    public void DirectMemberAccess_OnTypedOptionalVariable_IsRejected()
    {
        const string source = """
            class Inner {
                public int value;
                public Inner(int v) { this.value = v; }
            }
            class Program {
                public static void Main() {
                    Inner? maybe = new Inner(5);
                    var x = maybe.value;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertHasDiagnostic(diagnostics, "MIST_TC_0073", "direct . on T? variable");
    }

    /// <summary>
    /// Direct method call via <c>.</c> on a typed <c>T?</c> variable
    /// must be rejected.
    /// </summary>
    [Fact]
    public void DirectMethodCall_OnTypedOptionalVariable_IsRejected()
    {
        const string source = """
            class Inner {
                public int GetValue() { return 42; }
            }
            class Program {
                public static void Main() {
                    Inner? maybe = new Inner();
                    var x = maybe.GetValue();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertHasDiagnostic(diagnostics, "MIST_TC_0073", "direct .Method() on T? variable");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ?. ON T | void COMPILES SUCCESSFULLY
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Optional chaining <c>?.</c> on a non-provable <c>as</c> result compiles.
    /// </summary>
    [Fact]
    public void OptionalChaining_OnNonProvableAs_Compiles()
    {
        const string source = """
            class Animal { }
            class Dog : Animal {
                public void Bark() { }
            }
            class Program {
                public static void Main() {
                    Animal animal = new Dog();
                    var maybeDog = animal as Dog;
                    maybeDog?.Bark();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "?. on non-provable as result");
    }

    /// <summary>
    /// Optional chaining <c>?.</c> on a typed <c>T?</c> variable compiles.
    /// </summary>
    [Fact]
    public void OptionalChaining_OnTypedOptionalVariable_Compiles()
    {
        const string source = """
            class Inner {
                public int GetValue() { return 42; }
            }
            class Program {
                public static void Main() {
                    Inner? maybe = new Inner();
                    int? x = maybe?.GetValue();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "?. on T? variable");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // var INFERS T | void FROM NON-PROVABLE as
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>var x = expr as T</c> where the cast is non-provable must infer
    /// the variable type as <c>T | void</c>, not bare <c>T</c>. Verified
    /// by assigning the variable to a typed <c>T</c> target (should fail).
    /// </summary>
    [Fact]
    public void VarInference_NonProvableAs_InfersTOrVoid()
    {
        const string source = """
            class Animal { }
            class Dog : Animal { }
            class Program {
                public static void Main() {
                    Animal animal = new Dog();
                    var maybeDog = animal as Dog;
                    Dog definitelyDog = maybeDog;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count > 0,
            "Expected a type error assigning Dog|void to Dog, but got none — " +
            "var did not infer T|void from non-provable as expression.");
    }

    /// <summary>
    /// <c>var x = expr as T</c> where the cast is non-provable: accessing
    /// a member via <c>.</c> on the var must be rejected (proving the var
    /// is typed as <c>T | void</c>, not bare <c>T</c>).
    /// </summary>
    [Fact]
    public void VarInference_NonProvableAs_RejectsDirectMemberAccess()
    {
        const string source = """
            class Animal { }
            class Dog : Animal {
                public void Bark() { }
            }
            class Program {
                public static void Main() {
                    Animal animal = new Dog();
                    var maybeDog = animal as Dog;
                    maybeDog.Bark();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertHasDiagnostic(diagnostics, "MIST_TC_0073",
            "var from non-provable as should be T|void, rejecting direct .");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PROVABLE as STILL ALLOWS DIRECT .
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A provable <c>as</c> (widening) produces bare <c>T</c>, so direct
    /// <c>.</c> member access must still work without error.
    /// </summary>
    [Fact]
    public void DirectMemberAccess_OnProvableAs_StillWorks()
    {
        const string source = """
            class Animal {
                public string Name;
                public Animal(string n) { this.Name = n; }
            }
            class Dog : Animal {
                public Dog(string n) : base(n) { }
            }
            class Program {
                public static void Main() {
                    var dog = new Dog("Rex");
                    var animal = dog as Animal;
                    var name = animal.Name;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "provable as (widening) allows direct .");
    }

    /// <summary>
    /// A provable <c>as</c> to a base type does NOT produce a union,
    /// so direct <c>.</c> access on the provable result continues to work.
    /// This guards against false positives from the void-union rejection.
    /// </summary>
    [Fact]
    public void DirectMemberAccess_OnProvableAs_NoFalsePositive()
    {
        const string source = """
            class Animal {
                public int Age;
                public Animal(int a) { this.Age = a; }
            }
            class Dog : Animal {
                public Dog(int a) : base(a) { }
            }
            class Program {
                public static void Main() {
                    var dog = new Dog(3);
                    var animal = dog as Animal;
                    var age = animal.Age;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "provable as (widening) var allows direct . — no false positive");
    }
}