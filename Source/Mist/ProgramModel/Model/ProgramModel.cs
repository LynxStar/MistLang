using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Populated by the builder; consumers read it through <see cref="IProgramModel"/>.
/// </summary>
// Suppressed: the class name matches the feature namespace by design.
[SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Intentional - the class represents the concept that the namespace is named after")]
public sealed class ProgramModel : IProgramModel
{
    private readonly Dictionary<string, MistNamespace> namespaces = [];
    private readonly Dictionary<string, IMistType> typesByFQN = [];
    private readonly Dictionary<string, IMistType> intrinsicsByClrName = [];
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

    public IReadOnlyList<MistAssembly> Assemblies => assemblies;

    public MistAssembly? SelfAssembly { get; private set; }

    /// <summary>
    /// Null only for a compilation that loads no standard library: in practice, a hand-assembled test.
    /// </summary>
    public MistAssembly? StandardAssembly { get; private set; }

    public IReadOnlyDictionary<string, MistNamespace> Namespaces => namespaces;

    public IReadOnlyDictionary<string, IMistType> TypesByFQN => typesByFQN;

    /// <summary>
    /// Interns synthetic types (unions, constructed types) so structurally identical references share
    /// one instance.
    /// </summary>
    internal TypeInterner TypeInterner { get; } = new();

    internal ConstraintClosureRegistry ConstructorConstraintClosures { get; private set; } = new();

    internal VariadicUseRegistry VariadicUses { get; private set; } = new();

    internal GenericCallRegistry GenericCalls { get; private set; } = new();

    internal TaskDropRegistry TaskDrops { get; private set; } = new();

    internal OptionalApplicationRegistry OptionalApplications { get; private set; } = new();

    internal CompositeApplicationRegistry CompositeApplications { get; private set; } = new();

    internal TypeNamingRegistry TypeNamings { get; private set; } = new();

    /// <summary>
    /// Runs <paramref name="trial"/> against empty registries and restores these after, so a binding
    /// made only to answer a question -- what a lambda returns under one candidate's parameter types --
    /// records nothing a consumer reads. Nothing reads a registry during binding, which is what makes
    /// swapping them safe.
    /// </summary>
    internal T Discarding<T>(Func<T> trial)
    {
        var kept = (ConstructorConstraintClosures, VariadicUses, GenericCalls, TaskDrops, OptionalApplications, CompositeApplications, TypeNamings);
        (ConstructorConstraintClosures, VariadicUses, GenericCalls, TaskDrops, OptionalApplications, CompositeApplications, TypeNamings) =
            (new(), new(), new(), new(), new(), new(), new());

        try
        {
            return trial();
        }
        finally
        {
            (ConstructorConstraintClosures, VariadicUses, GenericCalls, TaskDrops, OptionalApplications, CompositeApplications, TypeNamings) = kept;
        }
    }

    internal List<ConformanceDeclaration> ConformancesList { get; } = [];

    IReadOnlyDictionary<string, IMistType> IProgramModel.TypesByFQN => typesByFQN;

    IReadOnlyList<IConformanceDeclaration> IProgramModel.Conformances => ConformancesList;

    IReadOnlyList<IAssembly> IProgramModel.Assemblies => assemblies;

    IAssembly? IProgramModel.SelfAssembly => SelfAssembly;

    /// <summary>
    /// Called once, before any type is registered.
    /// </summary>
    internal MistAssembly AddSelfAssembly(
        string name, ImmutableArray<string> globalUsings = default, string? entryPointSetting = null, bool isLibrary = false, string? projectPath = null)
    {
        SelfAssembly = new MistAssembly(name, MistAssemblyKind.Self, projectPath, globalUsings)
        {
            EntryPointSetting = entryPointSetting,
            IsLibrary = isLibrary,
        };
        assemblies.Add(SelfAssembly);
        return SelfAssembly;
    }

    /// <summary>
    /// Called once, before any type is registered.
    /// </summary>
    internal MistAssembly AddStandardAssembly()
    {
        StandardAssembly = new MistAssembly(MistAssembly.StandardName, MistAssemblyKind.Standard);
        assemblies.Add(StandardAssembly);
        return StandardAssembly;
    }

    /// <summary>
    /// Keyed on the canonical <c>mist.toml</c> path, so two projects sharing a display name remain
    /// distinct assemblies.
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

    internal MistAssembly? FindReferenceAssembly(string projectPath)
    {
        return referencesByProjectPath.TryGetValue(projectPath, out var assembly)
            ? assembly
            : null;
    }

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

    /// <summary>
    /// Names a Standard intrinsic by the CLR type it stands for, as the ambient provider's substitution
    /// does. Kept beside <see cref="TypesByFQN"/> rather than in it, because that registry enumerates each
    /// type once, and answered only by <see cref="FindQualifiedType"/>, so a using directive still reaches
    /// the intrinsic through Standard alone and the Mist-first import tier is untouched.
    /// </summary>
    internal void RegisterClrName(string clrName, IMistType intrinsic)
    {
        intrinsicsByClrName[clrName] = intrinsic;
    }

    internal IMistType? FindQualifiedType(string qualifiedName)
    {
        return FindType("", qualifiedName) ?? intrinsicsByClrName.GetValueOrDefault(qualifiedName);
    }

    internal void ReplaceType(IMistType replaced, IMistType replacement)
    {
        typesByFQN[replaced.FullyQualifiedName] = replacement;
    }

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
    /// A namespace holds only types and further namespaces, so those are the only two answers.
    /// Answered here rather than by a scope because the namespace to look in is already known, and
    /// without a diagnostic because the caller decides whether an unresolved qualification is an error.
    /// </summary>
    internal LookupResult LookupQualified(string namespaceName, string memberName)
    {
        var qualified = $"{namespaceName}.{memberName}";

        if(FindQualifiedType(qualified) is { } type)
        {
            return new TypeResult(type);
        }

        if(IsKnownNamespace(qualified))
        {
            return new NamespaceResult(qualified);
        }

        return new NotFoundResult();
    }

    /// <summary>
    /// Registration records leaf namespaces only, so a proper prefix of a registered name counts too.
    /// Ambient namespaces count only once the ambient projection has registered them.
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