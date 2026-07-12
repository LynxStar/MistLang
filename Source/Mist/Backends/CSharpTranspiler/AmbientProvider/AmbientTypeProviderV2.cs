using System.Collections.Immutable;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
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
    private readonly MetadataLoadContext? metadataContext;
    private readonly DiagnosticBag diagnostics;

    /// <summary>
    /// Creates an AmbientTypeProviderV2 with pre-loaded assemblies.
    /// Call <see cref="BuildAndRegister"/> after the Standard Library is registered
    /// to eagerly project all ambient types.
    /// </summary>
    public AmbientTypeProviderV2(
        ImmutableArray<LoadedAssemblyInfo> assemblies,
        MetadataLoadContext? metadataContext,
        DiagnosticBag diagnostics)
    {
        LoadedAssemblies = assemblies;
        this.metadataContext = metadataContext;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets the loaded assemblies available for type resolution.
    /// </summary>
    public ImmutableArray<LoadedAssemblyInfo> LoadedAssemblies { get; }

    /// <inheritdoc />
    public void BuildAndRegister(ProgramModelType programModel)
    {
        // Pull well-known Standard Library types out of the ProgramModel.
        var wellKnownTypes = ResolveWellKnownTypes(programModel);

        // Phase 1: Create shells for all exported types.
        var (shells, clrTypes) = CreateShells(wellKnownTypes);

        // Phase 2: Project type details onto shells (type params, base types, members).
        var resolver = new AmbientTypeResolver(shells, wellKnownTypes, programModel.TypeInterner, diagnostics);
        ProjectTypes(shells, clrTypes, resolver);

        // Merge into ProgramModel FQN dictionary.
        foreach(var (_, shell) in shells)
        {
            _ = programModel.GetOrAddNamespace(shell.NamespaceName);
            _ = programModel.RegisterType(shell);
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
    /// in the ProgramModel from source compilation. Returns a dictionary keyed
    /// by the CLR FQN (e.g., "System.Int32") for use during resolution.
    /// </summary>
    private static Dictionary<string, IMistType> ResolveWellKnownTypes(ProgramModelType programModel)
    {
        var resolved = new Dictionary<string, IMistType>();

        foreach(var (clrKey, mistMapping) in WellKnownTypeMap.Entries)
        {
            var mistFqn = string.IsNullOrEmpty(mistMapping.MistNamespace)
                ? mistMapping.MistTypeName
                : $"{mistMapping.MistNamespace}.{mistMapping.MistTypeName}";

            if(!programModel.TypesByFQN.TryGetValue(mistFqn, out var type))
            {
                throw new InvalidOperationException(
                    $"ICE: Well-known type '{mistFqn}' " +
                    "not found in ProgramModel. The Standard Library must be loaded before ambient type resolution.");
            }

            var clrFqn = string.IsNullOrEmpty(clrKey.Namespace)
                ? clrKey.TypeName
                : $"{clrKey.Namespace}.{clrKey.TypeName}";
            resolved[clrFqn] = type;
        }

        return resolved;
    }

    /// <summary>
    /// Phase 1: Iterates all exported types from all loaded assemblies and creates
    /// empty shells. Skips well-known types (they already exist as Standard Library instances).
    /// Also builds a parallel CLR type dictionary for use in phase 2.
    /// Dictionaries are keyed by FQN string (e.g., "System.Console").
    /// </summary>
    private (Dictionary<string, MistType> Shells, Dictionary<string, Type> ClrTypes) CreateShells(
        Dictionary<string, IMistType> wellKnownTypes)
    {
        var shells = new Dictionary<string, MistType>();
        var clrTypes = new Dictionary<string, Type>();

        foreach(var assembly in LoadedAssemblies)
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
                var fqn = string.IsNullOrEmpty(ns) ? clrName : $"{ns}.{clrName}";

                // Skip well-known types — they're Standard Library instances.
                if(wellKnownTypes.ContainsKey(fqn))
                {
                    continue;
                }

                // Duplicate type in multiple assemblies without forwarding.
                // Emit an error diagnostic — user likely has conflicting NuGet packages.
                if(shells.ContainsKey(fqn))
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
                shells[fqn] = shell;
                clrTypes[fqn] = clrType;
            }
        }

        return (shells, clrTypes);
    }

    /// <summary>
    /// Phase 2: Single pass over all types — dispatches to the appropriate projector
    /// for type parameters, base types, interfaces, enum members, etc.
    /// </summary>
    private static void ProjectTypes(
        Dictionary<string, MistType> shells,
        Dictionary<string, Type> clrTypes,
        AmbientTypeResolver resolver)
    {
        foreach(var (fqn, clrType) in clrTypes)
        {
            var shell = shells[fqn];

            switch(shell)
            {
                case EnumType enumType:
                    EnumProjector.Project(enumType, clrType, resolver);
                    break;

                case MemberType memberType:
                    if(clrType.IsGenericTypeDefinition)
                    {
                        TypeParameterProjector.Project(memberType, clrType, resolver);
                    }

                    BaseTypeProjector.Project(shell, clrType, resolver);
                    PropertyProjector.Project(memberType, clrType, resolver);
                    IndexerProjector.Project(memberType, clrType, resolver);
                    MethodProjector.Project(memberType, clrType, resolver);

                    if(shell is ConcreteType concreteType)
                    {
                        FieldProjector.Project(concreteType, clrType, resolver);
                        ConstructorProjector.Project(concreteType, clrType, resolver);
                    }

                    break;
            }
        }
    }
}