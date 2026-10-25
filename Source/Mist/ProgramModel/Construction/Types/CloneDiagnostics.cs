using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <summary>
/// Runs a step -- building, binding, analysing -- over each newtype's clone with what it reports withheld,
/// after the same step ran over every source, then reports only what the clone adds. A clone is its
/// source's own syntax bound again, so every mistake in the source would otherwise be reported twice at
/// one place; what the source does not report is the renaming's doing, and belongs to the newtype
/// (newtypes spec Requirement 3.6). Only errors are carried over: a warning about the source's code is the
/// source author's to act on, not the newtype's.
/// </summary>
internal static class CloneDiagnostics
{
    public static void Run(IEnumerable<ConcreteType> clones, Action<ConcreteType> step, DiagnosticBag bag)
    {
        foreach(var clone in clones)
        {
            var newtypeLocation = DiagnosticHelper.ResolveLocation(clone.NewtypeDeclaration!).Location;

            foreach(var withheld in bag.Withhold(() => step(clone)))
            {
                if(newtypeLocation.Contains(withheld.Location))
                {
                    bag.Add(withheld);
                    continue;
                }

                if(withheld.Severity != DiagnosticSeverity.Error || bag.Contains(withheld))
                {
                    continue;
                }

                bag.ReportAtNode(
                    ProgramModelDiagnostics.NewtypeCloneInvalid,
                    clone.NewtypeDeclaration!,
                    null,
                    TypeDisplay.Mist(clone),
                    TypeDisplay.Mist(clone.CloneSourceType ?? clone.CloneSource!),
                    MemberAt(clone, withheld.Location) ?? TypeDisplay.Mist(clone.CloneSource!),
                    withheld.Message);
            }
        }
    }

    private static string? MemberAt(ConcreteType clone, Location location)
    {
        var members = clone.FieldsList.Select(field => (field.Name, field.DeclarationNode))
            .Concat(clone.PropertiesList.Select(property => (property.Name, property.DeclarationNode)))
            .Concat(clone.IndexersList.Select(indexer => ("this[]", indexer.DeclarationNode)))
            .Concat(clone.EnumerateDelegates().Select(method => (method.Name, method.DeclarationNode)));

        foreach(var (name, node) in members)
        {
            if(node is not null && DiagnosticHelper.ResolveLocation(node).Location.Contains(location))
            {
                return name;
            }
        }

        return null;
    }
}