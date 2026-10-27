using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction.Scaffolding;

/// <summary>
/// Judges every using against the namespaces the program has. A file's own directive naming none is an
/// error at the directive; a project's implicit one is a warning, once per project and name, and is
/// withdrawn so the build can continue without it.
/// <para>
/// Runs once every source file and the ambient projection have registered their namespaces, because a
/// using may name one that only a later file, a referenced project or a CLR assembly declares, and
/// before any name is resolved, so that no file binds through a using its output will not carry.
/// </para>
/// </summary>
internal static class UsingValidation
{
    public static void Run(BuilderContext context)
    {
        var programModel = context.ProgramModel;

        foreach(var pending in context.PendingUsings)
        {
            if(!programModel.IsKnownNamespace(pending.NamespaceName))
            {
                context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.UndefinedNamespace, pending.Directive, null, new NameValue(pending.NamespaceName));
            }
        }

        foreach(var assembly in programModel.Assemblies)
        {
            WithdrawUndefinedGlobalUsings(assembly, context);
        }
    }

    private static void WithdrawUndefinedGlobalUsings(MistAssembly assembly, BuilderContext context)
    {
        var undefined = assembly.GlobalUsings
            .Where(name => !context.ProgramModel.IsKnownNamespace(name))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();

        if(undefined.IsEmpty)
        {
            return;
        }

        var scope = context.ScopeFor(assembly);

        foreach(var name in undefined)
        {
            var location = assembly.ProjectPath is { } path ? (Location)new ProjectLocation(path, null)
                : new ExternalLocation(assembly.Name, "global using");
            context.Diagnostics.ReportTyped(
                ProgramModelDiagnostics.GlobalUsingUndefinedNamespace, location,
                new NameValue(name), new NameValue(assembly.Name));

            scope.WithdrawImport(name);
        }

        assembly.GlobalUsings = [.. assembly.GlobalUsings.Where(name => !undefined.Contains(name))];
    }
}