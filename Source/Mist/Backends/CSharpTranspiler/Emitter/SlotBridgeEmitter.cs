using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Reconciles a method whose return type is a parameterized associated type slot with the erased
/// signature the emitted interface declares for it.
/// <para>
/// The implementor keeps returning its own nested type, because that is what the call site named and
/// what its members are reached through; the interface asks for the slot's bound, because
/// <see cref="SlotLift"/> has no higher-kinded parameter to lift the slot to. An EXPLICIT
/// implementation forwarding to the public method satisfies both, and only in return position --
/// widening a result is sound where narrowing an argument is not.
/// </para>
/// </summary>
internal static class SlotBridgeEmitter
{
    public static void Emit(
        ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        var fillers = ConformanceAnalyzer.Fillers(type).ToList();

        foreach(var reached in SlotLift.Reached(type))
        {
            if(SlotLift.Definition(reached) is not AbstractType definition)
            {
                continue;
            }

            var substitution = TypeSubstitution.ForParameters(
                definition.TypeParametersList,
                reached is ConstructedType constructed ? constructed.TypeArguments : []);

            foreach(var method in definition.MethodsList)
            {
                if(method.Body is not null || !type.MethodsList.Any(own => own.Name == method.Name))
                {
                    continue;
                }

                ReportParameters(method, definition, diagnostics);

                if(SlotLift.Erased(method.ReturnType) is not { } erased)
                {
                    continue;
                }

                var interfaceName = TypeNameEmitter.Emit(SlotLift.Base(reached, type) ?? reached);
                var source = fillers.FirstOrDefault(filler => filler.Method is { } own && own.Name == method.Name
                    && SlotSignatures.Answers(method, own, substitution, filler.Substitution, interner: null))?.Method;
                var completion = AsyncPromotion.IsPromoted(source) ? ".GetAwaiter().GetResult()" : "";

                TypeEmitter.SeparateMember(writer, ref anyEmitted);
                writer.WriteLine(
                    $"{TypeNameEmitter.Emit(substitution.Apply(erased, interner: null))}"
                    + $" {interfaceName}.{method.Name}({Parameters(method, substitution)})"
                    + $" => this.{method.Name}({Arguments(method)}){completion};");
            }
        }
    }

    private static void ReportParameters(Method method, AbstractType definition, DiagnosticBag diagnostics)
    {
        foreach(var parameter in method.ParametersList)
        {
            if(SlotLift.Erased(parameter.Type) is not null && definition.DeclarationNode is { } node)
            {
                diagnostics.ReportAtNode(
                    BackendDiagnostics.ParameterizedTypeSlotInParameter, node, null,
                    parameter.Name,
                    TypeDisplay.Mist(definition),
                    method.Name,
                    TypeDisplay.Mist(SlotLift.Definition(parameter.Type)));
            }
        }
    }

    private static string Parameters(Method method, TypeSubstitution substitution)
    {
        return string.Join(", ", method.ParametersList.Select(parameter =>
            $"{TypeNameEmitter.Emit(substitution.Apply(parameter.Type, interner: null))} {parameter.Name}"));
    }

    private static string Arguments(Method method)
    {
        return string.Join(", ", method.ParametersList.Select(parameter => parameter.Name));
    }
}