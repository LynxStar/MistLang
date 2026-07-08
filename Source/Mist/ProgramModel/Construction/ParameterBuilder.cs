using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves parameter lists from AST nodes into <see cref="Parameter"/> instances.
/// Shared by indexers, methods, constructors, and operators.
/// </summary>
internal sealed class ParameterBuilder
{
    private readonly TypeReferenceResolver typeResolver;

    public ParameterBuilder(TypeReferenceResolver typeResolver)
    {
        this.typeResolver = typeResolver;
    }

    public List<Parameter> Resolve(ParameterListNode? paramList, Scope scope)
    {
        if(paramList is null)
        {
            return [];
        }

        var parameters = new List<Parameter> { ResolveParameter(paramList.Parameter, scope) };

        foreach(var param in paramList.Parameters)
        {
            parameters.Add(ResolveParameter(param, scope));
        }

        return parameters;
    }

    private Parameter ResolveParameter(ParameterNode param, Scope scope)
    {
        var name = param.Name.Lexeme;
        var type = typeResolver.Resolve(param.Type, scope);

        return new Parameter(name, type);
    }
}