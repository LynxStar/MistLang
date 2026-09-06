using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
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
    private readonly AttributeBuilder attributeBuilder;

    public ParameterBuilder(ScopeResolver scopeResolver, AttributeBuilder attributeBuilder)
    {
        this.scopeResolver = scopeResolver;
        this.attributeBuilder = attributeBuilder;
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
        var isReadonly = param.Modifier is not null;
        var isRef = param.RefModifier is not null;

        // For a Mist parameter the direction follows from what was written, since the language has
        // one by-ref spelling and `readonly ref` is the only readonly one. It is recorded anyway so
        // that a call site reads the same field whether the callee was declared here or projected.
        var modifier = new ParameterModifier(
            IsReadonly: isReadonly,
            IsRef: isRef,
            Ownership: default,
            Direction: !isRef
                ? ByRefDirection.None
                : isReadonly ? ByRefDirection.In : ByRefDirection.Ref);

        var parameter = new Parameter(name, type, modifier);
        attributeBuilder.Build(param.Attributes, parameter.DeclaredAttributesList, scope);

        return parameter;
    }
}