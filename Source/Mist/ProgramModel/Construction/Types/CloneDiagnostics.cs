using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
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
            var newtype = DiagnosticHelper.ResolveLocation(clone.NewtypeDeclaration!);
            var sourceOrdinals = bag.Admitted.Select(occurrence => occurrence.Ordinal).ToHashSet();
            Withheld withheld;
            using(bag.ReplayOwnedFailures())
            {
                withheld = bag.Withhold(() => step(clone));
            }

            foreach(var occurrence in withheld.Occurrences)
            {
                if(sourceOrdinals.Contains(occurrence.Ordinal))
                {
                    continue;
                }

                if(newtype.Location.Contains(occurrence.Location))
                {
                    withheld.Replay(occurrence);
                    continue;
                }

                if(occurrence.Severity != DiagnosticSeverity.Error)
                {
                    continue;
                }

                bag.Admit(DiagnosticFactory.Create(
                    ProgramModelDiagnostics.NewtypeCloneInvalid,
                    newtype,
                    bag.Stage,
                    [
                        DiagnosticSnapshot.Type(clone),
                        DiagnosticSnapshot.Type(clone.CloneSourceType ?? clone.CloneSource!),
                        new NameValue(MemberAt(clone, occurrence.Location) ?? TypeDisplay.Mist(clone.CloneSource!)),
                    ],
                    cause: occurrence.Ordinal));
            }

            withheld.Discard();
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