using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A name resolution scope in the program model. Scopes form a parent chain
/// (global → file → nested) and track imported namespaces for lookup.
/// </summary>
internal sealed class Scope
{
    private readonly List<string> importedNamespaces = [];
    private readonly Dictionary<string, IMistType> localTypes = [];

    public Scope(Scope? parent)
    {
        Parent = parent;
    }

    /// <summary>
    /// The parent scope, or null for the global scope.
    /// </summary>
    public Scope? Parent { get; }

    /// <summary>
    /// The namespace this scope belongs to. For a file scope this is the file's
    /// effective namespace; for nested scopes (block namespaces) it is the block's
    /// namespace. Null for the global scope.
    /// </summary>
    public string? Namespace { get; private set; }

    /// <summary>
    /// Namespaces imported into this scope (via using declarations or global usings).
    /// </summary>
    public IReadOnlyList<string> ImportedNamespaces => importedNamespaces;

    /// <summary>
    /// Types directly visible by simple name in this scope. For a type's scope,
    /// this holds its direct nested types (children visible without qualification).
    /// </summary>
    public IReadOnlyDictionary<string, IMistType> LocalTypes => localTypes;

    /// <summary>
    /// Sets the namespace this scope belongs to.
    /// </summary>
    public void SetNamespace(string namespaceName)
    {
        Namespace = namespaceName;
    }

    /// <summary>
    /// Adds a namespace to this scope's import list.
    /// </summary>
    public void AddImportedNamespace(string namespaceName)
    {
        if(!importedNamespaces.Contains(namespaceName))
        {
            importedNamespaces.Add(namespaceName);
        }
    }

    /// <summary>
    /// Registers a type as directly visible by simple name in this scope.
    /// Used for nested types: a parent type's scope holds its children.
    /// </summary>
    public void AddLocalType(string name, IMistType typeDef)
    {
        localTypes[name] = typeDef;
    }
}