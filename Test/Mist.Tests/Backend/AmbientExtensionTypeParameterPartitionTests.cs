using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientExtensionTypeParameterPartitionTests(AmbientTypeProviderV2LinqInteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2LinqInteropFixture>
{
    private List<ExtensionBlock> BlocksIn(string namespaceName)
    {
        var blocks = new List<ExtensionBlock>();
        var ns = fixture.Model.GetOrAddNamespace(namespaceName);

        foreach(var byReceiver in ns.Extensions.Values)
        {
            blocks.AddRange(byReceiver);
        }

        return blocks;
    }

    private List<(ExtensionBlock Block, Method Method)> Overloads(string namespaceName, string methodName)
    {
        var found = new List<(ExtensionBlock, Method)>();

        foreach(var block in BlocksIn(namespaceName))
        {
            foreach(var method in block.MethodsList)
            {
                if(method.Name == methodName)
                {
                    found.Add((block, method));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Asserts what the receiver mentions rather than names, as
    /// <see cref="Where_PutsItsOnlyParameterOnTheBlock"/> does. Blocks are keyed by receiver type under
    /// reference equality, so overload order follows identity hash codes and a pinned name holds only
    /// by luck.
    /// </summary>
    [Fact]
    public void Select_PutsReceiverParameterOnBlockAndResultOnMethod()
    {
        var overloads = Overloads("System.Linq", "Select");
        Assert.NotEmpty(overloads);

        var split = overloads
            .Where(o => o.Block.TypeParametersList.Count == 1 && o.Method.TypeParametersList.Count == 1)
            .ToList();

        Assert.NotEmpty(split);

        Assert.All(split, o =>
        {
            var receiverMentions = new HashSet<string>(StringComparer.Ordinal);
            CollectTypeParameters(o.Block.ResolvedReceiverType!, receiverMentions);

            Assert.Contains(o.Block.TypeParametersList[0].Name, receiverMentions);
            Assert.DoesNotContain(o.Method.TypeParametersList[0].Name, receiverMentions);
        });
    }

    private static void CollectTypeParameters(IMistType type, HashSet<string> into)
    {
        switch(type)
        {
            case TypeParameter parameter:
                _ = into.Add(parameter.Name);
                break;

            case ConstructedType constructed:
                foreach(var argument in constructed.TypeArguments)
                {
                    CollectTypeParameters(argument, into);
                }

                break;
        }
    }

    /// <summary>
    /// Every <c>Where</c> overload's one type parameter is its receiver's element type, so the method
    /// always gets none. The name is the declaring class's choice, so only the placement is asserted.
    /// </summary>
    [Fact]
    public void Where_PutsItsOnlyParameterOnTheBlock()
    {
        var overloads = Overloads("System.Linq", "Where");
        Assert.NotEmpty(overloads);

        Assert.All(overloads, o =>
        {
            _ = Assert.Single(o.Block.TypeParametersList);
            Assert.Empty(o.Method.TypeParametersList);
        });
    }

    [Fact]
    public void NoTypeParameterAppearsAtBothLevels()
    {
        var blocks = BlocksIn("System.Linq");
        Assert.NotEmpty(blocks);

        foreach(var block in blocks)
        {
            var blockNames = block.TypeParametersList.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

            foreach(var method in block.MethodsList)
            {
                foreach(var methodParam in method.TypeParametersList)
                {
                    Assert.False(
                        blockNames.Contains(methodParam.Name),
                        $"System.Linq: '{methodParam.Name}' on {method.Name} is declared at both levels.");
                }
            }
        }
    }

    /// <summary>
    /// Pins the numbering ExtensionProjector.ProjectTypeParameters assigns, where the reason it is not
    /// renumbered per level is documented.
    /// </summary>
    [Fact]
    public void OrdinalsRemainTheClrIndicesAcrossBothLevels()
    {
        var overloads = Overloads("System.Linq", "Select");

        var split = overloads
            .Where(o => o.Block.TypeParametersList.Count == 1 && o.Method.TypeParametersList.Count == 1)
            .ToList();

        Assert.NotEmpty(split);

        var (block, method) = split[0];

        // Select<TSource, TResult>: TSource is CLR index 0, TResult is 1, regardless of level.
        Assert.Equal(0, block.TypeParametersList[0].Ordinal);
        Assert.Equal(1, method.TypeParametersList[0].Ordinal);
    }

    /// <summary>
    /// The method scope nests inside the block's, which is what lets a method signature mention the
    /// receiver's type parameter.
    /// </summary>
    [Fact]
    public void BlockParameterIsVisibleFromTheMethodScope()
    {
        var overloads = Overloads("System.Linq", "Select");

        var split = overloads
            .Where(o => o.Block.TypeParametersList.Count == 1 && o.Method.TypeParametersList.Count == 1)
            .ToList();

        Assert.NotEmpty(split);

        var (block, method) = split[0];
        var blockParameterName = block.TypeParametersList[0].Name;

        var result = method.Scope.Lookup(
            new LookupRequest(blockParameterName, MemberVisibility.None, null));

        var type = Assert.IsType<TypeResult>(result);
        Assert.Same(block.TypeParametersList[0], type.Type);
    }
}