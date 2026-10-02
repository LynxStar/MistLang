using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

internal static class RefinementContracts
{
    public static DiagnosticDescriptor? ReturnFinding(IMistType produced, IMistType promised, bool byReference = false, Admitted? lifted = null)
    {
        return (byReference ? ContractImplication.Invariant(produced, promised) : ContractImplication.Proves(produced, promised, lifted))
            ? null : ProgramAnalysisDiagnostics.RefinementReturnUnproven;
    }

    public static DiagnosticDescriptor? ParameterFinding(IMistType promised, IMistType demanded, bool byReference = false)
    {
        var proven = byReference ? ContractImplication.Invariant(promised, demanded)
            : RealRequirement.OfType(demanded) is { } realDemanded
            ? (RealRequirement.OfType(promised) ?? RealSet.Everything(realDemanded.Kind)).IsSubsetOf(realDemanded)
            : Requirement.OfType(demanded) is { } required
            ? (Requirement.OfType(promised)?.Values ?? Admitted.Everything).IsSubsetOf(required.Values)
                && (required.Bounds.Length == 0 || ContractImplication.Proves(promised, demanded))
            : ContractImplication.Proves(promised, demanded);
        return proven ? null : ProgramAnalysisDiagnostics.ConstraintStrengthened;
    }

    public static DiagnosticDescriptor? SetterFinding(IMistType promised, IMistType demanded)
    {
        return ContractImplication.Proves(promised, demanded) ? null : ProgramAnalysisDiagnostics.ConstraintStrengthened;
    }

    public static void Judge(AnalysisContext context, ConcreteType concrete, LiftedReturns lifted)
    {
        if(concrete.DeclarationNode is not { } at)
        {
            return;
        }

        var fillers = ConformanceAnalyzer.Fillers(concrete).ToList();

        foreach(var (abstractType, substitution) in ConformanceAnalyzer.Reached(concrete.BaseAbstractTypes, context.Model.TypeInterner))
        {
            Judge(context, concrete, abstractType, substitution, fillers, null, at, lifted);
        }

        var reached = concrete.BaseAbstractTypes.Select(type => type.FullyQualifiedName).ToHashSet(StringComparer.Ordinal);

        foreach(var conformance in concrete.ConformancesList)
        {
            if(!reached.Add(conformance.AbstractType.FullyQualifiedName))
            {
                continue;
            }

            foreach(var (abstractType, substitution) in ConformanceAnalyzer.Reached([conformance.AbstractType], context.Model.TypeInterner))
            {
                Judge(context, concrete, abstractType, substitution, fillers, conformance, conformance.DeclarationNode, lifted);
            }
        }
    }

    private static void Judge(
        AnalysisContext context, ConcreteType concrete, AbstractType abstractType, TypeSubstitution substitution,
        List<ConformanceAnalyzer.Filler> fillers, ConformanceDeclaration? mappings, SyntaxNode at, LiftedReturns lifted)
    {
        var interner = context.Model.TypeInterner;

        foreach(var slot in abstractType.MethodsList.Where(method => !method.Modifiers.IsStatic))
        {
            if(ConformanceAnswers.Method(slot, substitution, fillers, mappings, interner) is not { } answer)
            {
                continue;
            }

            var filler = answer.Member;
            var node = mappings is null && filler.DeclaringType == concrete ? filler.DeclarationNode ?? at : at;
            var produced = answer.Source.Apply(filler.ReturnType, interner);
            var promised = answer.Target.Apply(slot.ReturnType, interner);

            if(ReturnFinding(produced, promised, slot.Modifiers.IsRef || filler.Modifiers.IsRef, lifted.Of(filler)) is not null)
            {
                ReportReturn(context, filler.Name, produced, promised, abstractType, node);
            }

            JudgeParameters(context, slot, answer, node);
        }

        foreach(var slot in abstractType.PropertiesList.Where(property => !property.Modifiers.IsStatic))
        {
            if(ConformanceAnswers.Property(slot, substitution, fillers, mappings, interner) is not { } answer)
            {
                continue;
            }

            var filler = answer.Member;
            var node = mappings is null && filler.DeclaringType == concrete ? filler.DeclarationNode ?? at : at;
            var produced = answer.Source.Apply(filler.Type, interner);
            var promised = answer.Target.Apply(slot.Type, interner);

            if(slot.Getter is not null && filler.Getter is not null
                && ReturnFinding(produced, promised, slot.Modifiers.IsRef || filler.Modifiers.IsRef, lifted.Of(filler)) is not null)
            {
                ReportReturn(context, filler.Name, produced, promised, abstractType, node);
            }

            if(slot.Setter is not null && filler.Setter is not null && SetterFinding(promised, produced) is not null)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ConstraintStrengthened, node, [],
                    filler.Name, "value", TypeDisplay.Mist(promised), TypeDisplay.Mist(produced));
            }
        }
    }

    private static void JudgeParameters(
        AnalysisContext context, Method slot, ConformanceAnswers.MethodAnswer answer, SyntaxNode at)
    {
        var filler = answer.Member;
        var interner = context.Model.TypeInterner;

        var promisedParameters = ValuePack.Expand(slot.ParametersList, answer.Target, interner);

        for(var i = 0; i < promisedParameters.Count && i < filler.ParametersList.Count; i++)
        {
            var demandedType = answer.Source.Apply(filler.ParametersList[i].Type, interner);
            var promisedType = promisedParameters[i].Type;

            if(ParameterFinding(promisedType, demandedType, promisedParameters[i].Modifier.IsRef || filler.ParametersList[i].Modifier.IsRef) is null)
            {
                continue;
            }

            if(promisedParameters[i].Modifier.IsRef || filler.ParametersList[i].Modifier.IsRef)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ConstraintStrengthened, at, [],
                    filler.Name, filler.ParametersList[i].Name, TypeDisplay.Mist(promisedType), TypeDisplay.Mist(demandedType));

                continue;
            }

            if(RealRequirement.OfType(demandedType) is { } realDemanded)
            {
                var realPromised = RealRequirement.OfType(promisedType) ?? RealSet.Everything(realDemanded.Kind);

                context.Report(
                    ProgramAnalysisDiagnostics.ConstraintStrengthened, at, [],
                    filler.Name, filler.ParametersList[i].Name, realPromised, realDemanded);

                continue;
            }

            if(Requirement.OfType(demandedType) is not { } demanded)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ConstraintStrengthened, at, [],
                    filler.Name, filler.ParametersList[i].Name, TypeDisplay.Mist(promisedType), TypeDisplay.Mist(demandedType));

                continue;
            }

            var promised = Requirement.OfType(promisedType) ?? Requirement.Anything;

            context.Report(
                ProgramAnalysisDiagnostics.ConstraintStrengthened, at, [],
                filler.Name, filler.ParametersList[i].Name,
                demanded.Bounds.Length > 0 ? RefinementFindings.Describe(promised) : promised.Values.ToString(),
                demanded.Bounds.Length > 0 ? RefinementFindings.Describe(demanded) : demanded.Values.ToString());
        }
    }

    private static void ReportReturn(
        AnalysisContext context, string member, IMistType produced, IMistType promised, AbstractType abstractType, SyntaxNode at)
    {
        context.Report(
            ProgramAnalysisDiagnostics.RefinementReturnUnproven, at, [],
            member, TypeDisplay.Mist(produced), TypeDisplay.Mist(promised), TypeDisplay.Mist(abstractType));
    }
}