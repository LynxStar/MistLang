using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// The <c>pure</c> token before a type reference, applied after the reference resolves so that
/// <c>pure func</c>, <c>pure Func</c>, <c>pure Action</c> and <c>pure Step</c> over an alias all reach
/// one type.
/// </summary>
internal static class PureFunctionReference
{
    public static IMistType Apply(IMistType resolved, Storm.Runtime.SyntaxNode node, BuilderContext context, Action? reportFailure = null)
    {
        var interner = context.ProgramModel.TypeInterner;
        var expanded = AliasTransparency.Expand(resolved, interner);

        if(expanded is TypeAlias { AliasedType: { } aliased })
        {
            expanded = aliased;
        }

        if(expanded is IFunctionType function)
        {
            return function.IsPure
                ? function
                : interner.GetOrCreateFunctionType(function.ParameterTypes, function.ReturnType, isPure: true);
        }

        if(expanded is not ErrorType)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.PureRequiresFunctionType, node, null, DiagnosticSnapshot.Type(expanded));
            reportFailure?.Invoke();
        }

        return resolved;
    }
}