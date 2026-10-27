using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

internal sealed partial class ScopeResolver
{
    public IReadOnlyList<IMistType> ResolveTypeArguments(TypeArgumentListNode syntax, Scope scope, IMistType definition)
    {
        var arguments = ResolveTypeArguments(syntax, scope).ToList();
        if(definition is not IHasTypeParameters generic)
        {
            return arguments;
        }

        var parameters = generic.TypeParametersList;
        var nodes = new[] { syntax.TypeArgument }.Concat(syntax.TypeArguments).ToList();
        for(var i = 0; i < arguments.Count; i++)
        {
            if(arguments[i] is not SpreadType spread)
            {
                continue;
            }

            if(PackBinding.AcceptsSpread(parameters, arguments.Count, i))
            {
                continue;
            }

            var node = nodes[i] is TypeReferenceTypeArgumentTypeArgumentNode type ? type.Type.First : (Storm.Runtime.SyntaxNode)nodes[i];
            ReportResolutionFailure(ProgramModelDiagnostics.SpreadNonPackArgument, node, null, new NameValue(spread.Operand.Name), new NameValue(MistType.StripArity(definition.Name)));
            arguments[i] = ErrorType.Instance;
        }

        return arguments;
    }
}