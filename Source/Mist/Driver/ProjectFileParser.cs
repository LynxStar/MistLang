using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Tomlyn;
using Tomlyn.Model;

namespace Mist.Driver;

public static partial class ProjectFileParser
{
    public const string FileName = "mist.toml";
    public const int MaxNameLength = 128;

    /// <summary>
    /// Applies to the trimmed name.
    /// </summary>
    public const int MaxFrameworkReferenceNameLength = 256;

    /// <summary>
    /// Already set by the compiler's generated PropertyGroup; a user override is allowed, with a warning.
    /// </summary>
    private static readonly HashSet<string> CompilerManagedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "OutputType",
        "TargetFramework",
        "ImplicitUsings",
        "Nullable",
        "Deterministic",
        "EnableNETAnalyzers"
    };

    [GeneratedRegex(@"^[a-zA-Z0-9\-_\.]+$")]
    private static partial Regex ValidNamePattern();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$")]
    private static partial Regex ValidNamespacePattern();

    public static ProjectConfiguration? Parse(string projectFolder, DiagnosticBag diagnostics)
    {
        return Parse(projectFolder, diagnostics, root: false);
    }

    /// <summary>
    /// Parses the manifest of the project being compiled, whose <c>[diagnostics]</c> table is the
    /// policy of the whole operation: it is handed to <paramref name="diagnostics"/>' operation before
    /// the rest of the manifest is read, so the manifest's own warnings already obey it. A referenced
    /// project's manifest goes through <see cref="Parse(string, DiagnosticBag)"/> and sets no policy.
    /// </summary>
    public static ProjectConfiguration? ParseRoot(string projectFolder, DiagnosticBag diagnostics)
    {
        return Parse(projectFolder, diagnostics, root: true);
    }

    private static ProjectConfiguration? Parse(string projectFolder, DiagnosticBag diagnostics, bool root)
    {
        var filePath = Path.Combine(projectFolder, FileName);

        if(!File.Exists(filePath))
        {
            diagnostics.ReportTyped(DriverDiagnostics.ProjectFileNotFound, new FileOnly(filePath), new PathValue(filePath));
            return null;
        }

        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch(Exception ex)
        {
            diagnostics.ReportTyped(DriverDiagnostics.ProjectFileReadError, new FileOnly(filePath), new PathValue(filePath), new TextValue(ex.Message));
            return null;
        }

        TomlTable model;
        try
        {
            model = Toml.ToModel(content);
        }
        catch(TomlException ex)
        {
            diagnostics.ReportTyped(DriverDiagnostics.ProjectTomlError, new FileOnly(filePath), new PathValue(filePath), new TextValue(ex.Message));
            return null;
        }

        if(ReadDiagnosticsPolicy(model, filePath, diagnostics) is not { } policy)
        {
            return null;
        }

        if(root)
        {
            diagnostics.Operation.Policy = diagnostics.Operation.Overrides.Apply(policy);
        }

        if(!model.TryGetValue("name", out var nameObj) || nameObj is not string)
        {
            diagnostics.ReportTyped(DriverDiagnostics.ProjectMissingName, new FileOnly(filePath), new PathValue(filePath));
            return null;
        }

        var name = (string)nameObj;

        if(string.IsNullOrWhiteSpace(name))
        {
            diagnostics.ReportTyped(DriverDiagnostics.ProjectEmptyName, new FileOnly(filePath), new PathValue(filePath));
            return null;
        }

        if(!ValidNamePattern().IsMatch(name))
        {
            var invalidChars = name
                .Where(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                .Distinct()
                .OrderBy(c => c);

            diagnostics.ReportTyped(DriverDiagnostics.ProjectInvalidName, new FileOnly(filePath), new PathValue(filePath), new SequenceValue(SlotKind.Name, [.. invalidChars.Select(character => new NameValue(character.ToString()))], ItemPrefix: "'", ItemSuffix: "'"));
            return null;
        }

        if(name.Length > MaxNameLength)
        {
            diagnostics.ReportTyped(DriverDiagnostics.ProjectNameTooLong, new FileOnly(filePath), new PathValue(filePath), new QuantityValue(MaxNameLength), new QuantityValue(name.Length));
            return null;
        }

        string namespaceRoot;
        if(model.TryGetValue("namespace_root", out var nsRootObj) && nsRootObj is string nsRootStr)
        {
            if(string.IsNullOrWhiteSpace(nsRootStr))
            {
                diagnostics.ReportTyped(DriverDiagnostics.ProjectEmptyNamespaceRoot, new FileOnly(filePath), new PathValue(filePath));
                return null;
            }

            if(!ValidNamePattern().IsMatch(nsRootStr))
            {
                var invalidChars = nsRootStr
                    .Where(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                    .Distinct()
                    .OrderBy(c => c);

                diagnostics.ReportTyped(DriverDiagnostics.ProjectInvalidNamespaceRoot, new FileOnly(filePath), new PathValue(filePath), new SequenceValue(SlotKind.Name, [.. invalidChars.Select(character => new NameValue(character.ToString()))], ItemPrefix: "'", ItemSuffix: "'"));
                return null;
            }

            namespaceRoot = nsRootStr;
        }
        else
        {
            namespaceRoot = name;
        }

        string? entryPoint = null;
        if(model.TryGetValue("entry_point", out var epObj) && epObj is string epStr)
        {
            if(string.IsNullOrWhiteSpace(epStr))
            {
                diagnostics.ReportTyped(DriverDiagnostics.ProjectEmptyEntryPoint, new FileOnly(filePath), new PathValue(filePath));
                return null;
            }

            entryPoint = epStr;
        }

        var references = ImmutableArray.CreateBuilder<ProjectReference>();

        if(model.TryGetValue("references", out var refsObj))
        {
            if(refsObj is not TomlTable refsTable)
            {
                diagnostics.ReportTyped(DriverDiagnostics.ProjectReferencesNotTable, new FileOnly(filePath), new PathValue(filePath));
                return null;
            }

            foreach(var (refName, refValue) in refsTable)
            {
                if(string.IsNullOrWhiteSpace(refName))
                {
                    diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceEmptyName, new FileOnly(filePath), new NameValue(refName), new PathValue(filePath));
                    return null;
                }

                if(!ValidNamePattern().IsMatch(refName))
                {
                    diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceInvalidName, new FileOnly(filePath), new NameValue(refName), new PathValue(filePath));
                    return null;
                }

                if(refName.Length > MaxNameLength)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceNameTooLong, new FileOnly(filePath), new NameValue(refName), new PathValue(filePath), new QuantityValue(MaxNameLength), new QuantityValue(refName.Length));
                    return null;
                }

                if(refValue is not TomlTable refEntry)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceInvalidFormat, new FileOnly(filePath), new NameValue(refName), new PathValue(filePath));
                    return null;
                }

                if(!refEntry.TryGetValue("path", out var pathObj) || pathObj is not string rawPath)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceMissingPath, new FileOnly(filePath), new NameValue(refName), new PathValue(filePath));
                    return null;
                }

                if(string.IsNullOrWhiteSpace(rawPath))
                {
                    diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceEmptyPath, new FileOnly(filePath), new NameValue(refName), new PathValue(filePath));
                    return null;
                }

                var normalizedPath = Path.IsPathRooted(rawPath) ? Path.GetFullPath(rawPath) : Path.GetFullPath(Path.Combine(projectFolder, rawPath));

                // A directory or a missing path is left for the resolver to judge.
                if(ReferenceTo(refName, normalizedPath) is not { } reference)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.ReferenceUnsupportedPath, new FileOnly(filePath), new NameValue(refName), new PathValue(normalizedPath));
                    return null;
                }

                references.Add(reference);
            }
        }

        var globalUsings = ImmutableArray<string>.Empty;

        if(model.TryGetValue("global_usings", out var guObj))
        {
            if(guObj is not TomlTable guTable)
            {
                diagnostics.ReportTyped(DriverDiagnostics.GlobalUsingInvalidFormat, new FileOnly(filePath), new PathValue(filePath));
                return null;
            }

            if(!guTable.TryGetValue("namespaces", out var nsObj))
            {
                diagnostics.ReportTyped(DriverDiagnostics.GlobalUsingMissingNamespaces, new FileOnly(filePath), new PathValue(filePath));
                return null;
            }

            if(nsObj is not TomlArray nsArray)
            {
                diagnostics.ReportTyped(DriverDiagnostics.GlobalUsingNamespacesNotArray, new FileOnly(filePath), new PathValue(filePath));
                return null;
            }

            var builder = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for(var i = 0; i < nsArray.Count; i++)
            {
                if(nsArray[i] is not string entry)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.GlobalUsingElementNotString, new FileOnly(filePath), new QuantityValue(i), new PathValue(filePath));
                    return null;
                }

                if(string.IsNullOrWhiteSpace(entry))
                {
                    diagnostics.ReportTyped(DriverDiagnostics.GlobalUsingEmptyNamespace, new FileOnly(filePath), new QuantityValue(i), new PathValue(filePath));
                    return null;
                }

                if(!ValidNamespacePattern().IsMatch(entry))
                {
                    var invalidChars = entry
                        .Where(c => !char.IsLetterOrDigit(c) && c != '_' && c != '.')
                        .Distinct()
                        .OrderBy(c => c);

                    diagnostics.ReportTyped(DriverDiagnostics.GlobalUsingInvalidNamespace, new FileOnly(filePath), new NameValue(entry), new PathValue(filePath), new SequenceValue(SlotKind.Name, [.. invalidChars.Select(character => new NameValue(character.ToString()))], ItemPrefix: "'", ItemSuffix: "'"));
                    return null;
                }

                if(!seen.Add(entry))
                {
                    diagnostics.ReportTyped(DriverDiagnostics.GlobalUsingDuplicateNamespace, new FileOnly(filePath), new NameValue(entry), new PathValue(filePath));
                    continue;
                }

                builder.Add(entry);
            }

            globalUsings = builder.ToImmutable();
        }

        var packages = ImmutableArray<NuGetPackageReference>.Empty;

        if(model.TryGetValue("packages", out var pkgsObj))
        {
            if(pkgsObj is not TomlTable pkgsTable)
            {
                diagnostics.ReportTyped(DriverDiagnostics.PackageTableExpected, new FileOnly(filePath), new PathValue(filePath));
                return null;
            }

            var pkgBuilder = ImmutableArray.CreateBuilder<NuGetPackageReference>();

            foreach(var (pkgName, pkgValue) in pkgsTable)
            {
                if(!ValidNamePattern().IsMatch(pkgName))
                {
                    var invalidChars = pkgName
                        .Where(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                        .Distinct()
                        .OrderBy(c => c);

                    diagnostics.ReportTyped(DriverDiagnostics.PackageInvalidName, new FileOnly(filePath), new NameValue(pkgName), new PathValue(filePath), new SequenceValue(SlotKind.Name, [.. invalidChars.Select(character => new NameValue(character.ToString()))], ItemPrefix: "'", ItemSuffix: "'"));
                    continue;
                }

                if(pkgName.Length > MaxNameLength)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.PackageNameTooLong, new FileOnly(filePath), new NameValue(pkgName), new PathValue(filePath), new QuantityValue(MaxNameLength), new QuantityValue(pkgName.Length));
                    continue;
                }

                if(pkgValue is not string versionStr)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.PackageInvalidVersion, new FileOnly(filePath), new NameValue(pkgName), new PathValue(filePath));
                    continue;
                }

                if(string.IsNullOrWhiteSpace(versionStr))
                {
                    diagnostics.ReportTyped(DriverDiagnostics.PackageEmptyVersion, new FileOnly(filePath), new NameValue(pkgName), new PathValue(filePath));
                    continue;
                }

                pkgBuilder.Add(new NuGetPackageReference(pkgName, versionStr));
            }

            packages = pkgBuilder.ToImmutable();
        }

        var targetFramework = "net10.0";
        if(model.TryGetValue("target_framework", out var tfObj)
            && tfObj is string tfStr
            && !string.IsNullOrWhiteSpace(tfStr))
        {
            targetFramework = tfStr.Trim();
        }

        var frameworkReferences = ImmutableArray<FrameworkReference>.Empty;

        if(model.TryGetValue("framework_references", out var fwrefsObj))
        {
            if(fwrefsObj is not TomlTable fwrefsTable)
            {
                // Deliberately not fatal: the parse continues with no framework references.
                diagnostics.ReportTyped(DriverDiagnostics.FrameworkReferenceTableExpected, new FileOnly(filePath), new PathValue(filePath));
            }
            else
            {
                var fwrefBuilder = ImmutableArray.CreateBuilder<FrameworkReference>();
                var seenFrameworks = new HashSet<string>(StringComparer.Ordinal);

                foreach(var (rawFwName, fwValue) in fwrefsTable)
                {
                    var fwName = rawFwName.Trim();

                    if(fwName.Length == 0)
                    {
                        diagnostics.ReportTyped(DriverDiagnostics.FrameworkReferenceInvalidName, new FileOnly(filePath), new NameValue(rawFwName), new PathValue(filePath));
                        continue;
                    }

                    if(fwName.Length > MaxFrameworkReferenceNameLength)
                    {
                        diagnostics.ReportTyped(DriverDiagnostics.FrameworkReferenceNameTooLong, new FileOnly(filePath), new NameValue(fwName), new PathValue(filePath), new QuantityValue(MaxFrameworkReferenceNameLength), new QuantityValue(fwName.Length));
                        continue;
                    }

                    if(fwValue is not bool enabled)
                    {
                        diagnostics.ReportTyped(DriverDiagnostics.FrameworkReferenceInvalidValue, new FileOnly(filePath), new NameValue(fwName), new PathValue(filePath));
                        continue;
                    }

                    if(!enabled)
                    {
                        continue;
                    }

                    if(seenFrameworks.Add(fwName))
                    {
                        fwrefBuilder.Add(new FrameworkReference(fwName));
                    }
                }

                frameworkReferences = fwrefBuilder.ToImmutable();
            }
        }

        var csprojProperties = ImmutableArray<CsprojProperty>.Empty;

        if(model.TryGetValue("csproj_properties", out var csprojPropsObj))
        {
            if(csprojPropsObj is not TomlTable csprojPropsTable)
            {
                diagnostics.ReportTyped(DriverDiagnostics.CsprojPropertyTableExpected, new FileOnly(filePath), new PathValue(filePath));
            }
            else
            {
                var csprojPropBuilder = ImmutableArray.CreateBuilder<CsprojProperty>();

                foreach(var (rawKey, propValue) in csprojPropsTable)
                {
                    if(string.IsNullOrWhiteSpace(rawKey))
                    {
                        diagnostics.ReportTyped(DriverDiagnostics.CsprojPropertyEmptyKey, new FileOnly(filePath), new PathValue(filePath));
                        continue;
                    }

                    if(!ValidNamePattern().IsMatch(rawKey))
                    {
                        var invalidChars = rawKey
                            .Where(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                            .Distinct()
                            .OrderBy(c => c);

                        diagnostics.ReportTyped(DriverDiagnostics.CsprojPropertyInvalidKey, new FileOnly(filePath), new NameValue(rawKey), new PathValue(filePath), new SequenceValue(SlotKind.Name, [.. invalidChars.Select(character => new NameValue(character.ToString()))], ItemPrefix: "'", ItemSuffix: "'"));
                        continue;
                    }

                    if(rawKey.Length > MaxNameLength)
                    {
                        diagnostics.ReportTyped(DriverDiagnostics.CsprojPropertyKeyTooLong, new FileOnly(filePath), new NameValue(rawKey[..32] + "..."), new PathValue(filePath), new QuantityValue(MaxNameLength), new QuantityValue(rawKey.Length));
                        continue;
                    }

                    if(propValue is not string valueStr)
                    {
                        diagnostics.ReportTyped(DriverDiagnostics.CsprojPropertyInvalidValue, new FileOnly(filePath), new NameValue(rawKey), new PathValue(filePath));
                        continue;
                    }

                    if(string.IsNullOrWhiteSpace(valueStr))
                    {
                        diagnostics.ReportTyped(DriverDiagnostics.CsprojPropertyEmptyValue, new FileOnly(filePath), new NameValue(rawKey), new PathValue(filePath));
                        continue;
                    }

                    if(CompilerManagedProperties.Contains(rawKey))
                    {
                        diagnostics.ReportTyped(DriverDiagnostics.CsprojPropertyManagedOverride, new FileOnly(filePath), new NameValue(rawKey), new PathValue(filePath));
                    }

                    csprojPropBuilder.Add(new CsprojProperty(rawKey, valueStr));
                }

                csprojProperties = csprojPropBuilder.ToImmutable();
            }
        }

        var outputType = ProjectOutputType.Executable;

        if(model.TryGetValue("output_type", out var outputTypeObj))
        {
            if(outputTypeObj is not string outputTypeStr)
            {
                diagnostics.ReportTyped(DriverDiagnostics.ProjectInvalidOutputType, new FileOnly(filePath), new EnumValue("<non-string>"), new PathValue(filePath));
                return null;
            }

            switch(outputTypeStr)
            {
                case "executable":
                    outputType = ProjectOutputType.Executable;
                    break;
                case "library":
                    outputType = ProjectOutputType.Library;
                    break;
                default:
                    diagnostics.ReportTyped(DriverDiagnostics.ProjectInvalidOutputType, new FileOnly(filePath), new EnumValue(outputTypeStr), new PathValue(filePath));
                    return null;
            }
        }

        return new ProjectConfiguration(name, namespaceRoot, references.ToImmutable(), entryPoint, globalUsings, packages, frameworkReferences, targetFramework, csprojProperties, outputType, policy);
    }

    internal static DiagnosticPolicy? ReadDiagnosticsPolicy(TomlTable model, string filePath, DiagnosticBag diagnostics)
    {
        if(!model.TryGetValue("diagnostics", out var value))
        {
            return DiagnosticPolicy.Default;
        }

        if(value is not TomlTable table)
        {
            diagnostics.ReportTyped(DriverDiagnostics.DiagnosticsTableExpected, new FileOnly(filePath), new PathValue(filePath));
            return null;
        }

        var faults = 0;
        var promoteAll = false;
        var promoted = ImmutableHashSet.CreateBuilder<DiagnosticNumber>();
        var disabled = ImmutableHashSet.CreateBuilder<DiagnosticNumber>();
        var enabled = ImmutableHashSet.CreateBuilder<DiagnosticNumber>();

        foreach(var (key, entry) in table)
        {
            switch(key)
            {
                case "warnings_as_errors" when entry is bool all:
                    promoteAll = all;
                    break;

                case "warnings_as_errors" when entry is TomlArray numbers:
                    faults += Numbers(numbers, key, filePath, diagnostics, switchable: false, promoted);
                    break;

                case "warnings_as_errors":
                    diagnostics.ReportTyped(DriverDiagnostics.DiagnosticsValueInvalid, new FileOnly(filePath), new NameValue(key), new PathValue(filePath), new TextValue("true, false or an array of diagnostic numbers"));
                    faults++;
                    break;

                case "disable" or "enable" when entry is TomlArray numbers:
                    faults += Numbers(numbers, key, filePath, diagnostics, switchable: true, key == "disable" ? disabled : enabled);
                    break;

                case "disable" or "enable":
                    diagnostics.ReportTyped(DriverDiagnostics.DiagnosticsValueInvalid, new FileOnly(filePath), new NameValue(key), new PathValue(filePath), new TextValue("an array of diagnostic numbers"));
                    faults++;
                    break;

                default:
                    diagnostics.ReportTyped(DriverDiagnostics.DiagnosticsKeyUnknown, new FileOnly(filePath), new NameValue(key), new PathValue(filePath));
                    faults++;
                    break;
            }
        }

        return faults > 0 ? null : new DiagnosticPolicy(promoteAll, promoted.ToImmutable(), disabled.ToImmutable(), enabled.ToImmutable());
    }

    private static int Numbers(
        TomlArray numbers,
        string key,
        string filePath,
        DiagnosticBag diagnostics,
        bool switchable,
        ImmutableHashSet<DiagnosticNumber>.Builder into)
    {
        var faults = 0;
        foreach(var item in numbers)
        {
            if(item is not string code)
            {
                diagnostics.ReportTyped(DriverDiagnostics.DiagnosticsValueInvalid, new FileOnly(filePath), new NameValue(key), new PathValue(filePath), new TextValue("an array of diagnostic numbers written as strings"));
                faults++;
                continue;
            }

            if(DiagnosticRegistry.Index.Find(code) is not { } descriptor)
            {
                diagnostics.ReportTyped(DriverDiagnostics.DiagnosticsNumberUnknown, new FileOnly(filePath), new NameValue(code), new NameValue(key), new PathValue(filePath));
                faults++;
                continue;
            }

            if(switchable && descriptor.Policy != PolicyClass.Optional)
            {
                diagnostics.ReportTyped(DriverDiagnostics.DiagnosticsNumberNotOptional, new FileOnly(filePath), new NameValue(code), new NameValue(key), new PathValue(filePath), new EnumValue(descriptor.Policy.ToString()));
                faults++;
                continue;
            }

            _ = into.Add(descriptor.Number);
        }

        return faults;
    }

    private static ProjectReference? ReferenceTo(string refName, string normalizedPath)
    {
        if(Path.GetFileName(normalizedPath).Equals(FileName, StringComparison.Ordinal))
        {
            return new ProjectReference(refName, normalizedPath);
        }

        if(Directory.Exists(normalizedPath))
        {
            return new ProjectReference(refName, Path.Combine(normalizedPath, FileName), IsDirectory: true);
        }

        if(ReferenceClassifier.Classify(normalizedPath, isDirectory: false, []) is ReferenceClassified classified)
        {
            return new ProjectReference(refName, classified.ResolvedPath, classified.Kind);
        }

        return File.Exists(normalizedPath) ? null : new ProjectReference(refName, Path.Combine(normalizedPath, FileName));
    }
}