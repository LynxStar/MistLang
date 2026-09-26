using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A for loop with a clause that needs statements run ahead of it, which none of C#'s three clauses can hold. It
/// becomes its initializer and a <c>while (true)</c> that tests the condition at the top and runs the step after
/// the body, each clause with its statements in front of it. The step can follow the body only because Mist has
/// no <c>continue</c>: every way out of the body except its end also leaves the loop, as it leaves the for.
/// </summary>
internal static class ForLowering
{
    public static bool Applies(ForStatement loop)
    {
        var initializer = loop.Initializer switch
        {
            LocalDeclarationStatement declaration => UnitHoisting.Plan(declaration.Initializer),
            ExpressionStatement statement => UnitHoisting.Plan(statement.Expression, discarded: true),
            _ => UnitHoisting.Plan(null),
        };

        return !initializer.IsEmpty
            || !UnitHoisting.Plan(loop.Condition).IsEmpty
            || !UnitHoisting.Plan(loop.Iterator, discarded: true).IsEmpty
            ;
    }

    public static void Emit(ForStatement loop, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        using(writer.Block())
        {
            if(loop.Initializer is { } initializer)
            {
                BodyEmitter.EmitStatement(initializer, writer, diagnostics);
            }

            writer.WriteLine("while (true)");

            using(writer.Block())
            {
                if(loop.Condition is { } condition)
                {
                    GuardChain.EmitBreaks(GuardChain.Split(condition), writer, diagnostics);
                }

                using(writer.Block())
                {
                    BodyEmitter.EmitBlock(loop.Body, writer, diagnostics);
                }

                if(loop.Iterator is { } iterator)
                {
                    BodyEmitter.EmitSequenced(iterator, null, writer, diagnostics);
                }
            }
        }
    }
}