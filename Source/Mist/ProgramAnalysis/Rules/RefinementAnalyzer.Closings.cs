using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class RefinementAnalyzer
{
    private static void JudgeClosing(AnalysisContext context, TypeParameter parameter, IMistType argument, Storm.Runtime.SyntaxNode at)
    {
        if(DependentFinding(parameter, argument) is not { } finding)
        {
            return;
        }

        if(finding == ProgramAnalysisDiagnostics.DependentArgumentNotAssignable)
        {
            context.Report(
                finding, at, [],
                new OptionalValue(SlotKind.Type, argument is LiftedType ? null : DiagnosticSnapshot.Type(argument)),
                new OptionalValue(SlotKind.Name, argument is LiftedType lifted ? new NameValue(DependentDomains.Spell(lifted.Value)) : null),
                ArgumentDomain(context, argument),
                new NameValue(parameter.Name), DiagnosticSnapshot.Type(parameter.Domain!));
        }
        else if(finding == ProgramAnalysisDiagnostics.DependentForwardNotConvertible && argument is TypeParameter { Domain: { } from } source)
        {
            context.Report(
                finding, at, [], new NameValue(source.Name), DiagnosticSnapshot.Type(from),
                Owner(source), OwnerName(source), new NameValue(parameter.Name), DiagnosticSnapshot.Type(parameter.Domain!), Owner(parameter), OwnerName(parameter));
        }
        else if(Demanded(parameter) is { } required)
        {
            switch(argument)
            {
                case LiftedType lifted:
                    context.Report(finding, at, [], new NameValue(DependentDomains.Spell(lifted.Value)), new NameValue(required.Values.ToString()), new NameValue(parameter.Name));
                    break;

                case TypeParameter forwarded:
                    context.Report(
                        finding, at, [],
                        new NameValue(forwarded.Name), new NameValue(parameter.Name), new NameValue(Demanded(forwarded) is { } own ? $"'{own.Values}'" : "an unconstrained parameter"), new NameValue(required.Values.ToString()));
                    break;
            }
        }
    }

    private static TypeValue ArgumentDomain(AnalysisContext context, IMistType argument)
    {
        var spelling = argument is LiftedType lifted ? DependentDomains.TypeOfValue(lifted.Value) : WellKnownTypes.Type.Name;
        var known = spelling switch
        {
            "bool" => WellKnownTypes.Boolean,
            "char" => WellKnownTypes.Char,
            "int" => WellKnownTypes.Int32,
            "uint" => WellKnownTypes.UInt32,
            "long" => WellKnownTypes.Int64,
            "ulong" => WellKnownTypes.UInt64,
            _ => WellKnownTypes.Type,
        };

        return context.Model.TypesByFQN.TryGetValue(known.FullyQualifiedName, out var type)
            ? DiagnosticSnapshot.Type(type, spelling)
            : new TypeValue(known.FullyQualifiedName, spelling);
    }

    public static MistDiagnostic? DependentFinding(TypeParameter parameter, IMistType argument)
    {
        if(DomainFinding(parameter, argument) is { } mismatch)
        {
            return mismatch;
        }

        if(Demanded(parameter) is not { } required || required.Values.IsEverything)
        {
            return null;
        }

        return argument switch
        {
            LiftedType lifted when Known(lifted) is { } values && !values.IsSubsetOf(required.Values)
                => ProgramAnalysisDiagnostics.DependentArgumentViolated,
            TypeParameter forwarded when !(Demanded(forwarded)?.Values ?? Admitted.Everything).IsSubsetOf(required.Values)
                => ProgramAnalysisDiagnostics.DependentForwardUnproven,
            _ => null,
        };
    }

    public static MistDiagnostic? DomainFinding(TypeParameter parameter, IMistType argument)
    {
        if(parameter is not { IsDependent: true, IsPack: false, Domain: { } domain } || DependentDomains.Primitive(domain) is null)
        {
            return null;
        }

        return argument switch
        {
            ErrorType or UnknownType or UnknownValueType => null,
            LiftedType lifted => DependentDomains.Holds(domain, lifted.Value) ? null : ProgramAnalysisDiagnostics.DependentArgumentNotAssignable,
            TypeParameter { IsDependent: true, IsPack: false, Domain: null } => null,
            TypeParameter { IsDependent: true, IsPack: false, Domain: { } from }
                => DependentDomains.Forwards(from, domain) ? null : ProgramAnalysisDiagnostics.DependentForwardNotConvertible,
            _ => ProgramAnalysisDiagnostics.DependentArgumentNotAssignable,
        };
    }

    // Only ulong's upper half is boxed as a ulong, and long.MaxValue is the top every interval saturates at.
    private static Admitted? Known(LiftedType lifted)
    {
        return lifted.Value is ulong ? Admitted.Exactly(long.MaxValue) : Requirement.ValuesOf(lifted);
    }

    private static OptionalValue Owner(TypeParameter parameter)
    {
        return new OptionalValue(SlotKind.Type, parameter.DeclaringType is { } owner ? DiagnosticSnapshot.Type(owner) : null);
    }

    private static OptionalValue OwnerName(TypeParameter parameter)
    {
        return new OptionalValue(SlotKind.Name, parameter.DeclaringType is null ? new NameValue(parameter.Name) : null);
    }

    private static Requirement? Demanded(TypeParameter parameter)
    {
        var declared = Requirement.Of(parameter.Refinement);
        var domain = parameter.Domain is { } type ? Requirement.OfType(type) : null;

        return (declared, domain) switch
        {
            (null, null) => null,
            ({ } only, null) => only,
            (null, { } alone) => alone,
            ({ } both, { } and) => both.And(and),
        };
    }
}