using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// The <c>pure</c> token written before a type reference, applied after the reference resolves: the
/// resolved type is re-interned as the pure function type of the same shape, so <c>pure func</c>,
/// <c>pure Func</c>, <c>pure Action</c> and <c>pure Step</c> over an alias all reach one type. What is
/// not a function type is reported and left as it resolved.
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