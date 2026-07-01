using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Processes compilation units (files) by creating the file scope and delegating
/// to specialized builders. One instance is created per compilation and reused
/// across all files.
/// </summary>
internal sealed class CompilationUnitBuilder
{
    private readonly BuilderContext context;
    private readonly NamespaceBuilder namespaceBuilder;
    private readonly TypeBuilder typeBuilder;

    public CompilationUnitBuilder(BuilderContext context)
    {
        this.context = context;
        namespaceBuilder = new NamespaceBuilder(context);
        typeBuilder = new TypeBuilder(context);
    }

    /// <summary>
    /// Processes a compilation unit: creates the file scope, resolves namespaces
    /// and imports, and registers type declarations.
    /// Returns the file scope, or null if namespace validation produced fatal errors.
    /// </summary>
    public Scope? Build(CompilationUnit unit)
    {
        var ast = (CompilationUnitNode)unit.Ast!;
        var sourceText = unit.Source;
        var namespaceRoot = unit.NamespaceRoot!;
        var projectFolder = unit.ProjectFolder!;

        // Create the file scope (parent = global).
        var fileScope = new Scope(context.GlobalScope);

        // Validate, resolve, and populate namespace + imports.
        var success = namespaceBuilder.Build(fileScope, ast, sourceText, namespaceRoot, projectFolder);
        if(!success)
        {
            return null;
        }

        // Register type declarations.
        typeBuilder.Build(fileScope, ast);

        return fileScope;
    }
}