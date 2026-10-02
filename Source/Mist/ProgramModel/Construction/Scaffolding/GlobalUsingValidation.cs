using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.ProgramModel.Construction.Scaffolding;

/// <summary>
/// Withdraws each implicit using that names no namespace, warning once per project and name.
/// <para>
/// Runs once every source file and the ambient projection have registered their namespaces, because a
/// project may import one that only a later file, a referenced project or a CLR assembly declares, and
/// before any name is resolved, so that no file binds through a using its output will not carry.
/// </para>
/// </summary>
internal static class GlobalUsingValidation
{
    public static void Run(BuilderContext context)
    {
        var programModel = context.ProgramModel;

        foreach(var assembly in programModel.Assemblies)
        {
            var undefined = assembly.GlobalUsings
                .Where(name => !programModel.IsKnownNamespace(name))
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray();

            if(undefined.IsEmpty)
            {
                continue;
            }

            var scope = context.ScopeFor(assembly);

            foreach(var name in undefined)
            {
                context.Diagnostics.Report(
                    ProgramModelDiagnostics.GlobalUsingUndefinedNamespace,
                    default,
                    assembly.ProjectPath ?? "<unknown>",
                    name,
                    assembly.Name);

                scope.WithdrawImport(name);
            }

            assembly.GlobalUsings = [.. assembly.GlobalUsings.Where(name => !undefined.Contains(name))];
        }
    }
}