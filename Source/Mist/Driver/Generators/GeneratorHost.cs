using System.Collections.Immutable;
using System.Text;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Generators;

internal sealed class GeneratorHost(
    IGeneratorCache cache,
    IGeneratorFiles files,
    Func<DotnetInvocation, DotnetOutcome> run,
    Func<GeneratorRequest, string, DiagnosticBag, string?> compile)
{
    public CachedGeneration? Prepare(GeneratorRequest request, DiagnosticBag diagnostics)
    {
        var reference = request.Reference;
        var manifest = new ProjectLocation(request.ConsumerManifest, null);
        var snapshots = new List<byte[]>();
        foreach(var input in reference.Inputs)
        {
            if(!files.Exists(input))
            {
                diagnostics.ReportTyped(DriverDiagnostics.GeneratorInputNotFound, manifest, new PathValue(input));
                return null;
            }
        }

        try
        {
            foreach(var input in reference.Inputs)
            {
                var content = files.Read(input);
                if(!request.Inputs.Any(hash => hash.Path == input && hash.Sha256 == GeneratorProvenance.Hash(content)))
                {
                    diagnostics.ReportTyped(DriverDiagnostics.GeneratorRunFailed, manifest, new NameValue(reference.Name),
                        new TextValue("declared input changed during preparation"));
                    return null;
                }

                snapshots.Add(content);
            }

            var compiled = cache.Use("compiled", request.CompileKey, reference.TimeLimit, staging =>
            {
                string? artifact;
                try
                {
                    artifact = compile(request, staging, diagnostics);
                }
                catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
                {
                    artifact = null;
                }

                if(artifact is null || diagnostics.HasErrors)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.GeneratorBuildFailed, new ProjectLocation(reference.ProjectPath, null), new NameValue(reference.Name));
                    return null;
                }

                return Record(request, request.CompileKey, []) with { ArtifactPath = Path.GetRelativePath(staging, artifact) };
            });
            if(compiled is null)
            {
                return null;
            }

            var key = GeneratorKeys.Run(request.ToolchainIdentity, request.CompileKey, request.ConsumerName,
                request.ConsumerManifest, reference.Arguments, request.Inputs, reference.Seed);
            var reported = false;
            var published = cache.Use("runs", key, reference.TimeLimit, staging =>
            {
                var copied = reference.Inputs.Select(input => Path.Combine(staging, "inputs", Mirror(input))).ToArray();
                for(var i = 0; i < copied.Length; i++)
                {
                    files.Write(copied[i], snapshots[i]);
                }

                var text = string.Join('\n', copied) + "\n\n" + string.Concat(reference.Arguments.Select(argument => argument + "\n"));
                files.Write(Path.Combine(staging, "request.txt"), Encoding.UTF8.GetBytes(text));
                var artifact = Path.Combine(compiled.Folder, compiled.Provenance.ArtifactPath!);
                var outcome = run(new DotnetInvocation($"\"{artifact}\" \"{staging}\"", staging,
                    ImmutableDictionary<string, string>.Empty.Add("MIST_GENERATOR_STAGING", staging), reference.TimeLimit)
                { CleanEnvironment = true, ArgumentList = [artifact, staging] });
                reported = true;
                if(!ReportMessages(staging, request, diagnostics))
                {
                    return null;
                }

                if(outcome.TimedOut || outcome.ExitCode != 0)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.GeneratorRunFailed, manifest, new NameValue(reference.Name),
                        new TextValue(outcome.TimedOut ? "time limit exceeded" : CSharpProjectBuilder.ErrorLines(outcome, "generator")));
                    return null;
                }

                if(diagnostics.HasErrors)
                {
                    return null;
                }

                var listPath = Path.Combine(staging, "outputs.txt");
                var list = files.Exists(listPath) ? Lines(listPath) : [];
                var actual = files.Files(staging).Where(path => !SourcePathSelection.IsWithin(path, Path.Combine(staging, "inputs")))
                    .Select(path => Path.GetRelativePath(staging, path)).ToArray();
                var fault = OutputListValidation.Validate(list, actual.Where(path => path.EndsWith(".mist", StringComparison.OrdinalIgnoreCase)),
                    actual.Where(path => Path.GetFileName(path) == SourcePathSelection.GeneratedMarker).Select(path => Path.GetDirectoryName(path) ?? ""),
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                if(fault is not null)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.GeneratorOutputInvalid, manifest, new PathValue(fault.Path), new TextValue(fault.Reason));
                    return null;
                }

                return Record(request, key, [.. list.Select(path => new ContentHash(OutputListValidation.Normalize(path),
                    GeneratorProvenance.Hash(files.Read(Path.Combine(staging, OutputListValidation.Normalize(path))))))]);
            });
            if(published is not null && !reported && !ReportMessages(published.Folder, request, diagnostics))
            {
                return null;
            }

            return diagnostics.HasErrors ? null : published;
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or TimeoutException or ArgumentException)
        {
            diagnostics.ReportTyped(DriverDiagnostics.GeneratorRunFailed, manifest, new NameValue(reference.Name),
                new TextValue(exception is TimeoutException ? "cache lock time limit exceeded" : "generator files or process could not be accessed"));
            return null;
        }
    }

    private bool ReportMessages(string staging, GeneratorRequest request, DiagnosticBag diagnostics)
    {
        var path = Path.Combine(staging, "diagnostics.txt");
        if(!files.Exists(path))
        {
            return true;
        }

        foreach(var line in Lines(path))
        {
            if(GeneratorDiagnostics.Parse(line) is not { } message)
            {
                diagnostics.ReportTyped(DriverDiagnostics.GeneratorOutputInvalid, new ProjectLocation(request.ConsumerManifest, null),
                    new PathValue("diagnostics.txt"), new TextValue("malformed diagnostic line"));
                return false;
            }

            var original = request.Reference.Inputs.FirstOrDefault(input => message.File.EndsWith(
                Path.Combine("inputs", Mirror(input)), StringComparison.Ordinal));
            GeneratorDiagnostics.Report(original is null ? message : message with { File = original }, request.Reference.Name, staging, diagnostics);
        }

        return true;
    }

    private string[] Lines(string path)
    {
        return Encoding.UTF8.GetString(files.Read(path)).Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();
    }

    private static ProvenanceRecord Record(GeneratorRequest request, string key, ImmutableArray<ContentHash> outputs)
    {
        return new ProvenanceRecord(request.ToolchainIdentity, request.CompileKey, request.ConsumerName, request.ConsumerManifest,
            request.Inputs, request.Reference.Arguments, key, outputs, request.Reference.Seed);
    }

    private static string Mirror(string path)
    {
        var mirrored = OperatingSystem.IsWindows() ? path.Replace(':', '_') : path;
        return mirrored.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}