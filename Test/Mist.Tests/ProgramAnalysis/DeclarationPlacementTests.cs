using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class DeclarationPlacementTests
{
    private static MemberModifiers Static => DeclarationFragments.Public with { IsStatic = true };

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bodiless-method", "method", "Make")]
    [InlineData("bodied-method", "method", "Make")]
    [InlineData("property", "property", "Count")]
    [InlineData("indexer", "indexer", "this[]")]
    public void AStaticMemberOfAnAbstractIsRefused(string member, string kind, string name)
    {
        var fragment = new DeclarationFragments();
        var abstraction = fragment.Abstract("IMake");
        Declare(fragment, abstraction, member, Static);

        var refused = Assert.Single(DeclarationPlacementAnalyzer.StaticMembers(abstraction));

        Assert.Equal(kind, refused.Kind);
        Assert.Equal(name, refused.Name);
        Assert.NotNull(refused.Declaration);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bodiless-method")]
    [InlineData("bodied-method")]
    [InlineData("property")]
    [InlineData("indexer")]
    public void AnInstanceMemberOfAnAbstractIsAdmitted(string member)
    {
        var fragment = new DeclarationFragments();
        var abstraction = fragment.Abstract("IMake");
        Declare(fragment, abstraction, member, DeclarationFragments.Public);

        Assert.Empty(DeclarationPlacementAnalyzer.StaticMembers(abstraction));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AStaticMemberOfAProjectedClrInterfaceIsAdmitted()
    {
        var fragment = new DeclarationFragments();
        var projected = fragment.Abstract("IParsable");
        projected.Modifiers = projected.Modifiers with { IsClrProjected = true };
        Declare(fragment, projected, "bodiless-method", Static);

        Assert.Empty(DeclarationPlacementAnalyzer.StaticMembers(projected));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void EachStaticMemberOfAnAbstractIsReportedOnceAtItsDeclaration()
    {
        var fragment = new DeclarationFragments();
        var abstraction = fragment.Abstract("IMake");
        Declare(fragment, abstraction, "bodied-method", Static, offset: 30);
        Declare(fragment, abstraction, "property", Static, offset: 50);
        Declare(fragment, abstraction, "bodiless-method", DeclarationFragments.Public, offset: 70);

        var diagnostics = fragment.Run(new DeclarationPlacementAnalyzer());

        Assert.Equal(["MIST_PM_STATIC_ON_ABSTRACT_MEMBER", "MIST_PM_STATIC_ON_ABSTRACT_MEMBER"], diagnostics.Select(d => d.Id.Value));
        Assert.Equal([31, 51], diagnostics.Select(d => d.Location.Start.Column).Order());
        Assert.Contains(diagnostics, d => d.Message.Contains("method 'Make' of abstract type 'IMake'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Message.Contains("property 'Count' of abstract type 'IMake'", StringComparison.Ordinal));
    }

    private static void Declare(DeclarationFragments fragment, AbstractType owner, string member, MemberModifiers modifiers, int offset = 20)
    {
        var integer = fragment.Built.Type("Int32");

        switch(member)
        {
            case "bodiless-method":
            case "bodied-method":
                owner.MethodsList.Add(new Method("Make", integer, [], modifiers)
                {
                    DeclaringType = owner,
                    DeclarationNode = HandBuiltModel.Node(offset),
                    Body = member == "bodied-method" ? DeclarationFragments.Returning(HandBuiltModel.Literal(integer, 5L)) : null,
                });
                break;

            case "property":
                owner.PropertiesList.Add(new Property("Count", integer, modifiers)
                {
                    DeclaringType = owner,
                    DeclarationNode = HandBuiltModel.Node(offset),
                    Getter = new PropertyAccessor(),
                });
                break;

            default:
                owner.IndexersList.Add(new Indexer(integer, [new Parameter("i", integer)], modifiers)
                {
                    DeclaringType = owner,
                    DeclarationNode = HandBuiltModel.Node(offset),
                });
                break;
        }
    }
}