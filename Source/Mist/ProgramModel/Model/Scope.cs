using Mist.Common.Diagnostics;
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
    private readonly Dictionary<string, LocalVariable> localVariables = [];
    private readonly Dictionary<string, List<Method>> localMethods = [];

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
    public string? Namespace { get; internal set; }

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
    /// Local variables and parameters directly declared in this scope.
    /// </summary>
    public IReadOnlyDictionary<string, LocalVariable> LocalVariables => localVariables;

    /// <summary>
    /// Methods directly declared in this scope (grouped by name for overloads).
    /// </summary>
    public IReadOnlyDictionary<string, List<Method>> LocalMethods => localMethods;

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

    /// <summary>
    /// Registers a method in this scope. Multiple methods with the same name
    /// are collected as a method group (overloads).
    /// </summary>
    public void AddMethod(Method method)
    {
        if(!localMethods.TryGetValue(method.Name, out var group))
        {
            group = [];
            localMethods[method.Name] = group;
        }

        group.Add(method);
    }

    /// <summary>
    /// Registers a local variable (or parameter alias) in this scope. Reports an
    /// error diagnostic if a variable with the same name already exists directly in
    /// this scope.
    /// </summary>
    public void RegisterLocal(LocalVariable local, DiagnosticBag diagnostics)
    {
        if(!localVariables.TryAdd(local.Name, local))
        {
            diagnostics.Add(new Diagnostic(
                ProgramModelDiagnostics.DuplicateLocalVariable.Id,
                ProgramModelDiagnostics.DuplicateLocalVariable.DefaultSeverity,
                ProgramModelDiagnostics.DuplicateLocalVariable.Stage,
                string.Format(ProgramModelDiagnostics.DuplicateLocalVariable.MessageTemplate, local.Name),
                Location: default,
                CompilationUnitPath: "<unknown>",
                AnalyzerName: null));
        }
    }

    /// <summary>
    /// Looks up a local variable or parameter by name, walking the parent chain.
    /// Returns null if no local with that name is in scope.
    /// </summary>
    public LocalVariable? LookupLocal(string name)
    {
        for(var scope = this; scope is not null; scope = scope.Parent)
        {
            if(scope.localVariables.TryGetValue(name, out var local))
            {
                return local;
            }
        }

        return null;
    }
}