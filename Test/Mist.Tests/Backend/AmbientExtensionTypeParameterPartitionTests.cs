using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Verifies that projecting a CLR extension method splits its type parameters between the extension
/// block and the method, as Mist models them.
/// <para>
/// The CLR has no extension blocks. <c>Select&lt;TSource, TResult&gt;(this IEnumerable&lt;TSource&gt;
/// source, ...)</c> is a static method with one flat parameter list, and resugaring it has to decide
/// which parameters were the block's. The receiver type is the evidence: a parameter it mentions was
/// declared on the block, and the rest on the method.
/// </para>
/// </summary>
[Trait("Tier", "2")]
public sealed class AmbientExtensionTypeParameterPartitionTests(AmbientTypeProviderV2LinqInteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2LinqInteropFixture>
{
    /// <summary>
    /// Every projected extension block in a namespace, flattened across receiver types.
    /// </summary>
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

    /// <summary>
    /// The projected blocks holding a method of the given name and remaining-parameter count. One
    /// block per CLR method, so a name with overloads yields several.
    /// </summary>
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

    // ═══════════════════════════════════════════════════════════════════════
    // The receiver decides
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>Select&lt;TSource, TResult&gt;(this IEnumerable&lt;TSource&gt;, Func&lt;TSource,
    /// TResult&gt;)</c>. The receiver mentions one of the two parameters, so that one is the block's
    /// and the result is the method's -- one apiece, not two on either.
    /// <para>
    /// What the receiver MENTIONS is asserted rather than the parameter's name, for the same reason
    /// <see cref="Where_PutsItsOnlyParameterOnTheBlock"/> asserts only placement: the name is the
    /// declaring class's choice, not the partition's. Blocks are held in a dictionary keyed by
    /// receiver type under reference equality, so which overload is reached first follows identity
    /// hash codes and a name pinned to one of them holds only by luck of that order.
    /// </para>
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

    /// <summary>
    /// Every type parameter named anywhere in a type expression, which is the evidence the partition
    /// is built from.
    /// </summary>
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
    /// Every <c>Where</c> overload takes its sequence's element type and returns the same sequence
    /// shape, so its single type parameter is always the receiver's and the method always gets none.
    /// The name is the declaring class's choice, so only the placement is asserted.
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

    /// <summary>
    /// No parameter may land on both levels, for any extension System.Linq projects. This is the
    /// invariant the partition exists to hold, checked across every method rather than a chosen few.
    /// </summary>
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
    /// A block-level parameter is reachable by name from the method's scope, since the method scope is
    /// nested inside the block's. This is what lets a method signature mention the receiver's
    /// parameter, as <c>Func&lt;TSource, TResult&gt;</c> does.
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