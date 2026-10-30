using System.Security.Cryptography;
using System.Text;

namespace Mist.Driver.Generators;

public static class GeneratorKeys
{
    public static string Toolchain(IEnumerable<ContentHash> compiler, IEnumerable<ContentHash> standard,
        IEnumerable<ContentHash> referenceAssemblies, string runtimeVersion)
    {
        return Hash([Flatten(compiler), Flatten(standard), Flatten(referenceAssemblies), [runtimeVersion]]);
    }

    public static string Compile(string toolchain, IEnumerable<ContentHash> manifestAndSources,
        IEnumerable<ContentHash> references, string hostRuntimeIdentifier)
    {
        return Hash([[toolchain], Flatten(manifestAndSources), Flatten(references), [hostRuntimeIdentifier]]);
    }

    public static string Run(string toolchain, string generator, string consumerName, string consumerManifest,
        IEnumerable<string> arguments, IEnumerable<ContentHash> inputs, bool seedTool = false)
    {
        return Hash([[toolchain], [generator], [consumerName, consumerManifest], arguments, Flatten(inputs), [seedTool ? "seed" : "generator"]]);
    }

    private static IEnumerable<string> Flatten(IEnumerable<ContentHash> values)
    {
        return values.SelectMany(value => new[] { value.Path, value.Sha256 });
    }

    private static string Hash(IEnumerable<IEnumerable<string>> sections)
    {
        using var stream = new MemoryStream();
        using(var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach(var section in sections)
            {
                var values = section.ToArray();
                writer.Write(values.Length);
                foreach(var value in values)
                {
                    writer.Write(value);
                }
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
}