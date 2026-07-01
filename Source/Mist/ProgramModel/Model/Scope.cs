namespace Mist.ProgramModel.Model;

/// <summary>
/// A name resolution scope in the program model. Scopes form a parent chain
/// (global → file → nested) and track imported namespaces for lookup.
/// </summary>
internal sealed class Scope
{
    private readonly List<string> importedNamespaces = [];

    public Scope(Scope? parent)
    {
        Parent = parent;
    }

    /// <summary>
    /// The parent scope, or null for the global scope.
    /// </summary>
    public Scope? Parent { get; }

    /// <summary>
    /// Namespaces imported into this scope (via using declarations or global usings).
    /// </summary>
    public IReadOnlyList<string> ImportedNamespaces => importedNamespaces;

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
}