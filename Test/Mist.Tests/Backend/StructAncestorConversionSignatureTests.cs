using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class StructAncestorConversionSignatureTests
{
    [Fact]
    public void GenericLeafDeclaresOneConversionToEachExactAncestor()
    {
        var shapes = new GenericDefaultShapes();
        var root = shapes.Type("Root", isStruct: true);
        _ = Parameter(root, "TRoot");

        var middle = shapes.Type("Middle", isStruct: true, baseType: Construct(root, shapes.Integer));
        _ = Parameter(middle, "TMiddle");

        var leaf = (StructType)shapes.Type("Leaf", isStruct: true);
        _ = Parameter(leaf, "TFirst");
        var second = Parameter(leaf, "TSecond");
        leaf.BaseType = Construct(middle, second);

        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        TypeEmitter.EmitTypeBody(leaf, new ConstraintClosureMap(), new VariadicUseRegistry(), writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        var signatures = writer.ToString().Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains(" implicit operator ", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        Assert.Equal(
            [
                "public static implicit operator Probe.Middle<TSecond>(Leaf<TFirst, TSecond> value)",
                "public static implicit operator Probe.Root<int>(Leaf<TFirst, TSecond> value)",
            ],
            signatures);
    }
}