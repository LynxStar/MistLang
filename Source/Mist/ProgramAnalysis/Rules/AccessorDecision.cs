using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal static class AccessorDecision
{
    public static MistDiagnostic? Read(
        PropertyAccessor? getter, MistType? owner, MistAssembly? assembly,
        Accessibility memberAccessibility, MistType? declaring)
    {
        if(getter is null)
        {
            return ProgramAnalysisDiagnostics.NoGetAccessor;
        }

        return Visible(owner, assembly, memberAccessibility, declaring)
            ? Access(getter, owner, assembly, declaring)
            : null;
    }

    public static MistDiagnostic? Write(
        PropertyAccessor? setter, bool backingField, bool initializing,
        MistType? owner, MistAssembly? assembly, Accessibility memberAccessibility, MistType? declaring)
    {
        if(backingField)
        {
            return null;
        }

        if(setter is null)
        {
            return ProgramAnalysisDiagnostics.NoSetAccessor;
        }

        if(!Visible(owner, assembly, memberAccessibility, declaring))
        {
            return null;
        }

        return Access(setter, owner, assembly, declaring)
            ?? (setter.IsInit && !initializing ? ProgramAnalysisDiagnostics.InitOnlyAssignment : null);
    }

    private static bool Visible(MistType? owner, MistAssembly? assembly, Accessibility memberAccessibility, MistType? declaring)
    {
        return declaring is null || (AccessDomains.Reaches(owner, assembly, declaring)
            && AccessDomains.Admits(owner, assembly, memberAccessibility, declaring));
    }

    private static MistDiagnostic? Access(PropertyAccessor accessor, MistType? owner, MistAssembly? assembly, MistType? declaring)
    {
        return declaring is null || AccessDomains.Admits(owner, assembly, accessor.Accessibility, declaring)
            ? null : ProgramAnalysisDiagnostics.InaccessibleAccessor;
    }
}