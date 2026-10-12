using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Collected before anything is written: the member answering a nullable native slot, which CS8767 requires to
/// match, may be declared on a base that names no such slot and is written without the conformer in view.
/// </summary>
internal sealed class NativeSlotNullability
{
    [ThreadStatic]
    private static NativeSlotNullability? inScope;

    private readonly HashSet<Parameter> answering;

    private NativeSlotNullability(HashSet<Parameter> answering)
    {
        this.answering = answering;
    }

    public static IDisposable Enter(ProgramModelClass programModel)
    {
        return Enter(SourceTypes.Of(programModel).OfType<ConcreteType>(), programModel.TypeInterner);
    }

    public static IDisposable Enter(IEnumerable<ConcreteType> types, TypeInterner interner)
    {
        inScope = new NativeSlotNullability([.. types.SelectMany(type => Answering(type, interner))]);
        return new Scope();
    }

    public static bool Declares(Parameter parameter)
    {
        return parameter.ClrDeclaresNullable || (inScope is { } scope && scope.answering.Contains(parameter));
    }

    private static IEnumerable<Parameter> Answering(ConcreteType type, TypeInterner interner)
    {
        var reached = ConformanceAnalyzer.Reached(type.BaseAbstractTypes, interner, type)
            .Select(each => (each.Abstract, each.Substitution, Mappings: (ConformanceDeclaration?)null))
            .Concat(type.ConformancesList.SelectMany(conformance => ConformanceAnalyzer.Reached([conformance.AbstractType], interner, type)
                .Select(each => (each.Abstract, each.Substitution, Mappings: (ConformanceDeclaration?)conformance))));
        List<ConformanceAnalyzer.Filler>? fillers = null;

        foreach(var (definition, substitution, mappings) in reached)
        {
            foreach(var slot in definition.MethodsList)
            {
                if(slot.Modifiers.IsStatic || !slot.ParametersList.Exists(parameter => parameter.ClrDeclaresNullable))
                {
                    continue;
                }

                fillers ??= [.. ConformanceAnalyzer.Fillers(type)];

                if(ConformanceAnswers.Method(slot, substitution, fillers, mappings, interner)?.Member is not { } member
                    || member.ParametersList.Count != slot.ParametersList.Count)
                {
                    continue;
                }

                for(var i = 0; i < slot.ParametersList.Count; i++)
                {
                    if(slot.ParametersList[i].ClrDeclaresNullable)
                    {
                        yield return member.ParametersList[i];
                    }
                }
            }
        }
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            inScope = null;
        }
    }
}