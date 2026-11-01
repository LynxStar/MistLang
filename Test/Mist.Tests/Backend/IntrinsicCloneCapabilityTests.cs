using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class IntrinsicCloneCapabilityTests
{
    [Fact]
    public void AClonedBoundIsInitializedFromTheBackingField()
    {
        var (_, clone) = Types();
        var field = new Field("MaxValue", clone, OwnershipKind.Unrestricted,
            new MemberModifiers { IsStatic = true, IsIntrinsic = true, IsReadonly = true, Accessibility = Accessibility.Public })
        { DeclaringType = clone };
        var writer = new CSharpWriter();
        FieldEmitter.Emit(field, inReadonlyType: true, writer, new DiagnosticBag());

        Assert.Equal("public static readonly Count MaxValue = new Count(int.MaxValue);", writer.ToString().Trim());
    }

    [Fact]
    public void AClonedParserWrapsOnlyAPresentResultAndCallsTheAdapterOnce()
    {
        var (source, clone) = Types();
        var built = new HandBuiltModel();
        var text = new UnionType([built.Type("String"), built.Type("Void")]);
        var node = HandBuiltModel.Node();
        var modifiers = new MemberModifiers { IsStatic = true, IsIntrinsic = true, Accessibility = Accessibility.Public };
        source.MethodsList.Add(new Method("Parse", new UnionType([source, built.Type("Void")]), [new Parameter("text", text)], modifiers)
        { DeclaringType = source, DeclarationNode = node });
        var method = new Method("Parse", new UnionType([clone, built.Type("Void")]), [new Parameter("text", text)], modifiers)
        { DeclaringType = clone, DeclarationNode = node };
        var writer = new CSharpWriter();
        IntrinsicCloneEmitter.EmitMethod(method, writer, new DiagnosticBag());
        var emitted = writer.ToString();

        Assert.Contains("public static Count? Parse(string? text)", emitted);
        Assert.Contains(" is { } ", emitted);
        Assert.Contains("? new Count(", emitted);
        Assert.Contains(": (Count?)null", emitted);
        Assert.Equal(1, emitted.Split("NumericParsing.ParseInt32(text)").Length - 1);
    }

    private static (StructType Source, StructType Clone) Types()
    {
        var source = new StructType("Int32", "Standard", null, null, null)
        { Modifiers = new MemberModifiers { IsIntrinsic = true } };
        var clone = HandBuiltModel.Struct("Count");
        clone.CloneSource = source;
        return (source, clone);
    }
}