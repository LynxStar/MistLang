using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;

namespace Mist.ProgramAnalysis;

/// <summary>
/// What one project starts at, decided from what it declares and what its manifest asks for
/// (top-level-statements Requirements 2 to 4, and attributes-and-test-command's library rule). The one
/// place it is decided: <see cref="Rules.EntryPointAnalyzer"/> reports a refusal and the emitter emits
/// the rest, so the two cannot disagree about which <c>Main</c> runs.
/// </summary>
internal abstract record EntryPointSelection
{
    public const string TopLevelSetting = "top-level";

    public sealed record TopLevelStatements(string File) : EntryPointSelection;

    public sealed record Main(string Class) : EntryPointSelection;

    public sealed record None : EntryPointSelection;

    public sealed record Refused(DiagnosticDescriptor Descriptor, ImmutableArray<string> Arguments) : EntryPointSelection;

    public static EntryPointSelection Of(MistAssembly assembly)
    {
        return Decide(
            [.. assembly.CompilationUnits.Where(unit => unit.HasTopLevelStatements).Select(unit => unit.SourcePath)],
            [.. assembly.EntryPoints.Select(method => method.DeclaringType?.FullyQualifiedName).OfType<string>()],
            assembly.EntryPointSetting,
            assembly.IsLibrary);
    }

    public static EntryPointSelection Decide(
        IReadOnlyList<string> topLevelFiles, IReadOnlyList<string> mains, string? setting, bool isLibrary)
    {
        var files = topLevelFiles.Order(StringComparer.Ordinal).ToList();
        var classes = mains.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if(isLibrary && files.Count > 0)
        {
            return Refuse(EntryPointDiagnostics.LibraryWithTopLevelStatements, string.Join(", ", files));
        }

        if(files.Count > 1)
        {
            return Refuse(EntryPointDiagnostics.MultipleTopLevelFiles, string.Join(", ", files));
        }

        if(setting == TopLevelSetting)
        {
            return files.Count == 1
                ? new TopLevelStatements(files[0])
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
                ? new TopLevelStatements(files[0])
                : Refuse(EntryPointDiagnostics.TopLevelStatementsBesideMain, files[0], string.Join("', '", classes));
        }

        if(classes.Count > 1 && !isLibrary)
        {
            return Refuse(EntryPointDiagnostics.MultipleMains, string.Join(", ", classes), classes[0]);
        }

        return classes.Count == 1 ? MainOf(classes[0], mains, isLibrary) : new None();
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

    private static Refused Refuse(DiagnosticDescriptor descriptor, params ImmutableArray<string> arguments)
    {
        return new Refused(descriptor, arguments);
    }
}