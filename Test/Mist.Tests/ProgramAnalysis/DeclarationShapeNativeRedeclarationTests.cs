using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class DeclarationShapeNativeRedeclarationTests
{
    [Fact]
    public void AMemberRepeatingANonVirtualClrMember_IsARedeclaration()
    {
        var f = new NativeSlotFragments();
        var collection = NativeSlotFragments.Projected("Collection`1");
        var element = DeclarationFragments.Parameter(collection, collection.Scope, "T");
        _ = NativeSlotFragments.Slot(collection, "Add", NativeSlotKind.None, f.Void, Accessibility.Public, element);
        var add = f.Member(f.Source("Loud", f.Built.Interner.GetOrCreateConstructedType(collection, [f.Int])), "Add", f.Void, parameters: f.Int);

        var diagnostic = Assert.Single(f.Run(new DeclarationShapeAnalyzer()), each => each.Number.Value == "MIST0177");

        Assert.Contains("'Loud' cannot redeclare member 'Add' already defined in 'Collection`1'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(add.DeclarationNode!.Span.Start + 1, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData("None", true)]
    [InlineData("Defaulted", false)]
    public void APropertyRepeatingAClrProperty_IsARedeclarationUnlessItFillsTheSlot(string kindOfSlot, bool refused)
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Exception");
        _ = NativeSlotFragments.SlotProperty(clr, "Message", Enum.Parse<NativeSlotKind>(kindOfSlot), f.Text, settable: false);
        _ = f.MemberProperty(f.Source("Mine", clr), "Message", f.Text, settable: false);

        Assert.Equal(refused ? 1 : 0, f.Run(new DeclarationShapeAnalyzer()).Count(each => each.Number.Value == "MIST0177"));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void AStaticEqualsBesideAProjectedObjectOne_IsARedeclarationOnlyWhenSpelledSystemObject(bool spelledClr, int reported)
    {
        var f = new NativeSlotFragments();
        var root = new ClassType("Object", "System", new Scope(null), null, null) { Assembly = MistAssembly.Ambient };
        var boolean = f.Built.Type("Boolean");
        _ = NativeSlotFragments.Slot(root, "Equals", NativeSlotKind.Defaulted, boolean, Accessibility.Public, root);
        root.MethodsList.Add(new Method(
            "Equals", boolean, [new Parameter("objA", root), new Parameter("objB", root)],
            new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true, IsClrProjected = true })
        {
            DeclaringType = root,
            Scope = new Scope(root.Scope),
            SlotKind = NativeSlotKind.None,
        });
        IMistType parameter = spelledClr ? root : UnknownType.Instance;
        _ = f.Member(f.Source("Tagged", NativeSlotFragments.Projected("Collection", root)), "Equals", boolean, isStatic: true, parameters: [parameter, parameter]);

        Assert.Equal(reported, f.Run(new DeclarationShapeAnalyzer()).Count(each => each.Number.Value == "MIST0177"));
    }

    [Fact]
    public void RestatingAMistAncestorsFill_IsNoRedeclaration()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Hooked");
        _ = NativeSlotFragments.Slot(clr, "Hook", NativeSlotKind.Defaulted, f.Text, Accessibility.Protected, f.Int);
        var middle = f.Source("Middle", clr);
        _ = f.Member(middle, "Hook", f.Text, Accessibility.Protected, parameters: f.Int);
        _ = f.Member(f.Source("Derived", middle), "Hook", f.Text, Accessibility.Protected, parameters: f.Int);

        Assert.DoesNotContain(f.Run(new DeclarationShapeAnalyzer()), each => each.Number.Value == "MIST0177");
    }

    [Fact]
    public void ACollisionWithASealedSlot_IsLeftToTheSlotRule()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("SealingDerived");
        _ = NativeSlotFragments.Slot(clr, "Twice", NativeSlotKind.Sealed, f.Int, Accessibility.Public, f.Int);
        _ = f.Member(f.Source("Mine", clr), "Twice", f.Int, parameters: f.Int);

        Assert.DoesNotContain(f.Run(new DeclarationShapeAnalyzer()), each => each.Number.Value == "MIST0177");
    }

    [Fact]
    public void AMemberHidingTwoProjectedDeclarations_IsReportedOnce()
    {
        var f = new NativeSlotFragments();
        var root = NativeSlotFragments.Projected("Root");
        _ = NativeSlotFragments.Slot(root, "Twice", NativeSlotKind.Defaulted, f.Int, Accessibility.Public, f.Int);
        var hiding = NativeSlotFragments.Projected("Hiding", root);
        _ = NativeSlotFragments.Slot(hiding, "Twice", NativeSlotKind.None, f.Int, Accessibility.Public, f.Int);
        _ = f.Member(f.Source("Mine", hiding), "Twice", f.Int, parameters: f.Int);

        _ = Assert.Single(f.Run(new DeclarationShapeAnalyzer()), each => each.Number.Value == "MIST0177");
    }
}