using System.Diagnostics.CodeAnalysis;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Concrete implementation of <see cref="IProgramModel"/>.
/// Populated by the builder, then exposed as read-only through the interface.
/// </summary>
// Suppressed: the class name matches the feature namespace by design.
[SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Intentional - the class represents the concept that the namespace is named after")]
public sealed class ProgramModel : IProgramModel
{
    private readonly Dictionary<string, MistNamespace> namespaces = [];
    private readonly Dictionary<string, IMistType> typesByFQN = [];
    private readonly List<MistAssembly> assemblies = [];
    private readonly Dictionary<string, MistAssembly> referencesByProjectPath = [];

    public ProgramModel()
    {
        // Neither is declared by a compilation unit, so both are bootstrapped rather than
        // discovered. They are shared instances because nothing distinguishes one
        // compilation's ambient or synthetic types from another's.
        assemblies.Add(MistAssembly.Ambient);
        assemblies.Add(MistAssembly.Synthetic);
    }

    /// <summary>
    /// Every assembly participating in this compilation: the compiled project, each
    /// referenced project, the standard library, and the ambient and synthetic buckets.
    /// </summary>
    public IReadOnlyList<MistAssembly> Assemblies => assemblies;

    /// <summary>
    /// The assembly for the project being compiled, once <c>AssemblyRegistration</c> has
    /// established it.
    /// </summary>
    public MistAssembly? SelfAssembly { get; private set; }

    /// <summary>
    /// The standard library assembly. Null for compilations that load no standard library,
    /// which in practice means hand-assembled test compilations.
    /// </summary>
    public MistAssembly? StandardAssembly { get; private set; }

    /// <summary>
    /// All registered namespaces, keyed by fully qualified name.
    /// </summary>
    public IReadOnlyDictionary<string, MistNamespace> Namespaces => namespaces;

    /// <summary>
    /// All registered types, keyed by fully qualified name.
    /// This is the primary type registry for name resolution.
    /// </summary>
    public IReadOnlyDictionary<string, IMistType> TypesByFQN => typesByFQN;

    /// <summary>
    /// Interns synthetic types (unions, constructed types) so that structurally
    /// identical type references share a single instance throughout the model's lifetime.
    /// </summary>
    internal TypeInterner TypeInterner { get; } = new();

    /// <summary>
    /// Retroactive conformance declarations collected during Phase 2c.
    /// </summary>
    internal List<ConformanceDeclaration> ConformancesList { get; } = [];

    IReadOnlyDictionary<string, IMistType> IProgramModel.TypesByFQN => typesByFQN;

    IReadOnlyList<IConformanceDeclaration> IProgramModel.Conformances => ConformancesList;

    IReadOnlyList<IAssembly> IProgramModel.Assemblies => assemblies;

    IAssembly? IProgramModel.SelfAssembly => SelfAssembly;

    /// <summary>
    /// Establishes the assembly for the project being compiled. Called once, before any
    /// type is registered.
    /// </summary>
    internal MistAssembly AddSelfAssembly(string name)
    {
        SelfAssembly = new MistAssembly(name, MistAssemblyKind.Self);
        assemblies.Add(SelfAssembly);
        return SelfAssembly;
    }

    /// <summary>
    /// Establishes the standard library assembly. Called once, before any type is
    /// registered, and only for compilations that loaded the standard library.
    /// </summary>
    internal MistAssembly AddStandardAssembly()
    {
        StandardAssembly = new MistAssembly(MistAssembly.StandardName, MistAssemblyKind.Standard);
        assemblies.Add(StandardAssembly);
        return StandardAssembly;
    }

    /// <summary>
    /// Records a referenced Mist project's assembly, keyed on the canonical
    /// <c>mist.toml</c> path that identifies the project. Two projects sharing a display
    /// name remain distinct assemblies.
    /// </summary>
    internal MistAssembly AddReferenceAssembly(string name, string projectPath)
    {
        if(referencesByProjectPath.TryGetValue(projectPath, out var existing))
        {
            return existing;
        }

        var assembly = new MistAssembly(name, MistAssemblyKind.Reference, projectPath);
        referencesByProjectPath[projectPath] = assembly;
        assemblies.Add(assembly);
        return assembly;
    }

    /// <summary>
    /// Returns the assembly for a referenced project, or null when the compilation
    /// declared no such reference.
    /// </summary>
    internal MistAssembly? FindReferenceAssembly(string projectPath)
    {
        return referencesByProjectPath.TryGetValue(projectPath, out var assembly)
            ? assembly
            : null;
    }

    /// <summary>
    /// Gets an existing namespace or creates and registers a new one.
    /// Multiple files may declare the same namespace, so this deduplicates.
    /// </summary>
    public MistNamespace GetOrAddNamespace(string fullyQualifiedName)
    {
        if(namespaces.TryGetValue(fullyQualifiedName, out var existing))
        {
            return existing;
        }

        var newNamespace = new MistNamespace(fullyQualifiedName);
        namespaces[fullyQualifiedName] = newNamespace;
        return newNamespace;
    }

    /// <summary>
    /// Registers a type in the flat FQN dictionary. Returns false if a type
    /// with the same FQN is already registered (duplicate).
    /// </summary>
    internal bool RegisterType(IMistType type)
    {
        var fqn = type.FullyQualifiedName;
        if(typesByFQN.ContainsKey(fqn))
        {
            return false;
        }

        typesByFQN[fqn] = type;
        return true;
    }
}