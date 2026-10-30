using System.Text;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Generators;

internal static class GeneratorSeeds
{
    public static CachedGeneration? Prepare(GeneratorNode consumer, IGeneratorFiles files, DiagnosticBag diagnostics)
    {
        if(consumer.Configuration.SeedSources is not { } declaration)
        {
            return null;
        }

        try
        {
            if(!files.Exists(Path.Combine(declaration.Path, SourcePathSelection.GeneratedMarker)))
            {
                return invalid("captured output directory must contain a .mist-generated marker");
            }

            if(!files.Exists(declaration.Provenance)
                || GeneratorProvenance.Deserialize(Encoding.UTF8.GetString(files.Read(declaration.Provenance))) is not { } record
                || record.Outputs.IsDefaultOrEmpty)
            {
                return invalid();
            }

            var actual = files.Files(declaration.Path)
                .Where(path => path.EndsWith(".mist", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(declaration.Path, path)).ToArray();
            if(OutputListValidation.Validate(record.Outputs.Select(output => output.Path), actual, []) is not null)
            {
                return invalid();
            }

            foreach(var output in record.Outputs)
            {
                var path = Path.Combine(declaration.Path, OutputListValidation.Normalize(output.Path));
                if(!files.Exists(path) || GeneratorProvenance.Hash(files.Read(path)) != output.Sha256)
                {
                    return invalid();
                }
            }

            diagnostics.ReportTyped(DriverDiagnostics.GeneratorSeedUsed, new ProjectLocation(consumer.Manifest, null), new PathValue(declaration.Path));
            return diagnostics.HasErrors ? null : new CachedGeneration(declaration.Path, record);
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return invalid();
        }

        CachedGeneration? invalid(string reason = "provenance does not match the captured output set")
        {
            diagnostics.ReportTyped(DriverDiagnostics.GeneratorReferenceInvalid, new ProjectLocation(consumer.Manifest, null),
                new NameValue("seed_sources"), new TextValue(reason));
            return null;
        }
    }
}