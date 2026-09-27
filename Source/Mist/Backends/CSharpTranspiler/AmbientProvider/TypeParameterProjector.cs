using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class TypeParameterProjector
{
    public static bool Project(MemberType projectedType, Type clrType, AmbientTypeResolver resolver)
    {
        return Initialize(projectedType, clrType) && ProjectConstraints(projectedType, clrType, resolver);
    }

    public static bool Initialize(MemberType projectedType, Type clrType)
    {
        try
        {
            if(!clrType.IsGenericTypeDefinition)
            {
                return true;
            }

            var clrTypeParams = clrType.GetGenericArguments();
            var inheritedCount = clrType.DeclaringType?.GetGenericArguments().Length ?? 0;
            for(var i = inheritedCount; i < clrTypeParams.Length; i++)
            {
                projectedType.TypeParametersList.Add(new TypeParameter(
                    clrTypeParams[i].Name, i - inheritedCount, projectedType.Scope, projectedType));
            }

            return true;
        }
        catch(Exception exception) when(GenericConstraintProjector.IsMetadataFailure(exception))
        {
            return false;
        }
    }

    public static bool ProjectConstraints(MemberType projectedType, Type clrType, AmbientTypeResolver resolver)
    {
        try
        {
            if(!clrType.IsGenericTypeDefinition)
            {
                return true;
            }

            var clrTypeParams = clrType.GetGenericArguments();
            var inheritedCount = clrType.DeclaringType?.GetGenericArguments().Length ?? 0;
            foreach(var parameter in projectedType.TypeParametersList)
            {
                parameter.ConstraintsList.Clear();
            }

            for(var i = 0; i < projectedType.TypeParametersList.Count; i++)
            {
                if(!GenericConstraintProjector.Project(
                    projectedType.TypeParametersList[i], clrTypeParams[inheritedCount + i], projectedType.Scope, resolver))
                {
                    return false;
                }
            }

            return true;
        }
        catch(Exception exception) when(GenericConstraintProjector.IsMetadataFailure(exception))
        {
            return false;
        }
    }
}