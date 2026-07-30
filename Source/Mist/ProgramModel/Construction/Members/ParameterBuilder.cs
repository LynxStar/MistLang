using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Resolves parameter lists from AST nodes into <see cref="Parameter"/> instances.
/// Shared by indexers, methods, constructors, and operators.
/// </summary>
internal sealed class ParameterBuilder
{
    private readonly ScopeResolver scopeResolver;

    public ParameterBuilder(ScopeResolver scopeResolver)
    {
        this.scopeResolver = scopeResolver;
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
        var type = scopeResolver.Resolve(param.Type, scope);
        var modifier = new ParameterModifier(IsReadonly: false, IsRef: param.RefModifier is not null, Ownership: default);

        return new Parameter(name, type, modifier);
    }
}