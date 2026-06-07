// Feature: ownership-v2, Task 8.4 — unit tests asserting removed diagnostics never fire.
//
// In v1, the ownership analyzer emitted:
//   - MIST_OWN_FIELD_NOT_OWNED: when a field in an owned type was not itself an owned type
//   - MIST_OWN_TYPE_REQUIRES_OWNED: when an owned binding was assigned a non-owned type
//     (the structural "is this type eligible for ownership" check)
//
// In v2, both are removed (Requirements 10.2, 10.3, 10.4):
//   - Owned types can have any field type (cooperation contract replaces field restriction)
//   - Any non-substructural type can be owned at the binding level (no type-level eligibility)
//
// These tests compile programs that WOULD HAVE triggered these diagnostics in v1 and
// assert they compile cleanly (zero occurrences of those diagnostic ids) under v2 rules.
//
// In-process only: 0 external process spawns (test-process-spawning policy).

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
/// Unit tests asserting that the removed v1 diagnostics <c>MIST_OWN_FIELD_NOT_OWNED</c>
/// and structural <c>MIST_OWN_TYPE_REQUIRES_OWNED</c> are never emitted under v2 rules.
/// Programs that would have triggered these diagnostics in v1 must compile cleanly now.
///
/// **Validates: Requirements 10.2, 10.3, 10.4**
/// </summary>
public sealed class RemovedDiagnosticsNeverFireTests
{
    private const string FieldNotOwnedId = "MIST_OWN_FIELD_NOT_OWNED";
    private const string TypeRequiresOwnedId = "MIST_OWN_TYPE_REQUIRES_OWNED";

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
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_removed_diag_" + Guid.NewGuid().ToString("N"));
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

