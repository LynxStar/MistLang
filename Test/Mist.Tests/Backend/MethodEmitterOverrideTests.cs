using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class MethodEmitterOverrideTests
{
    private readonly NativeSlotFragments f = new();

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Protected", false, false, "protected override ")]
    [InlineData("Public", true, false, "public sealed override ")]
    [InlineData("Public", false, true, "public ")]
    public void AFill_IsSpelledOverrideAfterItsAccessibility(string access, bool isSealed, bool notAFill, string expected)
    {
        var modifiers = new MemberModifiers { Accessibility = Enum.Parse<Accessibility>(access), IsSealed = isSealed };

        Assert.Equal(expected, MemberModifierEmitter.Emit(modifiers, emitReadonly: false, fills: !notAFill));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AMethodFillingADefaultedSlot_IsEmittedOverride()
    {
        var clr = NativeSlotFragments.Projected("Hooked");
        _ = NativeSlotFragments.Slot(clr, "Hook", NativeSlotKind.Defaulted, f.Void, Accessibility.Protected);
        var hook = f.Member(f.Source("Mine", clr), "Hook", f.Void, Accessibility.Protected);

        Assert.StartsWith("protected override void Hook()", Emit(hook), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ASealedMistFill_IsEmittedSealedOverride()
    {
        var clr = NativeSlotFragments.Projected("Doubling");
        _ = NativeSlotFragments.Slot(clr, "Twice", NativeSlotKind.Defaulted, f.Int, Accessibility.Public, f.Int);
        var source = f.Source("Mine", clr);
        var twice = new Method("Twice", f.Int, [new Parameter("x", f.Int)], new MemberModifiers { Accessibility = Accessibility.Public, IsSealed = true })
        {
            DeclarationNode = HandBuiltModel.Node(90),
            DeclaringType = source,
            Scope = new Scope(source.Scope),
        };
        source.MethodsList.Add(twice);

        Assert.StartsWith("public sealed override int Twice(int x)", Emit(twice), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AToStringOutsideAnyFill_KeepsNew_AndOneFillingTheSlotIsOverrideWithoutNew()
    {
        var clr = NativeSlotFragments.Projected("Object");
        _ = NativeSlotFragments.Slot(clr, "ToString", NativeSlotKind.Defaulted, f.Text);
        var filling = f.Member(f.Source("Filled", clr), "ToString", f.Text);
        var plain = f.Member(f.Source("Plain", null), "ToString", f.Text);

        Assert.StartsWith("public override string ToString()", Emit(filling), StringComparison.Ordinal);
        Assert.StartsWith("public new string ToString()", Emit(plain), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AFillsParameter_IsNullableWhereTheSlotsParameterIs()
    {
        var clr = NativeSlotFragments.Projected("Object");
        var slot = NativeSlotFragments.Slot(clr, "Equals", NativeSlotKind.Defaulted, f.Built.Type("Boolean"));
        slot.ParametersList.Add(new Parameter("obj", f.Text) { ClrDeclaresNullable = true });
        var source = f.Source("Mine", clr);
        var equals = f.Member(source, "Equals", f.Built.Type("Boolean"), parameters: f.Text);

        using(NativeSlotNullability.Enter([source], f.Built.Interner))
        {
            Assert.StartsWith("public override bool Equals(string? p0)", Emit(equals), StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Tier", "2")]
    public void APropertyFill_IsEmittedOverride()
    {
        var clr = NativeSlotFragments.Projected("Labeled");
        _ = NativeSlotFragments.SlotProperty(clr, "Label", NativeSlotKind.Defaulted, f.Text, settable: false);
        var label = f.MemberProperty(f.Source("Mine", clr), "Label", f.Text, settable: false);
        var writer = new CSharpWriter();

        using(NativeSlotFills.Enter(f.Types.OfType<ConcreteType>(), f.Built.Interner))
        {
            PropertyEmitter.Emit(label, emitReadonly: false, readonlyGetter: false, writer, new DiagnosticBag());
        }

        Assert.Contains("public override string Label", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AnIndexerFill_IsEmittedOverride()
    {
        var clr = NativeSlotFragments.Projected("Indexed");
        clr.IndexersList.Add(new Indexer(f.Int, [new Parameter("index", f.Int)], new MemberModifiers { Accessibility = Accessibility.Public, IsClrProjected = true })
        {
            DeclaringType = clr,
            SlotKind = NativeSlotKind.Defaulted,
            Getter = new PropertyAccessor { Accessibility = Accessibility.Public },
        });
        var source = f.Source("Mine", clr);
        var indexer = new Indexer(f.Int, [new Parameter("index", f.Int)], DeclarationFragments.Public)
        {
            DeclarationNode = HandBuiltModel.Node(90),
            DeclaringType = source,
            Getter = new PropertyAccessor { Accessibility = Accessibility.Public },
        };
        source.IndexersList.Add(indexer);
        var writer = new CSharpWriter();

        using(NativeSlotFills.Enter(f.Types.OfType<ConcreteType>(), f.Built.Interner))
        {
            IndexerEmitter.Emit(indexer, emitReadonly: false, readonlyGetter: false, writer, new DiagnosticBag());
        }

        Assert.Contains("public override int this[int index]", writer.ToString(), StringComparison.Ordinal);
    }

    private string Emit(Method method)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        using(NativeSlotFills.Enter(f.Types.OfType<ConcreteType>(), f.Built.Interner))
        {
            MethodEmitter.Emit(method, emitReadonly: false, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Trim();
    }
}