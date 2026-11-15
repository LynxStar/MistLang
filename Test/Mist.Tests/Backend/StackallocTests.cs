// Tier 3 (Component): built over the REAL Standard sources because the spans `stackalloc T[n]` produces
// are Standard's own length-indexed declarations and their one-argument aliases.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class StackallocTests
{
    [Fact]
    public void AStackallocProducesASpanOfItsElementType()
    {
        Assert.Equal("Standard.Span<,>", DefinitionOf("Span<int> b = stackalloc int[4];"));
    }

    /// <summary>
    /// The syntax says nothing about mutability, so only the BINDING can reach the readonly span.
    /// </summary>
    [Fact]
    public void AReadonlySpanBindingProducesTheReadonlySpan()
    {
        Assert.Equal("Standard.ReadOnlySpan<,>", DefinitionOf("ReadOnlySpan<int> r = stackalloc int[3];"));
    }

    /// <summary>
    /// With no target to choose from, the mutable span is the answer that closes off least -- it
    /// converts to the readonly one where the reverse does not hold.
    /// </summary>
    [Fact]
    public void WithNoTargetTypeTheMutableSpanIsChosen()
    {
        Assert.Equal("Standard.Span<,>", DefinitionOf("var b = stackalloc int[4];"));
    }

    [Fact]
    public void AUserStructIsALegalElementType()
    {
        Assert.Equal(
            "Standard.Span<,>",
            DefinitionOf("Span<Point> p = stackalloc Point[2];", "public struct Point { public int x; }"));
    }

    /// <summary>
    /// A heap array and a stack buffer print the same values, so only the emitted keyword tells them apart.
    /// </summary>
    [Fact]
    public void TheStackAllocationIsEmittedAsOne()
    {
        var emitted = Emit("Span<int> b = stackalloc int[4];");

        Assert.Contains("stackalloc int[4]", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("new int[4]", emitted, StringComparison.Ordinal);
    }

    private static string DefinitionOf(string statement, string extraTypes = "")
    {
        var allocation = Assert.IsType<StackallocExpression>(Initializer(statement, extraTypes));
        var constructed = Assert.IsType<ConstructedType>(allocation.Type);

        return constructed.Definition.FullyQualifiedName;
    }

    private static Expression Initializer(string statement, string extraTypes)
    {
        var model = StandardModelProbe.Build(statement, out var diagnostics, extraTypes);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var body = probe.MethodsList.Single(method => method.Name == "Run").Body;

        Assert.NotNull(body);

        var declaration = body.Statements.OfType<LocalDeclarationStatement>().Last();
        Assert.NotNull(declaration.Initializer);

        return declaration.Initializer;
    }

    private static string Emit(string statement)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitExpression(Initializer(statement, ""), writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}