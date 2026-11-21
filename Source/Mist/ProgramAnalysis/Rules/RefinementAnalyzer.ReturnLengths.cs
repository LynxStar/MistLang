using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The contract of an array's length where a method's return type closes it with one of the method's
/// parameters (runtime-dependent-values Requirement 8.9): proven at the signature from the facts a body
/// starts with, the parameter's declared type and the method's <c>where</c> clause, so a method with no
/// body owes it as well.
/// </summary>
internal sealed partial class RefinementAnalyzer
{
    private static IEnumerable<Method> ReturningLengths(AnalysisContext context)
    {
        return context.Types.OfType<IMethodHost>().Concat(context.Extensions)
            .SelectMany(host => host.MethodsList)
            .Where(method => method.DeclarationNode is { } declared && context.IsInSource(declared) && HeldLength(method.ReturnType) is not null);
    }

    private static ParameterValue? HeldLength(IMistType type)
    {
        return type switch
        {
            ParameterValue held => held,
            ConstructedType constructed => constructed.TypeArguments.Select(HeldLength).FirstOrDefault(held => held is not null),
            _ => null,
        };
    }

    public static bool Admits(ParameterValue held, TypeParameter length)
    {
        return length.Domain is not { } domain || DependentDomains.Primitive(domain) is null
            || DependentDomains.Forwards(OperatorAdmissibility.Underlying(held.Parameter.Type), domain);
    }

    private sealed partial class Proving
    {
        public void NeedReturnLength(Method method)
        {
            if(HeldLength(method.ReturnType) is not { } held
                || context.Model.TypesByFQN.GetValueOrDefault(WellKnownTypes.Array.FullyQualifiedName) is not IHasTypeParameters { TypeParametersList: [_, { IsDependent: true } length] })
            {
                return;
            }

            if(!Admits(held, length))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.DependentArgumentNotAssignable, held.Written, [],
                    new OptionalValue(SlotKind.Type, null), new OptionalValue(SlotKind.Name, new NameValue(held.Parameter.Name)),
                    DiagnosticSnapshot.Type(held.Parameter.Type), new NameValue(length.Name), DiagnosticSnapshot.Type(length.Domain!));
                return;
            }

            if(InstanceSlotRead.Contract(length) is not { } contract)
            {
                return;
            }

            var passed = new LocalReferenceExpression(held.Written, new LocalVariable(held.Parameter.Name, held.Parameter.Type) { Parameter = held.Parameter });
            Need(FactFlow.Nothing, passed, contract, passed);
        }
    }
}