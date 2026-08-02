using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The <c>where</c> clauses of a generic declaration, one line per constrained parameter, indented
/// under the declaration they qualify.
/// <para>
/// C# FIXES THE ORDER WITHIN A CLAUSE -- <c>class</c> or <c>struct</c> first, then the types, then
/// <c>new()</c> -- and rejects any other, so the parts are collected into three buckets rather than
/// emitted as they were declared. Mist's own order is free.
/// </para>
/// <para>
/// Only the constraints C# HAS are spelled. The substructural and readonly kinds are Mist's alone and
/// govern checks rather than emission, so they contribute nothing here. A PARAMETERIZED
/// <c>new(int)</c> is likewise absent, because C# has only the parameterless form and standing one in
/// for the other needs the factory lowering of <c>generic-constraints-constructor</c>.
/// </para>
/// </summary>
internal static class ConstraintEmitter
{
    public static void Emit(IReadOnlyList<TypeParameter> typeParameters, CSharpWriter writer)
    {
        writer.Indent();

        foreach(var parameter in typeParameters)
        {
            var parts = Parts(parameter);

            if(parts.Count > 0)
            {
                writer.WriteLine($"where {parameter.Name} : {string.Join(", ", parts)}");
            }
        }

        writer.Dedent();
    }

    private static List<string> Parts(TypeParameter parameter)
    {
        var kinds = new List<string>();
        var types = new List<string>();
        var parameterless = false;

        foreach(var constraint in parameter.ConstraintsList)
        {
            switch(constraint)
            {
                case KeywordConstraint { Kind: TypeParameterConstraintKind.Class }:
                    kinds.Add("class");
                    break;

                case KeywordConstraint { Kind: TypeParameterConstraintKind.Struct }:
                    kinds.Add("struct");
                    break;

                case TypeConstraint typeConstraint:
                    types.Add(TypeNameEmitter.Emit(typeConstraint.ConstraintType));
                    break;

                case ConstructorConstraint { ParameterTypes.Count: 0 }:
                    parameterless = true;
                    break;
            }
        }

        var parts = new List<string>(kinds);
        parts.AddRange(types);

        if(parameterless)
        {
            parts.Add("new()");
        }

        return parts;
    }
}