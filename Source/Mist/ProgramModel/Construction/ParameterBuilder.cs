using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

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

    /// <summary>
    /// Resolves a value-parameter list into <see cref="Parameter"/> instances.
    ///
    /// When a <paramref name="diagnostics"/> bag is supplied, this method also enforces
    /// the at-most-one-value-side-<c>params</c> rule (Requirements 7.8, 7.9), reporting
    /// <see cref="VariadicsDiagnostics.MultipleParamsPacks"/> when a value-parameter list
    /// declares a second value-side <c>params</c> parameter. This mirrors the
    /// type-parameter side in <see cref="TypeParameterPopulator"/>; callers whose list is
    /// validated elsewhere may omit the bag.
    /// </summary>
    public List<Parameter> Resolve(ParameterListNode? paramList, Scope scope, DiagnosticBag? diagnostics = null)
    {
        if(paramList is null)
        {
            return [];
        }

        // Gather the declared parameter nodes in declaration order.
        var nodes = new List<ParameterNode> { paramList.Parameter };
        nodes.AddRange(paramList.Parameters);

        var parameters = new List<Parameter>(nodes.Count);
        foreach(var node in nodes)
        {
            parameters.Add(ResolveParameter(node, scope));
        }

        EnforceSingleValueParams(nodes, diagnostics);

        return parameters;
    }

    private Parameter ResolveParameter(ParameterNode param, Scope scope)
    {
        var name = param.Name.Lexeme;

        // A `params`-modified value parameter is resolved leniently: the value-pack form
        // `params TArgs args` names the pack directly, so a bare pack here must resolve to the
        // pack itself (to become the tuple (TArgs...) below) rather than being reported as a
        // bare-type-position error (MIST_VAR_0002, Requirement 6.3). Every other parameter uses
        // strict resolution, so a bare pack in an ordinary (non-`params`) parameter type is
        // still rejected. A homogeneous `params T[]` and any non-pack type resolve identically.
        var isParams = param.ParamsModifier is not null;
        var type = typeResolver.ResolveParameterType(param.Type, scope, isParams);

        // Variadics value side: a leading `params` on a value parameter introduces the
        // Flattened_Call_Convention (the caller supplies a flat, comma-separated argument list
        // rather than a single packaged argument). Which representation it takes is decided
        // purely by the declared type — a pack type parameter versus a homogeneous array — and,
        // because one ParameterNode yields one Parameter either way, method arity (the number of
        // Parameter instances) counts a value-side `params` as a single slot, mirroring the
        // type-parameter side (`Foo(int number, params TArgs args, bool enabled)` has arity 3).
        if(isParams)
        {
            // Value_Pack (Requirements 7.1, 7.2, 7.7): `params TArgs args` where the declared
            // type is a type-parameter pack. Its value has the tuple type `(TArgs...)`
            // (equivalently `ValueTuple<TArgs>`), so the parameter takes that tuple type.
            if(type is TypeParameter { IsPack: true } pack)
            {
                var packTuple = typeResolver.BuildValuePackTuple(pack);
                return new Parameter(name, packTuple, hasFlattenedCallConvention: true);
            }

            // Value_Level_Params (Requirements 13.1, 13.4): `params T[] xs` where the declared
            // type is the homogeneous array type `T[]` (NOT a pack). Unlike the value pack, the
            // parameter keeps that real array type — the callee receives a genuine `T[]`, and
            // only the Flattened_Call_Convention marks that the caller supplies the trailing
            // elements as a flat argument list. It is distinguished from a Type_Parameter_Pack
            // purely by position: this `params` sits in the VALUE-parameter list. Collecting the
            // trailing arguments into the array is the type checker's job (task 12.2); emitting
            // the C# `params T[]` is the backend's (task 14.2) — this is representation only.
            // Counting toward the one-`params`-per-list rule (Requirement 13.5) is handled by
            // EnforceSingleValueParams, which keys on the `params` modifier and so already
            // includes this case.
            if(typeResolver.IsArrayType(type))
            {
                return new Parameter(name, type, hasFlattenedCallConvention: true);
            }
        }

        // A plain `(TArgs...) args` parameter (no `params`) is an ordinary single-tuple
        // parameter: `type` has already resolved to the tuple `(TArgs...)`, and without the
        // `params` marker it is NOT flattened — it receives one tuple argument at the call site.
        // The `params` modifier is the sole marker of the Flattened_Call_Convention.
        //
        // Source-side modifier population (ref/readonly/ownership specifier, the
        // struct-vs-class by-value/by-ref derivation, and the redundant/invalid-combination
        // diagnostics) is handled by the type-checker pass, not here. Parameters default to an
        // unrestricted by-value modifier.
        return new Parameter(name, type);
    }

    /// <summary>
    /// Requirements 7.8 / 7.9: at most one value-side <c>params</c> parameter per
    /// value-parameter list. Both a Value_Pack (<c>params TArgs args</c>, target tuple
    /// <c>(TArgs...)</c>) and a Value_Level_Params (<c>params T[] xs</c>, target array)
    /// count as a value-side <c>params</c> parameter, so the count is driven by the
    /// presence of the <c>params</c> modifier on the parameter node itself
    /// (<c>ParamsModifier</c> is non-null for both spellings) rather than by the resolved
    /// parameter's call convention -- this stays correct regardless of whether the array
    /// target is yet marked with the Flattened_Call_Convention. Any additional pack must
    /// be written as an explicit plain tuple parameter <c>(TArgs...) name</c> (no
    /// <c>params</c>), which does not count. The first <c>params</c> parameter is the
    /// permitted bracket-free slot; a second one is the violation, so the diagnostic
    /// points at that second declaration, mirroring the type-parameter side in
    /// <see cref="TypeParameterPopulator"/>.
    /// </summary>
    private static void EnforceSingleValueParams(List<ParameterNode> nodes, DiagnosticBag? diagnostics)
    {
        if(diagnostics is null)
        {
            return;
        }

        ParameterNode? secondParamsNode = null;
        var paramsCount = 0;
        foreach(var node in nodes)
        {
            if(node.ParamsModifier is null)
            {
                continue;
            }

            paramsCount++;
            if(paramsCount == 2)
            {
                secondParamsNode = node;
                break;
            }
        }

        if(secondParamsNode is not null)
        {
            diagnostics.ReportAtNode(
                VariadicsDiagnostics.MultipleParamsPacks, secondParamsNode, null, "value-parameter");
        }
    }
}