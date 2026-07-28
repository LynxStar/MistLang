using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

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
public sealed class AmbientExtensionTypeParameterPartitionTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly AmbientTypeProviderV2 provider;
    private readonly MetadataLoadContext metadataContext;

    public AmbientExtensionTypeParameterPartitionTests()
    {
        programModel = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(programModel);

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        var resolver = new PathAssemblyResolver(assemblyPaths);
        metadataContext = new MetadataLoadContext(resolver);

        var loadedAssemblies = AmbientTypeProviderV2Factory.LoadAssemblies(
            assemblyPaths, metadataContext, new DiagnosticBag());

        provider = new AmbientTypeProviderV2(loadedAssemblies, null, new DiagnosticBag());
        provider.BuildAndRegister(programModel);
    }

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }

    /// <summary>
    /// Every projected extension block in a namespace, flattened across receiver types.
    /// </summary>
    private List<ExtensionBlock> BlocksIn(string namespaceName)
    {
        var blocks = new List<ExtensionBlock>();
        var ns = programModel.GetOrAddNamespace(namespaceName);

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
    /// TResult&gt;)</c>. The receiver mentions only TSource, so TSource is the block's and TResult is
    /// the method's -- one apiece, not two on either.
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

        var (block, method) = split[0];
        Assert.Equal("TSource", block.TypeParametersList[0].Name);
        Assert.Equal("TResult", method.TypeParametersList[0].Name);
    }

    /// <summary>
    /// Every <c>Where</c> overload takes its sequence's element type and returns the same sequence
    /// shape, so its single type parameter is always the receiver's and the method always gets none.
    /// The name varies by declaring class -- <c>TSource</c> on <c>Enumerable</c>, <c>T</c> on
    /// <c>ImmutableArrayExtensions</c> -- so only the placement is asserted.
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
    /// No parameter may land on both levels, for any projected extension in the BCL. This is the
    /// invariant the partition exists to hold, checked across every method rather than a chosen few.
    /// </summary>
    [Fact]
    public void NoTypeParameterAppearsAtBothLevels()
    {
        var namespaces = new[] { "System.Linq", "System", "System.Collections.Generic" };

        foreach(var namespaceName in namespaces)
        {
            foreach(var block in BlocksIn(namespaceName))
            {
                var blockNames = block.TypeParametersList.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

                foreach(var method in block.MethodsList)
                {
                    foreach(var methodParam in method.TypeParametersList)
                    {
                        Assert.False(
                            blockNames.Contains(methodParam.Name),
                            $"{namespaceName}: '{methodParam.Name}' on {method.Name} is declared at both levels.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Ordinals stay as the CLR numbered them across both levels, because
    /// <c>ConstructedType.Substitute</c> indexes a receiver's type arguments by them. Renumbering each
    /// list from zero would make a method-level parameter substitute to whatever sat in that slot.
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