using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelType = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal sealed class AmbientTypeProviderV2 : IAmbientTypeSource
{
    private AmbientTypeResolver? resolver;
    private readonly MetadataLoadContext? metadataContext;
    private readonly DiagnosticBag diagnostics;

    public AmbientTypeProviderV2(
        ImmutableArray<LoadedAssemblyInfo> assemblies,
        MetadataLoadContext? metadataContext,
        DiagnosticBag diagnostics)
    {
        LoadedAssemblies = assemblies;
        this.metadataContext = metadataContext;
        this.diagnostics = diagnostics;
    }

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
        var wellKnownTypes = ResolveWellKnownTypes(programModel);
        NameIntrinsicRuntimeTypes(programModel);
        NameIntrinsicsByClrName(programModel, wellKnownTypes);

        var (shells, clrTypes) = CreateShells(wellKnownTypes);

        resolver = new AmbientTypeResolver(
            shells, wellKnownTypes, programModel.TypeInterner, diagnostics, clrName => DeclaredByClrName(programModel, clrName));
        ProjectTypes(shells, clrTypes, resolver, wellKnownTypes);

        // This loop is the single funnel for every projected shell, nested types included, so it is
        // where they are attributed to the ambient assembly. Well-known types are absent from 'shells'
        // -- they are Standard Library types declared in Mist source and keep the standard library
        // assembly they were registered with.
        foreach(var (_, shell) in shells)
        {
            shell.Assembly = MistAssembly.Ambient;
            _ = programModel.GetOrAddNamespace(shell.NamespaceName);
            _ = programModel.RegisterType(shell);
        }

        // Runs after every type is registered so receiver types resolve.
        ExtensionProjector.ProjectExtensions(clrTypes, resolver, programModel);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        metadataContext?.Dispose();
    }

    private static MistType? DeclaredByClrName(ProgramModelType programModel, string clrName)
    {
        var mistName = clrName.Contains('`')
            ? string.Join(".", clrName.Split('.').Select(NameFormatTranslator.ClrNameToMistName))
            : clrName;

        return programModel.TypesByFQN.GetValueOrDefault(mistName) is MistType { Assembly.HasMistSource: true } declared
            ? declared
            : null;
    }

    private AmbientTypeResolver ProjectingResolver()
    {
        return resolver ?? throw new InvalidOperationException(
            "ICE: an ambient member was judged before BuildAndRegister projected the ambient types.");
    }

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

    private static void NameIntrinsicRuntimeTypes(ProgramModelType programModel)
    {
        foreach(var mistName in ClrSpelling.MistNames)
        {
            if(programModel.TypesByFQN.TryGetValue(mistName, out var type) && type is DeclaredType declared)
            {
                declared.ClrMetadataName = ClrSpelling.MetadataName(declared);
            }
        }
    }

    // An entry generic on either side is left out: the CLR and Mist arities differ for some families (a
    // span's is one short, a bare System.Action is a Mist pack), so no one name answers a written use.
    private static void NameIntrinsicsByClrName(ProgramModelType programModel, Dictionary<string, IMistType> wellKnownTypes)
    {
        foreach(var (clrFqn, intrinsic) in wellKnownTypes)
        {
            if(!clrFqn.Contains('`') && !intrinsic.FullyQualifiedName.Contains('<'))
            {
                programModel.RegisterClrName(clrFqn, intrinsic);
            }
        }
    }

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

                if(wellKnownTypes.ContainsKey(fqn))
                {
                    continue;
                }

                // FIRST DEFINITION WINS, SILENTLY. Type forwarding is excluded above, so what reaches
                // here is the same name genuinely defined twice, which real package sets do -- a test
                // platform's own assemblies among them -- and leaves the user nothing to remove.
                if(shells.ContainsKey(fqn))
                {
                    continue;
                }

                var shell = TypeProjector.CreateShell(clrType, ns);
                shells[fqn] = shell;
                clrTypes[fqn] = clrType;

                CreateNestedShells(clrType, shell, fqn, ns, wellKnownTypes, shells, clrTypes);
            }
        }

        return (shells, clrTypes);
    }

    /// <summary>
    /// Protected nested types are projected too: a Mist subclass needs them to call the protected
    /// methods that reference them.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
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
            if(!nestedClrType.IsNestedPublic && !nestedClrType.IsNestedFamily && !nestedClrType.IsNestedFamORAssem)
            {
                continue;
            }

            var nestedShell = TypeProjector.CreateShell(
                nestedClrType, namespaceName, declaringShell, declaringShell.Scope);

            // Matches the key AmbientTypeResolver.BuildFQN spells.
            var nestedClrFqn = $"{declaringClrFqn}.{nestedClrType.Name}";

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

            // The declaring type's scope reads NestedTypes back out, so this is the only record needed.
            declaringShell.NestedTypes.Add(nestedShell);

            CreateNestedShells(nestedClrType, nestedShell, nestedClrFqn, namespaceName, wellKnownTypes, shells, clrTypes);
        }
    }

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

        foreach(var (fqn, clrType) in clrTypes)
        {
            if(shells[fqn] is AbstractType derived)
            {
                NativeSlotOverrides.Project(derived, clrType, resolver);
            }
        }
    }
}