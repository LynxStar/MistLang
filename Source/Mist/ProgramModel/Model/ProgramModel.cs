using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

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
    /// Every type that closes a parameterized constructor constraint, collected as construction and
    /// resolution encounter them.
    /// </summary>
    internal ConstraintClosureRegistry ConstructorConstraintClosures { get; private set; } = new();

    internal VariadicUseRegistry VariadicUses { get; private set; } = new();

    internal GenericCallRegistry GenericCalls { get; private set; } = new();

    internal TaskDropRegistry TaskDrops { get; private set; } = new();

    internal OptionalApplicationRegistry OptionalApplications { get; private set; } = new();

    internal TypeNamingRegistry TypeNamings { get; private set; } = new();

    /// <summary>
    /// Runs <paramref name="trial"/> against empty registries and restores these after, so a binding
    /// made only to answer a question -- what a lambda returns under one candidate's parameter types --
    /// records nothing a consumer reads. Nothing reads a registry during binding, which is what makes
    /// swapping them safe.
    /// </summary>
    internal T Discarding<T>(Func<T> trial)
    {
        var kept = (ConstructorConstraintClosures, VariadicUses, GenericCalls, TaskDrops, OptionalApplications, TypeNamings);
        (ConstructorConstraintClosures, VariadicUses, GenericCalls, TaskDrops, OptionalApplications, TypeNamings) =
            (new(), new(), new(), new(), new(), new());

        try
        {
            return trial();
        }
        finally
        {
            (ConstructorConstraintClosures, VariadicUses, GenericCalls, TaskDrops, OptionalApplications, TypeNamings) = kept;
        }
    }

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
    internal MistAssembly AddSelfAssembly(
        string name, ImmutableArray<string> globalUsings = default, string? entryPointSetting = null, bool isLibrary = false)
    {
        SelfAssembly = new MistAssembly(name, MistAssemblyKind.Self, projectPath: null, globalUsings)
        {
            EntryPointSetting = entryPointSetting,
            IsLibrary = isLibrary,
        };
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
    internal MistAssembly AddReferenceAssembly(
        string name,
        string projectPath,
        ImmutableArray<string> globalUsings = default,
        string? entryPointSetting = null,
        bool isLibrary = false)
    {
        if(referencesByProjectPath.TryGetValue(projectPath, out var existing))
        {
            return existing;
        }

        var assembly = new MistAssembly(name, MistAssemblyKind.Reference, projectPath, globalUsings)
        {
            EntryPointSetting = entryPointSetting,
            IsLibrary = isLibrary,
        };
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

    internal void ReplaceType(IMistType replaced, IMistType replacement)
    {
        typesByFQN[replaced.FullyQualifiedName] = replacement;
    }

    /// <summary>
    /// A type by namespace and simple name, or null when that namespace holds no such type. An
    /// empty namespace names the global one, where the simple name IS the fully qualified name.
    /// </summary>
    internal IMistType? FindType(string namespaceName, string simpleName)
    {
        var fqn = string.IsNullOrEmpty(namespaceName)
            ? simpleName
            : $"{namespaceName}.{simpleName}"
            ;

        return typesByFQN.TryGetValue(fqn, out var type)
            ? type
            : null
            ;
    }

    /// <summary>
    /// One qualification step off a namespace: <c>namespaceName.memberName</c>. A namespace holds
    /// only types and further namespaces, so those are the only two answers.
    /// <para>
    /// Answered here rather than by a scope because no scope chain is involved -- the namespace to
    /// look in is already known, leaving nothing to resolve. No diagnostic either: the caller
    /// decides whether an unresolved qualification is an error.
    /// </para>
    /// </summary>
    internal LookupResult LookupQualified(string namespaceName, string memberName)
    {
        if(FindType(namespaceName, memberName) is { } type)
        {
            return new TypeResult(type);
        }

        var qualified = $"{namespaceName}.{memberName}";

        if(IsKnownNamespace(qualified))
        {
            return new NamespaceResult(qualified);
        }

        return new NotFoundResult();
    }

    /// <summary>
    /// Whether the name denotes a namespace. Registration records leaf namespaces only, so a name
    /// also counts if it is a proper prefix of a registered one -- <c>A.B.C</c> is what makes
    /// <c>A</c> and <c>A.B</c> namespaces.
    /// <para>
    /// Ambient namespaces are registered by the ambient projection, so the answer covers them only
    /// once it has run.
    /// </para>
    /// </summary>
    internal bool IsKnownNamespace(string name)
    {
        if(string.IsNullOrEmpty(name))
        {
            return false;
        }

        if(namespaces.ContainsKey(name))
        {
            return true;
        }

        var prefix = name + ".";
        foreach(var registered in namespaces.Keys)
        {
            if(registered.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}