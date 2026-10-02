using System.Collections.Immutable;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
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
    private AmbientTypeResolver? resolver;
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

    public Type? FindClrType(string fullName)
    {
        foreach(var assembly in LoadedAssemblies)
        {
            if(assembly.MetadataAssembly.GetType(fullName) is { } type)
            {
                return type;
            }
        }

        return null;
    }

    internal MistType? ProjectionOf(Type clrType)
    {
        return resolver?.Declared(clrType);
    }

    internal Method? Judge(MethodInfo method, MemberType declaringType, out Withholding? withheld)
    {
        return MethodProjector.Judge(method, declaringType, ProjectingResolver(), out withheld);
    }

    internal Property? Judge(PropertyInfo property, MemberType declaringType, out Withholding? withheld)
    {
        return PropertyProjector.Judge(property, declaringType, ProjectingResolver(), out withheld);
    }

    internal Indexer? JudgeIndexer(PropertyInfo property, MemberType declaringType, out Withholding? withheld)
    {
        return IndexerProjector.Judge(property, declaringType, ProjectingResolver(), out withheld);
    }

    /// <inheritdoc />
    public void BuildAndRegister(ProgramModelType programModel)
    {
        // Pull well-known Standard Library types out of the ProgramModel.
        var wellKnownTypes = ResolveWellKnownTypes(programModel);

        // Phase 1: Create shells for all exported types.
        var (shells, clrTypes) = CreateShells(wellKnownTypes);

        // Phase 2: Project type details onto shells (type params, base types, members).
        resolver = new AmbientTypeResolver(shells, wellKnownTypes, programModel.TypeInterner, diagnostics);
        ProjectTypes(shells, clrTypes, resolver, wellKnownTypes);

        // Merge into ProgramModel FQN dictionary. This loop is the single funnel for
        // every projected shell, nested types included, so it is where they are
        // attributed to the ambient assembly. Well-known types are absent from
        // 'shells' -- they are Standard Library types declared in Mist source and keep
        // the standard library assembly they were registered with.
        foreach(var (_, shell) in shells)
        {
            shell.Assembly = MistAssembly.Ambient;
            _ = programModel.GetOrAddNamespace(shell.NamespaceName);
            _ = programModel.RegisterType(shell);
        }

        // Phase 3: Project extension methods onto namespaces.
        // Runs after all types are registered so receiver types are resolvable.
        ExtensionProjector.ProjectExtensions(clrTypes, resolver, programModel);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        metadataContext?.Dispose();
    }

    private AmbientTypeResolver ProjectingResolver()
    {
        return resolver ?? throw new InvalidOperationException(
            "ICE: an ambient member was judged before BuildAndRegister projected the ambient types.");
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
            var mistFqn = $"{mistMapping.MistNamespace}.{mistMapping.MistTypeName}";

            if(!programModel.TypesByFQN.TryGetValue(mistFqn, out var type))
            {
                throw new InvalidOperationException(
                    $"ICE: Well-known type '{mistFqn}' " +
                    "not found in ProgramModel. The Standard Library must be loaded before ambient type resolution.");
            }

            var clrFqn = string.IsNullOrEmpty(clrKey.Namespace)
                ? clrKey.TypeName
                : $"{clrKey.Namespace}.{clrKey.TypeName}"
                ;

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
    private (Dictionary<string, DeclaredType> Shells, Dictionary<string, Type> ClrTypes) CreateShells(
        Dictionary<string, IMistType> wellKnownTypes)
    {
        var shells = new Dictionary<string, DeclaredType>();
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

                // Skip nested types — they're projected recursively from their declaring type.
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

                // FIRST DEFINITION WINS, SILENTLY, which is what classic resolves to. Classic never
                // meets this question: it looks a type up on demand and returns the first assembly
                // whose namespace index has the name. Shelling every exported type eagerly is what
                // surfaces the collision, so the collision is v2's to absorb rather than to report.
                // Type FORWARDING is already excluded above; what reaches here is the same name
                // genuinely defined twice, which real package sets do -- 223 of them in
                // samples/Testing alone, where the message's advice to remove a conflicting package
                // named the test platform's own assemblies.
                if(shells.ContainsKey(fqn))
                {
                    continue;
                }

                var shell = TypeProjector.CreateShell(clrType, ns);
                shells[fqn] = shell;
                clrTypes[fqn] = clrType;

                // Recursively create shells for public nested types.
                CreateNestedShells(clrType, shell, fqn, ns, wellKnownTypes, shells, clrTypes);
            }
        }

        return (shells, clrTypes);
    }

    /// <summary>
    /// Recursively creates shells for all public and protected nested types of a CLR type.
    /// Each nested shell uses the parent type's scope as its scope parent.
    /// Dictionary keys use CLR-format dot notation (e.g., "System.Environment.SpecialFolder").
    /// Protected nested types are projected for inheritance scenarios — a Mist subclass
    /// needs access to protected nested types to call protected methods that reference them.
    /// </summary>
    private static void CreateNestedShells(
        Type declaringClrType,
        MistType declaringShell,
        string declaringClrFqn,
        string namespaceName,
        Dictionary<string, IMistType> wellKnownTypes,
        Dictionary<string, DeclaredType> shells,
        Dictionary<string, Type> clrTypes)
    {
        var nestedTypes = declaringClrType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

        foreach(var nestedClrType in nestedTypes)
        {
            // Only project public and protected nested types.
            if(!nestedClrType.IsNestedPublic && !nestedClrType.IsNestedFamily && !nestedClrType.IsNestedFamORAssem)
            {
                continue;
            }

            var nestedShell = TypeProjector.CreateShell(
                nestedClrType, namespaceName, declaringShell, declaringShell.Scope);

            // Key uses CLR-format: "DeclaringClrFQN.NestedClrName" to match resolver lookups.
            var nestedClrFqn = $"{declaringClrFqn}.{nestedClrType.Name}";

            // Skip if this FQN collides with a well-known type.
            if(wellKnownTypes.ContainsKey(nestedClrFqn))
            {
                continue;
            }

            if(shells.ContainsKey(nestedClrFqn))
            {
                continue;
            }

            shells[nestedClrFqn] = nestedShell;
            clrTypes[nestedClrFqn] = nestedClrType;

            // Wire up the parent relationship immediately (scope was set at creation). The
            // declaring type's scope reads NestedTypes back out, so this is the only record needed.
            declaringShell.NestedTypes.Add(nestedShell);

            // Recurse for deeper nesting (Outer.Middle.Inner).
            CreateNestedShells(nestedClrType, nestedShell, nestedClrFqn, namespaceName, wellKnownTypes, shells, clrTypes);
        }
    }

    /// <summary>
    /// Establishes type availability and parameter scopes before publishing members.
    /// </summary>
    internal static void ProjectTypes(
        Dictionary<string, DeclaredType> shells,
        Dictionary<string, Type> clrTypes,
        AmbientTypeResolver resolver,
        Dictionary<string, IMistType> wellKnownTypes)
    {
        AmbientTypeAvailability.Prepare(shells, clrTypes, wellKnownTypes, resolver);

        foreach(var (fqn, clrType) in clrTypes)
        {
            var shell = shells[fqn];

            switch(shell)
            {
                case EnumType enumType:
                    EnumProjector.Project(enumType, clrType, resolver);
                    break;

                case MemberType memberType:
                    BaseTypeProjector.Project(shell, clrType, resolver);
                    PropertyProjector.Project(memberType, clrType, resolver);
                    IndexerProjector.Project(memberType, clrType, resolver);
                    MethodProjector.Project(memberType, clrType, resolver);
                    OperatorProjector.Project(memberType, clrType, resolver);
                    AttributeProjector.Project(clrType, memberType.AttributesList, memberType.Scope, resolver);
                    SentinelAttributes.ApplyTypeAttributes(clrType, memberType.AttributesList);

                    if(shell is ConcreteType concreteType)
                    {
                        FieldProjector.Project(concreteType, clrType, resolver);
                        ConstructorProjector.Project(concreteType, clrType, resolver);
                        DisposableDetector.Apply(concreteType);
                    }

                    break;
            }
        }
    }
}