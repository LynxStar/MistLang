using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class GenericConstraintProjector
{
    public static bool Project(TypeParameter parameter, Type clrParameter, Scope scope, AmbientTypeResolver resolver)
    {
        try
        {
            var constraints = clrParameter.GetGenericParameterConstraints();
            var special = clrParameter.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;
            if((special & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
            {
                parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));
            }

            if((special & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
            {
                parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
            }

            if((special & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
            {
                parameter.ConstraintsList.Add(new ConstructorConstraint([]));
            }

            foreach(var constraint in constraints)
            {
                if(constraint.FullName == "System.ValueType")
                {
                    continue;
                }

                var resolved = resolver.Resolve(constraint, scope, resolver.TupleNames.OfConstraint(clrParameter, constraint));
                if(resolved is ErrorType or NotProjectedType)
                {
                    return false;
                }

                parameter.ConstraintsList.Add(new TypeConstraint(resolved));
            }

            return true;
        }
        catch(Exception exception) when(IsMetadataFailure(exception))
        {
            return false;
        }
    }

    public static bool IsMetadataFailure(Exception exception)
    {
        return exception is TypeLoadException or FileNotFoundException or FileLoadException
            or BadImageFormatException or NotSupportedException;
    }
}