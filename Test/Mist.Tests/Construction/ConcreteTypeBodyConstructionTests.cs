using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class ConcreteTypeBodyConstructionTests
{
    private readonly BindingFragments f = new();

    [Fact]
    public void ASlotInAClassIsRefusedAndTheMembersAroundItAreStillBuilt()
    {
        var holder = Build(Field("before", 10), SlotMember(30), Field("after", 50));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0336", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(31, diagnostic.Position().Start.Column);
        Assert.Equal(["before", "after"], holder.FieldsList.Select(field => field.Name));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryDefaultBlockAfterTheFirstIsRefusedAndTheFirstIsKept(int count)
    {
        var blocks = Enumerable.Range(0, count).Select(i => DefaultBlock(10 + i * 20, $"f{i}")).ToArray();

        var holder = Build([.. blocks.Select(block => new DefaultBlockMemberMemberDeclarationNode { DefaultBlock = block })]);

        var refusals = f.Diagnostics.ToImmutableArray();
        Assert.Equal(Enumerable.Repeat("MIST0337", count - 1), refusals.Select(diagnostic => diagnostic.Number.Value));
        Assert.Equal(Enumerable.Range(1, count - 1).Select(i => 11 + i * 20), refusals.Select(diagnostic => diagnostic.Position().Start.Column));
        Assert.All(refusals, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("'Holder'", diagnostic.Message);
        });
        Assert.Same(blocks[0], holder.DefaultBlock!.DeclarationNode);
        Assert.Equal("f0", Assert.Single(holder.DefaultBlock.Entries).FieldName);
    }

    [Fact]
    public void OneDefaultBlockIsTheTypesDefaultBlock()
    {
        var block = DefaultBlock(10, "count");

        var holder = Build(Field("count", 30), new DefaultBlockMemberMemberDeclarationNode { DefaultBlock = block });

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(block, holder.DefaultBlock!.DeclarationNode);
        Assert.Equal("count", Assert.Single(holder.DefaultBlock.Entries).FieldName);
    }

    private ClassType Build(params MemberDeclarationNode[] members)
    {
        var body = BindingFragments.At<TypeBodyDeclarationNode>(0);
        body.Name = BindingFragments.Token("Holder");
        body.Keyword = new ClassKeywordTypeKeywordNode();
        body.Members = [.. members];

        var holder = new ClassType("Holder", "Probe", f.Types, null, new BodyTypeTypeDeclarationNode { TypeBody = body });
        new ConcreteTypeBuilder(f.Builder, f.Builder.Resolver).Build(holder);
        return holder;
    }

    private static FieldMemberMemberDeclarationNode Field(string name, int offset)
    {
        var field = BindingFragments.At<FieldDeclarationNode>(offset);
        field.Type = BindingFragments.TypeName("Int32");
        field.Name = BindingFragments.Token(name);
        return new FieldMemberMemberDeclarationNode { Field = field };
    }

    private static AssociatedTypeMemberMemberDeclarationNode SlotMember(int offset)
    {
        var slot = BindingFragments.At<AssociatedTypeSlotDeclarationNode>(offset);
        slot.Name = BindingFragments.Token("Item");
        var location = BindingFragments.At<AssociatedTypeMemberMemberDeclarationNode>(offset);
        location.AssociatedType = slot;
        return location;
    }

    private static DefaultBlockDeclarationNode DefaultBlock(int offset, string field)
    {
        var block = BindingFragments.At<DefaultBlockDeclarationNode>(offset);
        block.Entries = [new DefaultFieldOverrideDefaultFieldInitializerNode
        {
            FieldName = BindingFragments.Token(field),
            Value = BindingFragments.Expression(BindingFragments.Identifier("zero")),
        }];
        return block;
    }
}