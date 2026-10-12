using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class RefFieldEmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARefFieldIsNeverEmittedAsItsValueType(bool isStatic)
    {
        var field = Field(new MemberModifiers { Accessibility = Accessibility.Public, IsRef = true, IsStatic = isStatic });

        var refusal = Assert.Throws<InvalidOperationException>(() => FieldEmitter.Emit(field, false, new CSharpWriter(), new DiagnosticBag()));

        Assert.Contains("ref field 'Value'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldDeclaredByValueIsEmitted()
    {
        var writer = new CSharpWriter();

        FieldEmitter.Emit(Field(new MemberModifiers { Accessibility = Accessibility.Public }), false, writer, new DiagnosticBag());

        Assert.EndsWith("Widget Value;", writer.ToString().TrimEnd(), StringComparison.Ordinal);
    }

    private static Field Field(MemberModifiers modifiers)
    {
        var holder = HandBuiltModel.Class("Holder");
        return new Field("Value", HandBuiltModel.Struct("Widget"), OwnershipKind.Unrestricted, modifiers) { DeclaringType = holder };
    }
}