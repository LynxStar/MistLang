// Feature: ownership-v2, Task 6.3 — unit tests for the CooperationContractVerifier
// (design Component 3, Req 5 AC 3–4, Req 13 AC 4–5, design LD-2).
//
// The CooperationContractVerifier is wired into OwnershipAnalyzer Pass C (task 8.1),
// so these are end-to-end compilation tests: a Mist program declaring an `owned`
// type is compiled in-process through the full driver and the resulting diagnostics
// are inspected for MIST_OWN_CONTRACT_VIOLATION.
//
// Two groups of cases (per task 6.3):
//
//   COMPLIANT — no MIST_OWN_CONTRACT_VIOLATION is emitted when an owned type's
//   method/ctor assigns this.Field from an owned source:
//     1. owned parameter            (Req 6 AC 3 — owned params seed Live)
//     2. constructor call (new T()) (Req 6 AC 9 — fresh owned graph)
//     3. clonable source            (Req 8 — implicit clone bridges to owned)
//     4. default                    (Req 6 AC 10 — default yields ownership)
//     5. value/primitive            (Req 4 AC 3 — value copy, no provenance gate)
//
//   VIOLATING — MIST_OWN_CONTRACT_VIOLATION is emitted, naming the method, the
//   field, and the source expression:
//     6. non-owned reference parameter (Req 13 AC 4 — non-owned source)
//     7. static/global read           (design LD-2 — global state never owned)
//
// All compilation is in-process via a single shared CompilerDriver (test-process-
// spawning policy: 0 external process spawns).

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for the <see cref="CooperationContractVerifier"/> (ownership-v2 design
/// Component 3, Task 6.3). Verifies that <c>owned</c> types whose method/constructor
/// bodies populate fields from owned sources compile cleanly, while bodies that
/// populate a field from a non-owned reference parameter or a static/global read
/// (design LD-2) emit <c>MIST_OWN_CONTRACT_VIOLATION</c> naming the method, field,
/// and source.
///
/// **Validates: Requirements 5.4, 13.4, 13.5**
/// </summary>
public sealed class CooperationContractVerifierTests
{
    private const string ContractViolationId = "MIST_OWN_CONTRACT_VIOLATION";

    // ─── Shared infrastructure (in-process compile, 0 process spawns) ─────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_own_contract_" + Guid.NewGuid().ToString("N"));
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
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Diagnostic? FindContractViolation(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(d => d.Id.Value == ContractViolationId);

    private static void AssertNoContractViolation(ImmutableArray<Diagnostic> diagnostics)
    {
        var diag = FindContractViolation(diagnostics);
        Assert.True(
            diag is null,
            $"Expected no {ContractViolationId}, but got: {diag?.Message}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // COMPLIANT bodies — no contract violation.
    // ═══════════════════════════════════════════════════════════════════════

    // 1. Field assigned from an OWNED PARAMETER (ctor and mutable method).
    //    Owned params seed Live, so the move into the field is valid (Req 6 AC 3).
    [Fact]
    public void Compliant_OwnedParameter_NoViolation()
    {
        const string source = """
            namespace TestProgram;

            class Widget
            {
                public int Value;
                public Widget(int value)
                {
                    this.Value = value;
                }
            }

            owned class Container
            {
                public Widget Inner;

                public Container(owned Widget inner)
                {
                    this.Inner = inner;
                }

                public void Replace(owned Widget replacement)
                {
                    this.Inner = replacement;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        AssertNoContractViolation(CompileAndGetDiagnostics(source));
    }

    // 2. Field assigned from a CONSTRUCTOR CALL (new T()).
    //    A fresh constructor call yields a uniquely-owned graph (Req 6 AC 9).
    [Fact]
    public void Compliant_ConstructorCall_NoViolation()
    {
        const string source = """
            namespace TestProgram;

            class Widget
            {
                public int Value;
                public Widget(int value)
                {
                    this.Value = value;
                }
            }

            owned class Container
            {
                public Widget Inner;

                public Container()
                {
                    this.Inner = new Widget(1);
                }

                public void Reset()
                {
                    this.Inner = new Widget(2);
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        AssertNoContractViolation(CompileAndGetDiagnostics(source));
    }

    // 3. Field assigned from a CLONABLE SOURCE (implicit clone bridges to owned).
    //    'Config' auto-derives IClonable<Config> (all primitive/string fields), so a
    //    non-owned 'Config' parameter assigned into a field resolves as ImplicitClone,
    //    not Error (Req 8 AC 2–3, AC 6).
    [Fact]
    public void Compliant_ClonableSource_NoViolation()
    {
        const string source = """
            namespace TestProgram;

            class Config
            {
                public int Timeout;
                public Config(int timeout)
                {
                    this.Timeout = timeout;
                }
            }

            owned class Vault
            {
                public Config Setting;

                public Vault(Config setting)
                {
                    this.Setting = setting;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        AssertNoContractViolation(CompileAndGetDiagnostics(source));
    }

    // 4. Field assigned from DEFAULT (default yields a fresh owned graph, Req 6 AC 10).
    [Fact]
    public void Compliant_DefaultSource_NoViolation()
    {
        const string source = """
            namespace TestProgram;

            class Widget
            {
                public int Value;
                public Widget(int value)
                {
                    this.Value = value;
                }
            }

            owned class Container
            {
                public Widget Inner;

                public Container()
                {
                    this.Inner = default;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        AssertNoContractViolation(CompileAndGetDiagnostics(source));
    }

    // 5. Field assigned from a VALUE/PRIMITIVE source (value copy, no provenance gate).
    //    Primitive sources are copied, never aliased, so they never violate the
    //    contract regardless of where the value comes from (Req 4 AC 3).
    [Fact]
    public void Compliant_ValuePrimitiveSource_NoViolation()
    {
        const string source = """
            namespace TestProgram;

            owned class Counter
            {
                public int Value;

                public Counter(int seed)
                {
                    this.Value = seed;
                }

                public void Bump(int amount)
                {
                    this.Value = amount;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        AssertNoContractViolation(CompileAndGetDiagnostics(source));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VIOLATING bodies — MIST_OWN_CONTRACT_VIOLATION naming method/field/source.
    // ═══════════════════════════════════════════════════════════════════════

    // 6. Field assigned from a NON-OWNED REFERENCE PARAMETER (Req 13 AC 4).
    //    'Gadget' is non-clonable (an array field blocks IClonable auto-derivation),
    //    so a non-owned 'Gadget' parameter cannot be moved or cloned into the field —
    //    the contract is violated.
    [Fact]
    public void Violating_NonOwnedReferenceParameter_EmitsViolationNamingMethodFieldSource()
    {
        const string source = """
            namespace TestProgram;

            class Gadget
            {
                public int[] data;
            }

            owned class Vault
            {
                public Gadget setting;

                public void Configure(Gadget input)
                {
                    this.setting = input;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindContractViolation(diagnostics);

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        // Names the method, the field, and the non-owned source expression.
        Assert.Contains("Configure", diag.Message, StringComparison.Ordinal);
        Assert.Contains("setting", diag.Message, StringComparison.Ordinal);
        Assert.Contains("input", diag.Message, StringComparison.Ordinal);
    }

    // 7. Field assigned from a STATIC/GLOBAL READ (design LD-2).
    //    A static read yields neither an owned value nor a fresh graph; treating it as
    //    clonable would silently duplicate global state. Even though 'Config' is
    //    clonable, the static read is rejected directly as a contract violation.
    [Fact]
    public void Violating_StaticGlobalRead_EmitsViolationNamingMethodFieldSource()
    {
        const string source = """
            namespace TestProgram;

            class Config
            {
                public int Timeout;
                public Config(int timeout)
                {
                    this.Timeout = timeout;
                }
            }

            class Registry
            {
                public static Config Shared { get; set; }
            }

            owned class Vault
            {
                public Config setting;

                public void LoadShared()
                {
                    this.setting = Registry.Shared;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindContractViolation(diagnostics);

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        // Names the method and the field; the source names the static read.
        Assert.Contains("LoadShared", diag.Message, StringComparison.Ordinal);
        Assert.Contains("setting", diag.Message, StringComparison.Ordinal);
        Assert.Contains("Shared", diag.Message, StringComparison.Ordinal);
    }
}
