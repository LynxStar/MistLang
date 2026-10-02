using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Resolves parameter lists from AST nodes into <see cref="Parameter"/> instances.
/// Shared by indexers, methods, constructors, and operators.
/// </summary>
internal sealed class ParameterBuilder
{
    private readonly ScopeResolver scopeResolver;
    private readonly AttributeBuilder attributeBuilder;
    private readonly DiagnosticBag diagnostics;

    public ParameterBuilder(
        ScopeResolver scopeResolver, AttributeBuilder attributeBuilder, DiagnosticBag diagnostics)
    {
        this.scopeResolver = scopeResolver;
        this.attributeBuilder = attributeBuilder;
        this.diagnostics = diagnostics;
    }

    public List<Parameter> Resolve(ParameterListNode? paramList, Scope scope)
    {
        if(paramList is null)
        {
            return [];
        }

        var nodes = new List<ParameterNode> { paramList.Parameter };
        nodes.AddRange(paramList.Parameters);

        var parameters = nodes.ConvertAll(node => ResolveParameter(node, scope));
        var absorbing = ValuePack.FlattenedSlot(parameters);

        for(var i = 0; i < parameters.Count; i++)
        {
            if(ParamsModifiers.WhyMalformed(parameters[i]) is { } reason)
            {
                diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.ParamsParameterMalformed, nodes[i], null, parameters[i].Name, reason);
            }
        }

        // Demoted for the reason a second bracket-free type pack is: the diagnostic is the verdict, and
        // a list left with two absorbing slots has no reading a call could be bound or judged against.
        foreach(var surplus in ParamsModifiers.Surplus(parameters))
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.MultipleParamsParameters, nodes[surplus], null,
                parameters[surplus].Name, parameters[absorbing!.Value].Name);

            parameters[surplus].IsFlattened = false;
        }

        // Only the HOMOGENEOUS form is pinned to the end. Its arguments are whatever runs off the
        // end of the fixed ones, which no earlier slot can collect; a value pack binds inward from
        // both ends instead and so may sit anywhere the Binding_Rule can reach it.
        for(var i = 0; i < parameters.Count - 1; i++)
        {
            if(parameters[i].IsFlattened && ValuePack.PackParameterOf(parameters[i]) is null
                && ParamsModifiers.WhyMalformed(parameters[i]) is null)
            {
                diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.ParamsArrayNotLast, nodes[i], null, parameters[i].Name);
            }
        }

        return parameters;
    }

    private Parameter ResolveParameter(ParameterNode param, Scope scope)
    {
        var name = param.Name.Lexeme;

        // `params TArgs args` gives args the tuple the pack expands to, which is what makes it a
        // real value: one parameter slot, a tuple type, and a flat call site. The homogeneous
        // `params string[]` keeps the array it was written with and only takes the convention.
        var type = PackNamedBy(param, scope) is { } pack
            ? scopeResolver.Interner.GetOrCreateTupleType([new SpreadType(pack)])
            : scopeResolver.Resolve(param.Type, scope);

        var isReadonly = param.Modifier is not null;
        var isRef = param.RefModifier is not null;

        // For a Mist parameter the direction follows from what was written, since the language has
        // one by-ref spelling and `readonly ref` is the only readonly one. It is recorded anyway so
        // that a call site reads the same field whether the callee was declared here or projected.
        var modifier = new ParameterModifier(
            IsReadonly: isReadonly,
            IsRef: isRef,
            // Read from the specifier rather than left at the default: an `owned` parameter is the
            // promise a caller has to honour, and a call site that cannot see it hands over a value
            // someone else still holds. See OwnedProvenance.
            Ownership: MemberTypeBuilder.ResolveOwnership(param.OwnershipSpecifier),
            Direction: !isRef
                ? ByRefDirection.None
                : isReadonly ? ByRefDirection.In : ByRefDirection.Ref);

        var parameter = new Parameter(name, type, modifier) { IsFlattened = param.ParamsModifier is not null };
        attributeBuilder.Build(param.Attributes, parameter.DeclaredAttributesList, scope);

        return parameter;
    }

    private TypeParameter? PackNamedBy(ParameterNode param, Scope scope)
    {
        if(param is not
            {
                ParamsModifier: not null,
                Type:
                {
                    SingleTypeRefs.Length: 0,
                    First: NamedSingleTypeRefSingleTypeRefNode
                    {
                        Pure: null, TypeArgs: null, ArrayRank: null, Optional: null, Name: { IDENTIFIERs.Length: 0 } written,
                    },
                },
            })
        {
            return null;
        }

        return scopeResolver.FindGenericDefinition(written.IDENTIFIER.Lexeme, arity: 0, scope) is TypeParameter { IsPack: true } named
            ? named
            : null;
    }
}