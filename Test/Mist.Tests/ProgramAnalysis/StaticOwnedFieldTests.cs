using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class StaticOwnedFieldTests
{
    [Theory]
    [InlineData("static owned field", true)]
    [InlineData("static field with the owned modifier", true)]
    [InlineData("static field", false)]
    [InlineData("owned instance field", false)]
    [InlineData("static field of an owned type", false)]
    public void OnlyAStaticFieldItselfMarkedOwnedIsRefused(string shape, bool refused)
    {
        var fragment = new DeclarationFragments();

        Assert.Equal(refused, DisciplineAnalyzer.StaticOwned(Declare(fragment, shape)));
    }

    [Fact]
    public void AStaticOwnedFieldIsRefusedAtItsDeclaration()
    {
        var fragment = new DeclarationFragments();
        _ = Declare(fragment, "static owned field");

        var diagnostic = Assert.Single(fragment.Run(new DisciplineAnalyzer()));

        Assert.Equal("MIST0510", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains("Static field 'S' of 'C' cannot be marked 'owned': ownership semantics require an instance-level owner", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("static field")]
    [InlineData("owned instance field")]
    public void APlainStaticFieldAndAnOwnedInstanceFieldAreAdmitted(string shape)
    {
        var fragment = new DeclarationFragments();
        _ = Declare(fragment, shape);

        Assert.Empty(fragment.Run(new DisciplineAnalyzer()));
    }

    private static Field Declare(DeclarationFragments fragment, string shape)
    {
        var box = fragment.Class("Box");
        var declaring = fragment.Class("C");
        var (ownership, modifiers) = shape switch
        {
            "static owned field" => (OwnershipKind.Owned, new MemberModifiers { IsStatic = true }),
            "static field with the owned modifier" => (OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = true, IsOwned = true }),
            "owned instance field" => (OwnershipKind.Owned, new MemberModifiers()),
            _ => (OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = true }),
        };

        if(shape == "static field of an owned type")
        {
            declaring.Modifiers = DeclarationFragments.Public with { IsOwned = true };
        }

        var field = new Field("S", box, ownership, modifiers) { DeclaringType = declaring, DeclarationNode = HandBuiltModel.Node(37) };
        declaring.FieldsList.Add(field);
        return field;
    }
}