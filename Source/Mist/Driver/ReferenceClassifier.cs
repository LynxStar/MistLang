using System.Collections.Immutable;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// Decides what a <c>[references]</c> path names from its final segment or, for a directory, from the
/// names of the directory's own files. It never touches the file system: the caller lists the directory.
/// </summary>
public static class ReferenceClassifier
{
    public static ReferenceClassification Classify(string path, bool isDirectory, IReadOnlyList<string> directoryFileNames)
    {
        if(!isDirectory)
        {
            return KindOf(Path.GetFileName(path)) is { } kind
                ? new ReferenceClassified(kind, path)
                : new ReferenceUnsupportedExtension(Path.GetExtension(path));
        }

        var candidates = directoryFileNames
            .Where(name => name.IndexOfAny(['/', '\\']) < 0 && KindOf(name) is not null)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        return candidates.Length switch
        {
            0 => new ReferenceNoCandidate(),
            1 => new ReferenceClassified(KindOf(candidates[0])!.Value, Path.Combine(path, candidates[0])),
            _ => new ReferenceAmbiguous(candidates),
        };
    }

    private static ReferenceKind? KindOf(string fileName)
    {
        if(fileName.Equals(ProjectFileParser.FileName, StringComparison.Ordinal))
        {
            return ReferenceKind.MistProject;
        }

        if(fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceKind.CSharpProject;
        }

        return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ReferenceKind.Assembly : null;
    }
}