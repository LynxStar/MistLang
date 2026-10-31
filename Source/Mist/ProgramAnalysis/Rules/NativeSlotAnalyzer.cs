using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The refusals of clr-slot-filling Requirements 3 and 4 that are not redeclarations: a required CLR
/// slot nothing fills, a member colliding with a sealed slot, and a member that repeats a slot's name and
/// parameters but not its shape. What fills what is <see cref="NativeSlotFills"/>'s; a collision with a
/// member that is no slot is <see cref="DeclarationShapeAnalyzer"/>'s redeclaration.
/// </summary>
internal sealed class NativeSlotAnalyzer : IProgramAnalyzer
{
    public string Id => "native-slot-filling";

    public void Analyze(AnalysisContext context)
    {
        var interner = context.Model.TypeInterner;

        foreach(var type in context.Types)
        {
            if(type is not ConcreteType { DeclarationNode: { } node } concrete)
            {
                continue;
            }

            foreach(var (slot, ancestor, inheritance) in NativeSlotFills.Unfilled(concrete, interner))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.NativeSlotNotFilled, node, [],
                    DiagnosticSnapshot.Type(concrete), new NameValue(slot.Display(inheritance, interner)), DiagnosticSnapshot.Type(ancestor));
            }

            foreach(var collision in NativeSlotFills.Collisions(concrete, interner))
            {
                var at = collision.Member.DeclarationNode!;
                var slot = new NameValue(collision.Against.Display(collision.Inheritance, interner));

                if(collision.Kind == NativeSlotKind.Sealed)
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.NativeSlotSealed, at, [],
                        DiagnosticSnapshot.Type(concrete), new NameValue(collision.Member.Name), slot, DiagnosticSnapshot.Type(collision.Ancestor));
                }
                else if(collision is { Mismatch: { } mismatch, Declared: { } declared })
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.NativeSlotShape, at, [],
                        DiagnosticSnapshot.Type(concrete), new NameValue(collision.Member.Name), slot, DiagnosticSnapshot.Type(collision.Ancestor),
                        new EnumValue(mismatch), new NameValue(declared));
                }
            }
        }
    }
}