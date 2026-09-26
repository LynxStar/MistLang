using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The <c>where</c> clauses of a generic declaration, one line per constrained parameter, indented
/// under the declaration they qualify.
/// <para>
/// C# FIXES THE ORDER WITHIN A CLAUSE -- <c>class</c>, <c>struct</c> or <c>unmanaged</c> first, then the types, then
/// <c>new()</c> -- and rejects any other, so the parts are collected into three buckets rather than
/// emitted as they were declared. Mist's own order is free.
/// </para>
/// <para>
/// The substructural and readonly kinds are Mist's alone and govern checks rather than emission, so
/// they contribute nothing here. A PARAMETERIZED <c>new(int)</c> becomes a synthesized interface
/// instead, which is a type constraint and so joins that bucket -- see
/// <see cref="ConstructorConstraintInterfaces"/>.
/// </para>
/// </summary>
internal static class ConstraintEmitter
{
    public static void Emit(IReadOnlyList<TypeParameter> typeParameters, CSharpWriter writer, bool liftSlots = false)
    {
        writer.Indent();

        var constrained = liftSlots
            ? typeParameters.Concat(SlotLift.Synthesized(typeParameters))
            : typeParameters;

        foreach(var parameter in constrained)
        {
            var parts = Parts(parameter, liftSlots);

            // The witness is what makes a dependent parameter carry a value at all, so it is added
            // here rather than declared in Mist -- there is no Mist constraint that spells it.
            if(parameter.Domain is { } domain
                && parameter.IsDependent
                && !parameter.IsPack
                && DependentCarrierNames.Witness(domain) is { } witness)
            {
                parts.Add(witness);
            }

            if(parts.Count > 0)
            {
                writer.WriteLine(
                    $"where {DependentCarrierNames.Parameter(parameter)} : {string.Join(", ", parts)}");
            }
        }

        writer.Dedent();
    }

    private static List<string> Parts(TypeParameter parameter, bool liftSlots)
    {
        var kinds = new List<string>();
        var types = new List<string>();
        var parameterless = false;
        var stampable = parameter.ConstraintsList.Exists(constraint => constraint.Kind == TypeParameterConstraintKind.Stampable);

        if(stampable)
        {
            kinds.Add("unmanaged");
        }

        foreach(var constraint in parameter.ConstraintsList)
        {
            switch(constraint)
            {
                case KeywordConstraint { Kind: TypeParameterConstraintKind.Class }:
                    kinds.Add("class");
                    break;

                case KeywordConstraint { Kind: TypeParameterConstraintKind.Struct }:
                    if(!stampable)
                    {
                        kinds.Add("struct");
                    }

                    break;

                case TypeConstraint typeConstraint:
                    types.Add(TypeNameEmitter.Emit(
                        liftSlots ? SlotLift.Constraint(typeConstraint.ConstraintType) : typeConstraint.ConstraintType));
                    break;

                case ConstructorConstraint { ParameterTypes.Count: 0 }:
                    parameterless = true;
                    break;

                case ConstructorConstraint constructorConstraint:
                    types.Add(ConstructorConstraintInterfaces.Closed(
                        constructorConstraint.ParameterTypes, parameter.Name));
                    break;
            }
        }

        var parts = new List<string>(kinds);
        parts.AddRange(types);

        if(parameterless && !stampable)
        {
            parts.Add("new()");
        }

        return parts;
    }
}