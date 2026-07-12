using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR methods onto a Mist MemberType shell.
/// Handles instance and static methods, method-level type parameters with
/// constraints, and filters out property/event accessors and operators.
/// </summary>
internal static class MethodProjector
{
    /// <summary>
    /// Projects all public declared methods onto the given MemberType.
    /// Skips property accessors, event accessors, operators, and methods
    /// with unresolvable parameter or return types.
    /// </summary>
    public static void Project(MemberType memberType, Type clrType, AmbientTypeResolver resolver)
    {
        var methods = clrType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach(var method in methods)
        {
            if(ShouldSkip(method))
            {
                continue;
            }

            var projected = ProjectMethod(method, memberType, resolver);
            if(projected is not null)
            {
                memberType.MethodsList.Add(projected);
            }
        }
    }

    /// <summary>
    /// Projects a single CLR method into a Mist Method instance.
    /// Returns null if any parameter or return type is unresolvable.
    /// </summary>
    private static Method? ProjectMethod(MethodInfo method, MemberType memberType, AmbientTypeResolver resolver)
    {
        // Create a method scope for method-level type parameters.
        var methodScope = new Scope(memberType.Scope)
        {
            Namespace = memberType.NamespaceName
        };

        // Project method-level type parameters (if generic method).
        var typeParameters = new List<TypeParameter>();
        if(method.IsGenericMethodDefinition)
        {
            ProjectMethodTypeParameters(method, methodScope, memberType, typeParameters, resolver);
        }

        // Resolve return type.
        var returnType = resolver.Resolve(method.ReturnType, methodScope);
        if(returnType is ErrorType or NotProjectedType)
        {
            return null;
        }

        // Resolve parameters.
        var clrParams = method.GetParameters();
        var parameters = new List<Parameter>(clrParams.Length);
        foreach(var param in clrParams)
        {
            if(HasPointerType(param.ParameterType))
            {
                return null;
            }

            var paramType = resolver.Resolve(param.ParameterType, methodScope);
            if(paramType is ErrorType or NotProjectedType)
            {
                return null;
            }

            parameters.Add(new Parameter(param.Name ?? "", paramType));
        }

        var modifiers = new MemberModifiers
        {
            IsPublic = true,
            IsStatic = method.IsStatic,
        };

        var projected = new Method(method.Name, returnType, parameters, modifiers)
        {
            Scope = methodScope,
        };

        projected.TypeParametersList.AddRange(typeParameters);

        return projected;
    }

    /// <summary>
    /// Projects method-level generic type parameters and their constraints.
    /// Uses a two-pass approach: first creates all type parameters and registers
    /// them in the method scope, then resolves constraints (so cross-parameter
    /// constraints like <c>where T : U</c> can resolve).
    /// </summary>
    private static void ProjectMethodTypeParameters(
        MethodInfo method,
        Scope methodScope,
        MemberType declaringType,
        List<TypeParameter> typeParameters,
        AmbientTypeResolver resolver)
    {
        var clrTypeParams = method.GetGenericArguments();

        // Pass 1: Create type parameters and register in scope.
        for(var i = 0; i < clrTypeParams.Length; i++)
        {
            var clrParam = clrTypeParams[i];
            var typeParam = new TypeParameter(clrParam.Name, i, methodScope, declaringType);
            typeParameters.Add(typeParam);
            methodScope.AddLocalType(clrParam.Name, typeParam);
        }

        // Pass 2: Resolve constraints.
        for(var i = 0; i < clrTypeParams.Length; i++)
        {
            ProjectConstraints(typeParameters[i], clrTypeParams[i], methodScope, resolver);
        }
    }

    /// <summary>
    /// Maps CLR generic parameter constraints to Mist constraint instances.
    /// Mirrors the logic in <see cref="TypeParameterProjector"/>.
    /// </summary>
    private static void ProjectConstraints(
        TypeParameter typeParam,
        Type clrParam,
        Scope scope,
        AmbientTypeResolver resolver)
    {
        var attributes = clrParam.GenericParameterAttributes;
        var specialConstraints = attributes & GenericParameterAttributes.SpecialConstraintMask;

        if((specialConstraints & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
        {
            typeParam.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));
        }

        if((specialConstraints & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
        {
            typeParam.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
        }

        if((specialConstraints & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
        {
            typeParam.ConstraintsList.Add(new ConstructorConstraint([]));
        }

        var constraintTypes = clrParam.GetGenericParameterConstraints();
        foreach(var constraintType in constraintTypes)
        {
            if(constraintType.FullName == "System.ValueType")
            {
                continue;
            }

            var resolved = resolver.Resolve(constraintType, scope);
            if(resolved is not ErrorType and not NotProjectedType)
            {
                typeParam.ConstraintsList.Add(new TypeConstraint(resolved));
            }
        }
    }

    /// <summary>
    /// Determines whether a CLR method should be skipped during projection.
    /// Filters out: property accessors, event accessors, operator overloads,
    /// and finalizers.
    /// </summary>
    private static bool ShouldSkip(MethodInfo method)
    {
        // Property and event accessors are marked as SpecialName.
        if(method.IsSpecialName)
        {
            return true;
        }

        // Skip finalizers.
        if(method.Name == "Finalize" && method.GetParameters().Length == 0 && method.DeclaringType != typeof(object))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether a CLR type involves pointers (unsafe types that cannot
    /// be represented in Mist).
    /// </summary>
    private static bool HasPointerType(Type type)
    {
        return type.IsPointer || (type.IsByRef && type.GetElementType()!.IsPointer);
    }
}