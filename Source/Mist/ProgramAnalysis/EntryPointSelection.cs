using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;

namespace Mist.ProgramAnalysis;

/// <summary>
/// What one project starts at, decided from what it declares and what its manifest asks for
/// (top-level-statements Requirements 1.6 and 2 to 4, and attributes-and-test-command's library rule). The
/// one place it is decided: <see cref="Rules.EntryPointAnalyzer"/> reports a refusal and the emitter emits
/// the rest, so the two cannot disagree about which <c>Main</c> runs.
/// </summary>
internal abstract record EntryPointSelection
{
    public const string TopLevelSetting = "top-level";

    public sealed record TopLevelFile(string Path, ImmutableArray<string> BlockNamespaces);

    public sealed record TopLevelStatements(string File) : EntryPointSelection;

    public sealed record Main(string Class) : EntryPointSelection;

    public sealed record None : EntryPointSelection;

    public sealed record Refused(MistDiagnostic Descriptor, ImmutableArray<string> Arguments) : EntryPointSelection;

    public static EntryPointSelection Of(MistAssembly assembly)
    {
        return Decide(
            [.. assembly.CompilationUnits
                .Where(unit => unit.HasTopLevelStatements)
                .Select(unit => new TopLevelFile(unit.SourcePath, unit.BlockNamespaces))],
            [.. assembly.EntryPoints.Select(method => method.DeclaringType?.FullyQualifiedName).OfType<string>()],
            assembly.EntryPointSetting,
            assembly.IsLibrary);
    }

    public static EntryPointSelection Decide(
        IReadOnlyList<TopLevelFile> topLevelFiles, IReadOnlyList<string> mains, string? setting, bool isLibrary)
    {
        var files = topLevelFiles.OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
        var paths = string.Join(", ", files.Select(file => file.Path));
        var classes = mains.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if(isLibrary && files.Count > 0)
        {
            return Refuse(EntryPointDiagnostics.LibraryWithTopLevelStatements, paths);
        }

        if(files.Count > 1)
        {
            return Refuse(EntryPointDiagnostics.MultipleTopLevelFiles, paths);
        }

        if(setting == TopLevelSetting)
        {
            return files.Count == 1
                ? StatementsOf(files[0])
                : Refuse(EntryPointDiagnostics.TopLevelEntryPointWithoutStatements);
        }

        if(setting is not null)
        {
            if(files.Count == 1)
            {
                return Refuse(EntryPointDiagnostics.ClassEntryPointBesideTopLevelStatements, setting);
            }

            return classes.Contains(setting)
                ? MainOf(setting, mains, isLibrary)
                : Refuse(EntryPointDiagnostics.EntryPointClassNotFound, setting);
        }

        if(files.Count == 1)
        {
            return classes.Count == 0
                ? StatementsOf(files[0])
                : Refuse(EntryPointDiagnostics.TopLevelStatementsBesideMain, files[0].Path, string.Join("', '", classes));
        }

        if(classes.Count > 1 && !isLibrary)
        {
            return Refuse(EntryPointDiagnostics.MultipleMains, string.Join(", ", classes), classes[0]);
        }

        return classes.Count == 1 ? MainOf(classes[0], mains, isLibrary) : new None();
    }

    /// <summary>
    /// Requirement 1.6, judged on the selected file alone and after every project-level refusal, as
    /// <see cref="MainOf"/> judges the selected class: removing the statements, or not selecting them,
    /// resolves each of those refusals and this one with it, whereas a file-scoped namespace resolves
    /// nothing else.
    /// </summary>
    private static EntryPointSelection StatementsOf(TopLevelFile file)
    {
        if(file.BlockNamespaces.IsEmpty)
        {
            return new TopLevelStatements(file.Path);
        }

        var declared = file.BlockNamespaces
            .Distinct(StringComparer.Ordinal)
            .Select(name => $"namespace {name} {{ ... }}");
        return Refuse(EntryPointDiagnostics.TopLevelStatementsWithBlockNamespace, file.Path, string.Join(", ", declared));
    }

    private static EntryPointSelection MainOf(string chosen, IReadOnlyList<string> mains, bool isLibrary)
    {
        if(isLibrary)
        {
            return new None();
        }

        return mains.Count(declaring => declaring == chosen) > 1
            ? Refuse(EntryPointDiagnostics.DuplicateEntryPoint, chosen)
            : new Main(chosen);
    }

    private static Refused Refuse(MistDiagnostic descriptor, params ImmutableArray<string> arguments)
    {
        return new Refused(descriptor, arguments);
    }
}