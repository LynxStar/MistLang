// Tier 3: metadata-only CLR identity retains builtin length contracts and excludes lookalikes.

using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.ProgramModel.Model.Members;

namespace Mist.Tests.Backend;

public sealed class ClrSequenceLengthTests
{
    [Fact]
    public void ReferencePackDeclarations_RetainSupportedLengthContracts()
    {
        var diagnostics = new DiagnosticBag();
        var referenceDir = SdkDiscovery.FindReferenceAssemblyPath("net10.0", diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.NotNull(referenceDir);
        using var context = new MetadataLoadContext(new PathAssemblyResolver(Directory.GetFiles(referenceDir, "*.dll")));
        var rows = new (string Assembly, Type Host, string Name, int Count, SequenceLengthOperation Operation)[]
        {
            ("System.Runtime", typeof(Span<>), "Slice", 1, SequenceLengthOperation.Slice),
            ("System.Runtime", typeof(ReadOnlySpan<>), "ToArray", 0, SequenceLengthOperation.Preserved),
            ("System.Runtime", typeof(string), "ToCharArray", 0, SequenceLengthOperation.Preserved),
            ("System.Linq", typeof(Enumerable), "ToArray", 1, SequenceLengthOperation.Preserved),
        };

        foreach(var (assembly, host, name, count, operation) in rows)
        {
            var metadataHost = context.LoadFromAssemblyName(assembly).GetType(host.FullName!)!;
            var methods = metadataHost.GetMethods().Where(method => method.Name == name && method.GetParameters().Length == count).ToArray();
            Assert.NotEmpty(methods);
            Assert.All(methods, method => Assert.True(operation == ClrSequenceLengths.Of(method), metadataHost.AssemblyQualifiedName));
        }

        var extensions = context.LoadFromAssemblyName("System.Memory").GetType(typeof(MemoryExtensions).FullName!)!;
        var asSpan = extensions.GetMethods().Where(method => method.Name == "AsSpan" && method.GetParameters().Length == 1).ToArray();
        Assert.NotEmpty(asSpan);
        foreach(var method in asSpan)
        {
            var source = method.GetParameters()[0].ParameterType;
            var expected = source.IsSZArray || source.FullName == typeof(string).FullName
                ? SequenceLengthOperation.Preserved : SequenceLengthOperation.None;
            Assert.True(expected == ClrSequenceLengths.Of(method),
                $"{method}: {extensions.Assembly.FullName}; runtime: {typeof(MemoryExtensions).Assembly.FullName}");
        }
    }

    [Fact]
    public void RuntimeAndMetadataDeclarations_HaveTheSameSupportedContract()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        using var context = new MetadataLoadContext(new PathAssemblyResolver(Directory.GetFiles(runtimeDir, "*.dll")));
        var rows = new (Type Host, string Name, int Count, SequenceLengthOperation Operation)[]
        {
            (typeof(Span<>), "Slice", 1, SequenceLengthOperation.Slice),
            (typeof(Span<>), "Slice", 2, SequenceLengthOperation.Slice),
            (typeof(ReadOnlySpan<>), "ToArray", 0, SequenceLengthOperation.Preserved),
            (typeof(string), "ToCharArray", 0, SequenceLengthOperation.Preserved),
            (typeof(string), "ToCharArray", 2, SequenceLengthOperation.None),
            (typeof(Enumerable), "ToArray", 1, SequenceLengthOperation.Preserved),
        };

        foreach(var (host, name, count, operation) in rows)
        {
            var runtimeMethods = host.GetMethods().Where(method => method.Name == name && method.GetParameters().Length == count).ToArray();
            Assert.NotEmpty(runtimeMethods);
            var metadataHost = context.LoadFromAssemblyPath(host.Assembly.Location).GetType(host.FullName!)!;
            var metadataMethods = metadataHost.GetMethods().Where(method => method.Name == name && method.GetParameters().Length == count).ToArray();
            Assert.Equal(runtimeMethods.Length, metadataMethods.Length);
            Assert.All(runtimeMethods, method => Assert.Equal(operation, ClrSequenceLengths.Of(method)));
            Assert.All(metadataMethods, method => Assert.Equal(operation, ClrSequenceLengths.Of(method)));
        }

        var extensions = context.LoadFromAssemblyPath(typeof(MemoryExtensions).Assembly.Location).GetType(typeof(MemoryExtensions).FullName!)!;
        foreach(var method in extensions.GetMethods().Where(method => method.Name == "AsSpan" && method.GetParameters().Length == 1))
        {
            var source = method.GetParameters()[0].ParameterType;
            Assert.Equal(source.IsSZArray || source.FullName == typeof(string).FullName
                ? SequenceLengthOperation.Preserved : SequenceLengthOperation.None, ClrSequenceLengths.Of(method));
        }

        Assert.All(typeof(Lookalike).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => Assert.Equal(SequenceLengthOperation.None, ClrSequenceLengths.Of(method)));
    }

    private sealed class Lookalike
    {
        private readonly int[] values = [10];

        public int[] ToArray()
        {
            return values;
        }

        public int[] Slice(int start, int length)
        {
            return [values[0], start, length];
        }
    }
}