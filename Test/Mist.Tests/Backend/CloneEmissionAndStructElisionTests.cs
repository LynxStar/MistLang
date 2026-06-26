// Feature: ownership-v2, Task 12.3
// Unit tests for clone emission and pure-struct elision.
// Assert .Clone() emitted for clonable class sources; assert elided for pure-struct value types.
// _Requirements: 14.7, 8.8_

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying that the C# emitter inserts <c>.Clone()</c> at implicit-clone
/// sites for clonable class (reference-type) sources assigned to owned destinations,
/// and elides <c>.Clone()</c> for pure-struct (value-type) sources where copy semantics
/// already provide independence (Requirement 14 AC 7, Requirement 8 AC 8).
/// </summary>
public sealed class CloneEmissionAndStructElisionTests
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

    private static IReadOnlyDictionary<string, string>? CompileAndGetEmittedFiles(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_clone_emit_" + Guid.NewGuid().ToString("N"));
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

            if(!result.Succeeded)
                return null;

            return result.EmittedFiles;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Returns the concatenated content of all emitted .cs files (excluding .csproj).
    /// </summary>
    private static string? GetAllEmittedCSharp(string source)
    {
        var files = CompileAndGetEmittedFiles(source);
        if(files is null)
            return null;

        var csFiles = files
            .Where(kv => kv.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value);

        return string.Join("\n", csFiles);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Clone emission: clonable class source → .Clone() present
    // Requirements: 14.7, 8.8
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClonableClassSource_OwnedBindingInit_EmitsCloneCall()
    {
        // A clonable class (all fields are clonable primitives) assigned from a
        // non-owned binding into an owned binding should emit .Clone().
        const string source = """
            owned class Container {
                int value;

                public Container(int v) {
                    this.value = v;
                }
            }
            class Program {
                public static void Main() {
                    var shared = new Container(42);
                    owned Container x = shared;
                }
            }
            """;

        var emitted = GetAllEmittedCSharp(source);
        Assert.NotNull(emitted);
        Assert.Contains(".Clone()", emitted);
    }

    [Fact]
    public void ClonableClassSource_OwnedVarInit_EmitsCloneCall()
    {
        // Same as above but with `owned var` syntax.
        const string source = """
            owned class Widget {
                int count;
                string name;

                public Widget(int c, string n) {
                    this.count = c;
                    this.name = n;
                }
            }
            class Program {
                public static void Main() {
                    var w = new Widget(1, "test");
                    owned var x = w;
                }
            }
            """;

        var emitted = GetAllEmittedCSharp(source);
        Assert.NotNull(emitted);
        Assert.Contains(".Clone()", emitted);
    }

    [Fact]
    public void ClonableClassSource_OwnedFieldAssignment_EmitsCloneCall()
    {
        // Assigning to a field on an owned-bound instance from a non-owned clonable source.
        const string source = """
            owned class Inner {
                int data;

                public Inner(int d) {
                    this.data = d;
                }
            }
            owned class Outer {
                Inner child;

                public Outer(owned Inner c) {
                    this.child = c;
                }

                public void SetChild(Inner src) {
                    this.child = src;
                }
            }
            class Program {
                public static void Main() {
                    var inner = new Inner(10);
                    owned var outer = new Outer(new Inner(0));
                    outer.SetChild(inner);
                }
            }
            """;

        var emitted = GetAllEmittedCSharp(source);
        Assert.NotNull(emitted);
        Assert.Contains(".Clone()", emitted);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pure-struct elision: value-type source → .Clone() NOT present
    // Requirements: 14.7, 8.8
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PureStructSource_OwnedBindingInit_DoesNotEmitClone()
    {
        // A struct (value type) copied into an owned binding — .Clone() should be
        // elided because value semantics already produce independence.
        const string source = """
            struct Point {
                int x;
                int y;

                public Point(int px, int py) {
                    this.x = px;
                    this.y = py;
                }
            }
            class Program {
                public static void Main() {
                    var p = new Point(1, 2);
                    owned var q = p;
                }
            }
            """;

        var emitted = GetAllEmittedCSharp(source);
        Assert.NotNull(emitted);
        Assert.DoesNotContain(".Clone()", emitted);
    }

    [Fact]
    public void PureStructSource_OwnedFieldAssignment_DoesNotEmitClone()
    {
        // A struct assigned to a field of an owned-bound instance — no .Clone() needed.
        const string source = """
            struct Coord {
                int x;
                int y;

                public Coord(int cx, int cy) {
                    this.x = cx;
                    this.y = cy;
                }
            }
            owned class Canvas {
                Coord origin;

                public Canvas(Coord o) {
                    this.origin = o;
                }

                public void SetOrigin(Coord c) {
                    this.origin = c;
                }
            }
            class Program {
                public static void Main() {
                    var c = new Coord(5, 5);
                    owned var canvas = new Canvas(new Coord(0, 0));
                    canvas.SetOrigin(c);
                }
            }
            """;

        var emitted = GetAllEmittedCSharp(source);
        Assert.NotNull(emitted);
        Assert.DoesNotContain(".Clone()", emitted);
    }

    [Fact]
    public void OwnedStructSource_OwnedBindingInit_DoesNotEmitClone()
    {
        // An owned struct (declares the cooperation contract) still gets value-copy
        // semantics — .Clone() should not appear because structs copy by value.
        const string source = """
            owned struct Size {
                int width;
                int height;

                public Size(int w, int h) {
                    this.width = w;
                    this.height = h;
                }
            }
            class Program {
                public static void Main() {
                    var s = new Size(10, 20);
                    owned var t = s;
                }
            }
            """;

        var emitted = GetAllEmittedCSharp(source);
        Assert.NotNull(emitted);
        Assert.DoesNotContain(".Clone()", emitted);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Contrast: owned source (move) → no .Clone() regardless of type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedClassSource_Move_DoesNotEmitClone()
    {
        // Moving from an already-owned binding to another owned binding does NOT
        // clone — the source is consumed. Verify no .Clone() appears.
        const string source = """
            owned class Resource {
                int id;

                public Resource(int i) {
                    this.id = i;
                }
            }
            class Program {
                public static void Main() {
                    owned var a = new Resource(1);
                    owned var b = a;
                }
            }
            """;

        var emitted = GetAllEmittedCSharp(source);
        Assert.NotNull(emitted);
        Assert.DoesNotContain(".Clone()", emitted);
    }

    [Fact]
    public void ConstructorCall_DoesNotEmitClone()
    {
        // A constructor call into an owned binding — already yields ownership.
        // No .Clone() should appear.
        const string source = """
            owned class Item {
                int value;

                public Item(int v) {
                    this.value = v;
                }
            }
            class Program {
                public static void Main() {
                    owned var item = new Item(99);
                }
            }
            """;

        var emitted = GetAllEmittedCSharp(source);
        Assert.NotNull(emitted);
        Assert.DoesNotContain(".Clone()", emitted);
    }
}