using System.Collections.Immutable;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using ProgramModelType = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Provides ambient type resolution from pre-loaded .NET assemblies.
/// <see cref="BuildAndRegister"/> eagerly projects all CLR types in two phases
/// (shells, then type parameters/constraints) and merges them into the ProgramModel.
/// </summary>
internal sealed class AmbientTypeProviderV2 : IAmbientTypeProvider, IDisposable
{
    private readonly ImmutableArray<LoadedAssemblyInfo> assemblies;
    private readonly MetadataLoadContext? metadataContext;
    private readonly DiagnosticBag diagnostics;

    /// <summary>
    /// Creates an AmbientTypeProviderV2 with pre-loaded assemblies.
    /// Call <see cref="BuildAndRegister"/> after the Standard Library is registered
    /// to eagerly project all ambient types.
    /// </summary>
    public AmbientTypeProviderV2(
        ImmutableArray<LoadedAssemblyInfo> assemblies,
        TypeInterner typeInterner,
        MetadataLoadContext? metadataContext,
        DiagnosticBag diagnostics)
    {
        this.assemblies = assemblies;
        this.metadataContext = metadataContext;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets the loaded assemblies available for type resolution.
    /// </summary>
    public ImmutableArray<LoadedAssemblyInfo> LoadedAssemblies => assemblies;

    /// <inheritdoc />
    public void BuildAndRegister(ProgramModelType programModel)
    {
        // Pull well-known Standard Library types out of the ProgramModel.
        var wellKnownTypes = ResolveWellKnownTypes(programModel);

        // Phase 1: Create shells for all exported types.
        var (shells, clrTypes) = CreateShells(wellKnownTypes);

        // Phase 2: Project type parameters and constraints.
        var resolver = new AmbientTypeResolver(shells, wellKnownTypes, diagnostics);
        ProjectTypeParameters(shells, clrTypes, resolver);

        // Merge into ProgramModel namespaces.
        foreach(var (key, shell) in shells)
        {
            var ns = programModel.GetOrAddNamespace(key.Namespace);
            ns.Types[shell.Name] = shell;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        metadataContext?.Dispose();
    }

    /// <summary>
    /// Resolves all well-known CLR types to their Standard Library instances.
    /// These are types like System.Int32 → Standard.Int32 that already exist
    /// in the ProgramModel from source compilation.
    /// </summary>
    private static Dictionary<(string Namespace, string ClrName), IMistType> ResolveWellKnownTypes(ProgramModelType programModel)
    {
        var resolved = new Dictionary<(string Namespace, string ClrName), IMistType>();

        foreach(var (clrKey, mistMapping) in WellKnownTypeMap.Entries)
        {
            if(!programModel.Namespaces.TryGetValue(mistMapping.MistNamespace, out var ns)
                || !ns.Types.TryGetValue(mistMapping.MistTypeName, out var type))
            {
                throw new InvalidOperationException(
                    $"ICE: Well-known type '{mistMapping.MistNamespace}.{mistMapping.MistTypeName}' " +
                    "not found in ProgramModel. The Standard Library must be loaded before ambient type resolution.");
            }

            resolved[clrKey] = type;
        }

        return resolved;
    }

    /// <summary>
    /// Phase 1: Iterates all exported types from all loaded assemblies and creates
    /// empty shells. Skips well-known types (they already exist as Standard Library instances).
    /// Also builds a parallel CLR type dictionary for use in phase 2.
    /// </summary>
    private (Dictionary<(string Namespace, string ClrName), MistType> Shells, Dictionary<(string Namespace, string ClrName), Type> ClrTypes) CreateShells(
        Dictionary<(string Namespace, string ClrName), IMistType> wellKnownTypes)
    {
        var shells = new Dictionary<(string Namespace, string ClrName), MistType>();
        var clrTypes = new Dictionary<(string Namespace, string ClrName), Type>();

        foreach(var assembly in assemblies)
        {
            var exportedTypes = assembly.MetadataAssembly.GetExportedTypes();

            foreach(var clrType in exportedTypes)
            {
                // Skip type-forwarded entries — only project from the defining assembly.
                if(clrType.Assembly != assembly.MetadataAssembly)
                {
                    continue;
                }

                // Skip nested types — they're accessed through their declaring type.
                if(clrType.IsNested)
                {
                    continue;
                }

                var ns = clrType.Namespace ?? "";
                var clrName = clrType.Name;
                var key = (ns, clrName);

                // Skip well-known types — they're Standard Library instances.
                if(wellKnownTypes.ContainsKey(key))
                {
                    continue;
                }

                // Duplicate type in multiple assemblies without forwarding.
                // Emit an error diagnostic — user likely has conflicting NuGet packages.
                if(shells.ContainsKey(key))
                {
                    diagnostics.Report(
                        BackendDiagnostics.DuplicateAmbientType,
                        default,
                        "<ambient>",
                        (object?)ns,
                        clrName,
                        assembly.FilePath);
                    continue;
                }

                var shell = TypeProjector.CreateShell(clrType, ns);
                shells[key] = shell;
                clrTypes[key] = clrType;
            }
        }

        return (shells, clrTypes);
    }

    /// <summary>
    /// Phase 2: Projects type parameters and constraints onto all generic shells.
    /// Constraint resolution uses the shells dictionary (other ambient types) and
    /// the well-known types table (Standard Library intrinsics).
    /// </summary>
    private static void ProjectTypeParameters(
        Dictionary<(string Namespace, string ClrName), MistType> shells,
        Dictionary<(string Namespace, string ClrName), Type> clrTypes,
        AmbientTypeResolver resolver)
    {
        foreach(var (key, clrType) in clrTypes)
        {
            if(!clrType.IsGenericTypeDefinition)
            {
                continue;
            }

            if(shells.TryGetValue(key, out var shell) && shell is MemberType memberType)
            {
                TypeParameterProjector.Project(memberType, clrType, resolver);
            }
        }
    }
}
