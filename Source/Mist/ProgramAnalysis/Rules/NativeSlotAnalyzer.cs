using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// <see cref="NativeSlotFills"/> owns slot matching; collisions with non-slot members belong to
/// <see cref="DeclarationShapeAnalyzer"/>.
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