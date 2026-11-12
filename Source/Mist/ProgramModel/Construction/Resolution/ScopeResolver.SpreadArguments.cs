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
        var nodes = ArgumentNodes(syntax);
        var parameters = (definition as IHasTypeParameters)?.TypeParametersList;
        var arguments = ResolveArguments(nodes, scope, parameters);

        if(parameters is null)
        {
            return arguments;
        }

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

    private static List<TypeArgumentNode> ArgumentNodes(TypeArgumentListNode syntax)
    {
        return [syntax.TypeArgument, .. syntax.TypeArguments];
    }

    private List<IMistType> ResolveArguments(List<TypeArgumentNode> nodes, Scope scope, IReadOnlyList<TypeParameter>? parameters)
    {
        var arguments = new List<IMistType>(nodes.Count);

        for(var i = 0; i < nodes.Count; i++)
        {
            if(parameters is not null && SlotAt(parameters, nodes.Count, i) is { IsDependent: true, IsPack: false } slot
                && SlotValue(nodes[i], slot, scope) is { } value)
            {
                arguments.Add(value);
            }
            else
            {
                ResolveTypeArgument(nodes[i], scope, arguments);
            }
        }

        return arguments;
    }
}