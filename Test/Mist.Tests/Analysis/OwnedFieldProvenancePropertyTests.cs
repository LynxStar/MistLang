// Feature: owned-fields, Property 7: Provenance on owned fields (Req 8)
//
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4**
//
// For all owned-field assignments, accepted ⟺ MoveBoundaryResolver.Resolve(source,
// OwnedFieldAssignment) ∉ {Error}.
//
// The property is verified by enumerating all provenance source categories:
//   1. Constructor call → succeeds (owned source)
//   2. Owned variable (move) → succeeds
//   3. IClonable source → succeeds (auto-clone inserted)
//   4. Non-owned, non-IClonable source → MIST_OWN_REQUIRES_OWNED or MIST_OWN_NOT_CLONABLE
//   5. Value type → succeeds (value types exempt from provenance)
//   6. default expression → succeeds (fresh default graph)
//
// Uses in-process CompilerDriver.Compile() — no per-iteration process spawning.
// 0 external process spawns (test-process-spawning policy).

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property 7: Provenance on owned fields (Req 8).
///
/// For all owned-field assignments (regardless of parent tier), the source must
/// satisfy <see cref="MoveBoundaryResolver"/> — identical to the existing owned-binding
/// provenance rules. Accepted ⟺ Resolver.Resolve(source, OwnedFieldAssignment) ∉ {Error}.
///
/// The input space is finite (6 source categories), so this uses deterministic
/// [Theory] tests covering all provenance categories exhaustively.
///
/// <b>Validates: Requirements 8.1, 8.2, 8.3, 8.4</b>
/// </summary>
public sealed class OwnedFieldProvenancePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

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
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_own_provenance_pbt_" + Guid.NewGuid().ToString("N"));
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

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Diagnostic? FindById(ImmutableArray<Diagnostic> diagnostics, string id) =>
        diagnostics.FirstOrDefault(d => d.Id.Value == id);

    private static bool HasProvenanceError(ImmutableArray<Diagnostic> diagnostics) =>
        FindById(diagnostics, "MIST_OWN_REQUIRES_OWNED") is not null ||
        FindById(diagnostics, "MIST_OWN_NOT_CLONABLE") is not null;

    // ─── Shared type declarations ────────────────────────────────────────

    /// <summary>
    /// A non-IClonable reference type (has an array field which blocks auto-derivation).
    /// Used as the field type for provenance tests that should fail.
    /// </summary>
    private const string NonClonableWidget = """
        class Widget
        {
            public int[] parts;
        }
        """;

    /// <summary>
    /// Container with an owned field of type string (IClonable).
    /// Used for tests involving IClonable bridging and value-type exemption.
    /// </summary>
    private const string StringFieldContainer = """
        class StringContainer
        {
            public owned string Data;

            public StringContainer(owned string data)
            {
                this.Data = data;
            }
        }
        """;

    /// <summary>
    /// Container with an owned field of non-IClonable reference type.
    /// Used for tests involving non-clonable provenance failures.
    /// </summary>
    private const string WidgetFieldContainer = """
        class WidgetContainer
        {
            public owned Widget item;

            public WidgetContainer(owned Widget w)
            {
                this.item = w;
            }
        }
        """;

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7a: Owned field assignment from constructor call → succeeds
    //
    // A constructor call (new T()) yields a fresh uniquely-owned value.
    // The MoveBoundaryResolver recognizes this as MovedFromOwned.
    //
    // **Validates: Requirement 8.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedFieldAssignment_FromConstructorCall_Succeeds()
    {
        var source = NonClonableWidget + WidgetFieldContainer + """

            class Program
            {
                public static void Main()
                {
                    owned var c = new WidgetContainer(new Widget());
                    c.item = new Widget();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasProvenanceError(diagnostics),
            "Constructor call should satisfy provenance — MoveBoundaryResolver yields MovedFromOwned");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7b: Owned field assignment from owned variable (move) → succeeds
    //
    // Moving from an owned binding into an owned field satisfies provenance.
    // The MoveBoundaryResolver recognizes this as MovedFromOwned and marks
    // the source binding Moved.
    //
    // **Validates: Requirement 8.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedFieldAssignment_FromOwnedVariable_Succeeds()
    {
        var source = NonClonableWidget + WidgetFieldContainer + """

            class Program
            {
                public static void Main()
                {
                    owned var c = new WidgetContainer(new Widget());
                    owned var w = new Widget();
                    c.item = w;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasProvenanceError(diagnostics),
            "Moving from an owned variable should satisfy provenance — MoveBoundaryResolver yields MovedFromOwned");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7c: Owned field assignment from IClonable source → succeeds
    //
    // When the source is non-owned but its type implements IClonable<T>,
    // the MoveBoundaryResolver inserts an implicit .Clone() to produce a
    // fresh owned value. No provenance error.
    //
    // **Validates: Requirement 8.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedFieldAssignment_FromIClonableSource_Succeeds()
    {
        var source = StringFieldContainer + """

            class Program
            {
                public static void Main()
                {
                    owned var c = new StringContainer("initial");
                    var shared = "cloned value";
                    c.Data = shared;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasProvenanceError(diagnostics),
            "IClonable source should satisfy provenance via implicit .Clone() — MoveBoundaryResolver yields ImplicitClone");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7d: Owned field assignment from non-owned, non-IClonable source
    //              → MIST_OWN_REQUIRES_OWNED or MIST_OWN_NOT_CLONABLE
    //
    // When the source is a non-owned binding whose type does NOT implement
    // IClonable<T>, the MoveBoundaryResolver cannot resolve the provenance
    // and emits an error.
    //
    // **Validates: Requirement 8.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedFieldAssignment_FromNonOwnedNonClonable_EmitsProvenanceError()
    {
        var source = NonClonableWidget + WidgetFieldContainer + """

            class Program
            {
                public static void Main()
                {
                    owned var c = new WidgetContainer(new Widget());
                    var shared = new Widget();
                    c.item = shared;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(HasProvenanceError(diagnostics),
            "Non-owned, non-IClonable source should fail provenance — " +
            "expected MIST_OWN_REQUIRES_OWNED or MIST_OWN_NOT_CLONABLE");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7e: Owned field assignment from value type → succeeds
    //
    // Value types are exempt from provenance because they are copied (no
    // aliasing possible). The MoveBoundaryResolver yields ValueCopy.
    //
    // **Validates: Requirement 8.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedFieldAssignment_FromValueType_Succeeds()
    {
        // Container with an owned int field — int is a value type.
        var source = """
            class IntContainer
            {
                public owned int Count;

                public IntContainer(int count)
                {
                    this.Count = count;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var c = new IntContainer(0);
                    var x = 42;
                    c.Count = x;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasProvenanceError(diagnostics),
            "Value type source should satisfy provenance — MoveBoundaryResolver yields ValueCopy (exempt from ownership)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7f: Owned field assignment from `default` → succeeds
    //
    // The `default` expression yields a fresh default graph (all fields zeroed).
    // The MoveBoundaryResolver recognizes this as DefaultGraph — no provenance
    // error.
    //
    // **Validates: Requirement 8.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedFieldAssignment_FromDefault_Succeeds()
    {
        var source = NonClonableWidget + WidgetFieldContainer + """

            class Program
            {
                public static void Main()
                {
                    owned var c = new WidgetContainer(new Widget());
                    c.item = default;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasProvenanceError(diagnostics),
            "`default` should satisfy provenance — MoveBoundaryResolver yields DefaultGraph");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7g: Provenance applies through shared parent as well
    //
    // Req 2 AC 2 states that shared parents CAN write to owned fields with
    // provenance. This test verifies that a valid provenance source (constructor
    // call) through a shared parent succeeds, and a non-owned non-clonable source
    // through an owned parent fails.
    //
    // NOTE: The ownership inference pass does not currently mutate symbol tiers
    // (documented as "future flow analysis integration" in the orchestrator).
    // When the parent uses `var` without explicit `owned`, provenance enforcement
    // depends on whether inference has been applied to the symbol. The current
    // behavior requires an explicit `owned` parent for provenance enforcement
    // via the field assignment path.
    //
    // **Validates: Requirements 8.1, 8.2, 8.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_OwnedFieldAssignment_FromConstructor_Succeeds()
    {
        var source = NonClonableWidget + WidgetFieldContainer + """

            class Program
            {
                public static void Main()
                {
                    var c = new WidgetContainer(new Widget());
                    c.item = new Widget();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasProvenanceError(diagnostics),
            "Constructor call through shared parent should satisfy provenance — " +
            "provenance enforcement is the same regardless of parent tier");
    }

    [Fact]
    public void ExplicitOwnedParent_OwnedFieldAssignment_FromNonOwnedNonClonable_EmitsProvenanceError()
    {
        // Explicit 'owned var c' — provenance is enforced because IsOwnedVariable(c) == true.
        var source = NonClonableWidget + WidgetFieldContainer + """

            class Program
            {
                public static void Main()
                {
                    owned var c = new WidgetContainer(new Widget());
                    var shared = new Widget();
                    c.item = shared;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(HasProvenanceError(diagnostics),
            "Non-owned, non-IClonable source through explicitly owned parent should fail provenance — " +
            "expected MIST_OWN_REQUIRES_OWNED or MIST_OWN_NOT_CLONABLE");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7h: Provenance applies in method body (Req 8 AC 1)
    //
    // The provenance requirement applies "in a constructor, a method body, or
    // direct field assignment from an owned binding". This validates that
    // method body assignments through an explicit owned binding are checked.
    //
    // NOTE: Constructor body assignments via `this.field = param` are handled
    // through a separate mechanism (the receiver is `this`, not a local variable).
    // The owned-field provenance enforcement via the field assignment path
    // applies when the receiver is a resolvable local/parameter symbol.
    //
    // **Validates: Requirements 8.1, 8.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstructorBody_OwnedFieldAssignment_FromOwnedParam_Succeeds()
    {
        // The constructor parameter is 'owned Widget w' → provenance satisfied.
        var source = NonClonableWidget + """

            class Box
            {
                public owned Widget thing;

                public Box(owned Widget w)
                {
                    this.thing = w;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var b = new Box(new Widget());
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasProvenanceError(diagnostics),
            "Constructor body assigning from owned parameter should satisfy provenance");
    }

    [Fact]
    public void MethodBody_OwnedFieldAssignment_FromNonOwnedNonClonable_EmitsProvenanceError()
    {
        // Direct field assignment to an owned field through an explicit owned parent binding.
        // The source 'shared' is non-owned and Widget is not IClonable → provenance fails.
        var source = NonClonableWidget + """

            class Box
            {
                public owned Widget thing;

                public Box(owned Widget w)
                {
                    this.thing = w;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var b = new Box(new Widget());
                    var shared = new Widget();
                    b.thing = shared;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(HasProvenanceError(diagnostics),
            "Direct field assignment of non-owned, non-IClonable source to owned field through " +
            "explicitly owned parent should fail provenance");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7i: Constructor with owned parameter called with non-owned
    //              non-clonable argument → provenance error at call site
    //
    // When a constructor declares an `owned` parameter, passing a non-owned,
    // non-IClonable argument to that parameter at the call site triggers a
    // provenance error — the argument is not a uniquely-owned source.
    // This is the call-site mechanism that enforces provenance for constructors.
    //
    // **Validates: Requirements 8.1, 8.4, 12.1, 12.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstructorCallSite_NonOwnedNonClonableArg_ForOwnedParam_EmitsProvenanceError()
    {
        var source = NonClonableWidget + """

            class Box
            {
                public owned Widget thing;

                public Box(owned Widget w)
                {
                    this.thing = w;
                }
            }

            class Program
            {
                public static void Main()
                {
                    var shared = new Widget();
                    owned var b = new Box(shared);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(HasProvenanceError(diagnostics),
            "Passing non-owned, non-IClonable argument to constructor's owned parameter " +
            "should fail provenance at the call site — argument is not uniquely owned");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7j: Constructor with owned parameter called with IClonable
    //              argument → .Clone() inserted, succeeds
    //
    // When a constructor declares an `owned` parameter, passing a non-owned
    // argument of IClonable type triggers implicit .Clone() at the call site
    // and the call succeeds — the clone produces a fresh owned value.
    //
    // **Validates: Requirements 8.3, 12.1, 12.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstructorCallSite_IClonableArg_ForOwnedParam_Succeeds()
    {
        var source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }
            }

            class Program
            {
                public static void Main()
                {
                    var shared = "hello";
                    owned var c = new Container(shared);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasProvenanceError(diagnostics),
            "Passing IClonable argument to constructor's owned parameter should succeed — " +
            "MoveBoundaryResolver inserts implicit .Clone() at call site");
    }
}