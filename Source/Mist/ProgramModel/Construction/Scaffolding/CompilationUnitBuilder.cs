using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction.Scaffolding;

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
        typeBuilder.Build(fileScope, ast, ResolveAssembly(unit));

        return fileScope;
    }

    /// <summary>
    /// Determines which assembly a unit's declarations belong to. The unit carries two
    /// independent flags rather than one boundary, so they are folded here: stdlib units
    /// are the standard library, units with no origin project belong to the project being
    /// compiled, and the rest arrived through a reference.
    /// </summary>
    private MistAssembly ResolveAssembly(CompilationUnit unit)
    {
        var programModel = context.ProgramModel;

        if(unit.IsStdlib)
        {
            return programModel.StandardAssembly
                ?? throw new InvalidOperationException(
                    "ICE: a stdlib unit was processed but the compilation did not report "
                    + "loading the standard library.");
        }

        if(unit.OriginProjectPath is null)
        {
            return programModel.SelfAssembly
                ?? throw new InvalidOperationException(
                    "ICE: a compilation unit was processed before the self assembly was established.");
        }

        // Referenced assemblies come from the compilation's project list, which the driver
        // populates alongside the units themselves. A unit citing a project that is not
        // there means the two disagree.
        return programModel.FindReferenceAssembly(unit.OriginProjectPath)
            ?? throw new InvalidOperationException(
                $"ICE: unit '{unit.Source.Path}' cites referenced project "
                + $"'{unit.OriginProjectPath}', which is absent from the compilation's project list.");
    }
}