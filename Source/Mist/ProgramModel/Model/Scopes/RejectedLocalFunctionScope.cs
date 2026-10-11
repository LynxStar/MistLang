using Mist.Generated;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

internal sealed class RejectedLocalFunctionScope(Scope parent, TypeParameterListNode parameters) : Scope(parent)
{
    private readonly HashSet<string> names = Names(parameters);

    internal bool ReturnTypeIsFailed { get; set; }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        var stem = MistType.StripArity(request.Name.Split('.')[0]);

        return names.Contains(stem)
            ? new TypeResult(ErrorType.Instance, IsFailed: true)
            : new NotFoundResult();
    }

    private static HashSet<string> Names(TypeParameterListNode parameters)
    {
        return [parameters.TypeParameter.GetName().Lexeme, .. parameters.TypeParameters.Select(parameter => parameter.GetName().Lexeme)];
    }
}