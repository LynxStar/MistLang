using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class IntrinsicCloneArrayResultTests
{
    [Fact]
    public void AClonedIntrinsicArrayResultWrapsEachElement()
    {
        var built = new HandBuiltModel();
        var source = built.Type("String");
        source.Modifiers = new MemberModifiers { IsIntrinsic = true };
        var clone = HandBuiltModel.Class("Label");
        clone.CloneSource = source;
        clone.Assembly = built.Model.AddSelfAssembly("Probe", isLibrary: true);
        var result = new ConstructedType(built.Type("Array<,>"), [clone, built.Type("Int32")]);
        var split = new Method("Split", result, [new Parameter("separator", built.Type("Char"))],
            new MemberModifiers { IsIntrinsic = true })
        { DeclaringType = clone };
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        IntrinsicCloneEmitter.EmitMethod(split, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Contains("Probe.Label[] Split(char separator)", writer.ToString());
        Assert.Contains("System.Array.ConvertAll(Value.Split(separator), item => new Probe.Label(item))", writer.ToString());
    }

    [Fact]
    public void AnArrayOfAnotherTypeRetainsItsBackingElements()
    {
        var built = new HandBuiltModel();
        var source = built.Type("String");
        source.Modifiers = new MemberModifiers { IsIntrinsic = true };
        var clone = HandBuiltModel.Class("Label");
        clone.CloneSource = source;
        clone.Assembly = built.Model.AddSelfAssembly("Probe", isLibrary: true);
        var result = new ConstructedType(built.Type("Array<,>"), [built.Type("Char"), built.Type("Int32")]);
        var method = new Method("ToCharArray", result, [], new MemberModifiers { IsIntrinsic = true })
        { DeclaringType = clone };
        var writer = new CSharpWriter();

        IntrinsicCloneEmitter.EmitMethod(method, writer, new DiagnosticBag());

        Assert.Contains("=> Value.ToCharArray();", writer.ToString());
        Assert.DoesNotContain("ConvertAll", writer.ToString());
    }
}