    private static void AssertNeitherRemovedDiagnosticFires(ImmutableArray<Diagnostic> diagnostics)
    {
        var fieldNotOwned = diagnostics.FirstOrDefault(d => d.Id.Value == FieldNotOwnedId);
        Assert.True(
            fieldNotOwned is null,
            $"Expected {FieldNotOwnedId} to never fire, but got: {fieldNotOwned?.Message}");

        var typeRequiresOwned = diagnostics.FirstOrDefault(d => d.Id.Value == TypeRequiresOwnedId);
        Assert.True(
            typeRequiresOwned is null,
            $"Expected {TypeRequiresOwnedId} to never fire, but got: {typeRequiresOwned?.Message}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_OWN_FIELD_NOT_OWNED — never fires (Req 10.2)
    //
    // In v1, an owned type with a non-owned reference-type field would trigger
    // this diagnostic. In v2, owned types can have any field type.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An owned class with a non-owned class field compiles cleanly (Req 10.2).
    /// v1 would have reported MIST_OWN_FIELD_NOT_OWNED on the 'widget' field.
    /// </summary>
    [Fact]
    public void OwnedType_NonOwnedClassField_NoFieldNotOwnedDiagnostic()
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
                public Widget widget;

                public Container(owned Widget w)
                {
                    this.widget = w;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        AssertNeitherRemovedDiagnosticFires(CompileAndGetDiagnostics(source));
    }

    /// <summary>
    /// An owned class with multiple non-owned reference-type fields compiles cleanly (Req 10.2).
    /// v1 would have reported MIST_OWN_FIELD_NOT_OWNED for each non-owned field.
    /// </summary>
    [Fact]
    public void OwnedType_MultipleNonOwnedFields_NoFieldNotOwnedDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            class Gadget
            {
                public int Id;
                public Gadget(int id)
                {
                    this.Id = id;
                }
            }

            class Config
            {
                public int Timeout;
                public Config(int timeout)
                {
                    this.Timeout = timeout;
                }
            }

            owned class MultiHolder
            {
                public Gadget gadget;
                public Config config;
                public int count;

                public MultiHolder(owned Gadget g, owned Config c)
                {
                    this.gadget = g;
                    this.config = c;
                    this.count = 0;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        AssertNeitherRemovedDiagnosticFires(CompileAndGetDiagnostics(source));
    }

    /// <summary>
    /// An owned struct with a non-owned reference-type field compiles cleanly (Req 10.2).
    /// v1 would have reported MIST_OWN_FIELD_NOT_OWNED on struct fields too.
    /// </summary>
    [Fact]
    public void OwnedStruct_NonOwnedClassField_NoFieldNotOwnedDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            class Label
            {
                public string Text;
                public Label(string text)
                {
                    this.Text = text;
                }
            }

            owned struct TaggedValue
            {
                public int Value;
                public Label label;

                public TaggedValue(int value, owned Label l)
                {
                    this.Value = value;
                    this.label = l;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        AssertNeitherRemovedDiagnosticFires(CompileAndGetDiagnostics(source));
    }

    /// <summary>
    /// An owned class with a field of another owned type alongside non-owned fields
    /// compiles cleanly (Req 10.2). v1 would accept the owned-typed field but reject
    /// the non-owned one; v2 accepts both unconditionally.
    /// </summary>
    [Fact]
    public void OwnedType_MixedOwnedAndNonOwnedFields_NoFieldNotOwnedDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            owned class Inner
            {
                public int X;
                public Inner(int x)
                {
                    this.X = x;
                }
            }

            class Unowned
            {
                public int Y;
                public Unowned(int y)
                {
                    this.Y = y;
                }
            }

            owned class Composite
            {
                public Inner ownedPart;
                public Unowned unownedPart;

                public Composite(owned Inner a, owned Unowned b)
                {
                    this.ownedPart = a;
                    this.unownedPart = b;
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        AssertNeitherRemovedDiagnosticFires(CompileAndGetDiagnostics(source));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_OWN_TYPE_REQUIRES_OWNED — never fires (Req 10.3)
    //
    // In v1, this diagnostic was emitted when an owned binding was initialized
    // with a type that was not declared 'owned' (the structural eligibility check).
    // In v2, any non-substructural type can be used in an owned binding.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An owned binding of a non-owned class compiles cleanly (Req 10.3).
    /// v1 would have reported MIST_OWN_TYPE_REQUIRES_OWNED because Widget is not
    /// declared 'owned'.
    /// </summary>
    [Fact]
    public void OwnedBinding_NonOwnedClass_NoTypeRequiresOwnedDiagnostic()
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

            class Program
            {
                public static void Main()
                {
                    owned var w = new Widget(42);
                }
            }
            """;

        AssertNeitherRemovedDiagnosticFires(CompileAndGetDiagnostics(source));
    }

    /// <summary>
    /// An owned binding of a non-owned struct compiles cleanly (Req 10.3, 10.4).
    /// v1 would have reported MIST_OWN_TYPE_REQUIRES_OWNED because Point is not
    /// declared 'owned'.
    /// </summary>
    [Fact]
    public void OwnedBinding_NonOwnedStruct_NoTypeRequiresOwnedDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            struct Point
            {
                public int X;
                public int Y;

                public Point(int x, int y)
                {
                    this.X = x;
                    this.Y = y;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var p = new Point(1, 2);
                }
            }
            """;

        AssertNeitherRemovedDiagnosticFires(CompileAndGetDiagnostics(source));
    }

    /// <summary>
    /// An explicitly-typed owned binding of a non-owned class compiles cleanly (Req 10.3).
    /// Uses the form 'owned Widget w = ...' rather than 'owned var w = ...'.
    /// </summary>
    [Fact]
    public void OwnedBinding_ExplicitType_NonOwnedClass_NoTypeRequiresOwnedDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            class Gadget
            {
                public int Id;
                public Gadget(int id)
                {
                    this.Id = id;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned Gadget g = new Gadget(7);
                }
            }
            """;

        AssertNeitherRemovedDiagnosticFires(CompileAndGetDiagnostics(source));
    }

    /// <summary>
    /// An owned parameter of a non-owned type compiles cleanly (Req 10.3, 10.4).
    /// v1 would have required the parameter type to be declared 'owned'.
    /// </summary>
    [Fact]
    public void OwnedParameter_NonOwnedType_NoTypeRequiresOwnedDiagnostic()
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

            class Program
            {
                public static void Take(owned Widget w)
                {
                }

                public static void Main()
                {
                    owned var w = new Widget(1);
                    Take(w);
                }
            }
            """;

        AssertNeitherRemovedDiagnosticFires(CompileAndGetDiagnostics(source));
    }

    /// <summary>
    /// Existing code using owned type declarations with non-owned field types
    /// compiles without error under v2 rules (Req 10.4). This exercises the
    /// "existing codebase" acceptance criterion.
    /// </summary>
    [Fact]
    public void ExistingCodebase_OwnedTypeWithNonOwnedFields_CompilesCleanly()
    {
        const string source = """
            namespace TestProgram;

            class Logger
            {
                public string Prefix;
                public Logger(string prefix)
                {
                    this.Prefix = prefix;
                }
            }

            class Connection
            {
                public int Port;
                public Connection(int port)
                {
                    this.Port = port;
                }
            }

            owned class Service
            {
                public Logger log;
                public Connection conn;
                public int RequestCount;

                public Service(owned Logger log, owned Connection conn)
                {
                    this.log = log;
                    this.conn = conn;
                    this.RequestCount = 0;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var svc = new Service(new Logger("app"), new Connection(8080));
                }
            }
            """;

        AssertNeitherRemovedDiagnosticFires(CompileAndGetDiagnostics(source));
    }
}
