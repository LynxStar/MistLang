using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
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

        // Source-side modifier population (ref/readonly/ownership specifier, the struct-vs-class
        // by-value/by-ref derivation, and the redundant/invalid-combination diagnostics) is handled
        // by the type-checker pass, not here. Parameters default to an unrestricted by-value modifier.
        return new Parameter(name, type);
    }
}