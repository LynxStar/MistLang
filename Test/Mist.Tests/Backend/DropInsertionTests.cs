// Tier 3 (Component): which bindings a scope drops on the way out, and which it must not. Asserted on
// the emitted TEXT because an omitted drop and an invented one are both invisible in shape -- only the
// call the scope makes says which happened. No Roslyn and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class DropInsertionTests : ProgramModelProbe
{
    public DropInsertionTests()
        : base("MistDropInsertion_")
    {
    }

    protected override string Fixtures { get; } = """
        public linear class Handle
        {
            public int Fd;
            public void Drop() { }
        }

        public affine class Token
        {
            public int Id;
            public void Drop() { }
        }

        public class Plain
        {
            public int Value;
            public void Drop() { }
        }

        public linear class Box
        {
            public Handle Inner;
            public Handle Drop() { return Inner; }
        }

        public linear class Logged
        {
            public Handle Inner;
            public void Drop() { }
        }

        public linear class Pair
        {
            public Handle First;
            public Handle Second;
        }

        public class Sink
        {
            public static void Consume(Handle h) { }

            public static void Borrow(readonly ref Handle h) { }
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void ALinearBindingDropsAtScopeExit()
    {
        Assert.Contains("h.Drop();", Run("var h = new Handle();"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Affine allows forgetting, so a drop here would be a call the author declined to make.
    /// </summary>
    [Fact]
    public void AnAffineBindingDoesNot()
    {
        Assert.DoesNotContain("t.Drop();", Run("var t = new Token();"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnrestrictedBindingDoesNot()
    {
        Assert.DoesNotContain("p.Drop();", Run("var p = new Plain();"), StringComparison.Ordinal);
    }

    [Fact]
    public void HandingTheValueToACallElidesTheDrop()
    {
        Assert.DoesNotContain(
            "h.Drop();",
            Run("var h = new Handle();\nSink.Consume(h);"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Lending is a loan, so the caller still owes the drop. Reading a member off the value is not a
    /// move either -- counting references rather than positions would elide both.
    /// </summary>
    [Fact]
    public void LendingAndReadingDoNot()
    {
        Assert.Contains(
            "h.Drop();",
            Run("var h = new Handle();\nSink.Borrow(ref h);"),
            StringComparison.Ordinal);

        Assert.Contains(
            "h.Drop();",
            Run("var h = new Handle();\nint fd = h.Fd;"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The nesting is what orders them: each drop sits inside the try of every binding declared before
    /// it, so the last declared is the first released.
    /// </summary>
    [Fact]
    public void DropsFireInReverseDeclarationOrder()
    {
        var emitted = Run("var first = new Handle();\nvar second = new Handle();");

        Assert.True(
            emitted.IndexOf("second.Drop();", StringComparison.Ordinal)
                < emitted.IndexOf("first.Drop();", StringComparison.Ordinal),
            $"second should be released first:\n{emitted}");
    }

    /// <summary>
    /// A drop that hands back what it held passes the obligation on with the value.
    /// </summary>
    [Fact]
    public void ADropReturningADroppableValueChains()
    {
        var emitted = Run("var b = new Box();");
        Assert.True(
            emitted.Contains("(b.Drop()).Drop();", StringComparison.Ordinal),
            $"expected a chained drop, got:\n{emitted}");
    }

    /// <summary>
    /// A container under an obligation cannot honour it without releasing what it holds, and writing no
    /// Drop is not declining -- there is nothing to write that the fields do not already say.
    /// </summary>
    [Fact]
    public void AContainerWithDroppableFieldsGetsASynthesizedDrop()
    {
        var emitted = EmitType("Pair");

        Assert.Contains("public void Drop()", emitted, StringComparison.Ordinal);
        Assert.Contains("this.First.Drop();", emitted, StringComparison.Ordinal);
        Assert.Contains("this.Second.Drop();", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Writing a Drop does not discharge the obligation: what the author wrote happens IN ADDITION to
    /// releasing what the type holds.
    /// </summary>
    [Fact]
    public void ADeclaredDropStillOwesItsFields()
    {
        Assert.Contains("this.Inner.Drop();", EmitType("Logged"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A field the declared drop hands back is moved, so it is not also released.
    /// </summary>
    [Fact]
    public void ADeclaredDropReturningItsFieldOwesNothing()
    {
        Assert.DoesNotContain("this.Inner.Drop();", EmitType("Box"), StringComparison.Ordinal);
    }

    private string Run(string statements)
    {
        var body = BodyIn(statements);
        var writer = new CSharpWriter();

        BodyEmitter.EmitBlock(body, writer, new DiagnosticBag());

        return writer.ToString();
    }

    private string EmitType(string typeName)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(
            type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);

        return writer.ToString();
    }
}