using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
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
    /// Projects all public and protected declared methods onto the given MemberType.
    /// Skips property accessors, event accessors, operators, and methods
    /// with unresolvable parameter or return types.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
    public static void Project(MemberType memberType, Type clrType, AmbientTypeResolver resolver)
    {
        var methods = clrType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach(var method in methods)
        {
            if(Judge(method, memberType, resolver, out var withheld) is { } projected)
            {
                memberType.MethodsList.Add(projected);
            }
            else if(withheld is { } reason)
            {
                memberType.WithheldMembers.Add(reason.Snapshot(method));
            }
        }
    }

    internal static Method? Judge(
        MethodInfo method, MemberType memberType, AmbientTypeResolver resolver, out Withholding? withheld)
    {
        if(SkipReason(method) is { } reason)
        {
            withheld = new Withholding(reason);
            return null;
        }

        return ProjectMethod(method, memberType, resolver, out withheld);
    }

    /// <summary>
    /// Projects a single CLR method into a Mist Method instance.
    /// Returns null if any parameter or return type is unresolvable.
    /// </summary>
    private static Method? ProjectMethod(
        MethodInfo method, MemberType memberType, AmbientTypeResolver resolver, out Withholding? withheld)
    {
        withheld = null;

        // Create a method scope for method-level type parameters.
        var methodScope = new MethodScope(memberType.Scope)
        {
            IsStatic = method.IsStatic,
        };

        // Project method-level type parameters (if generic method).
        var typeParameters = new List<TypeParameter>();
        if(method.IsGenericMethodDefinition)
        {
            if(!ProjectMethodTypeParameters(method, methodScope, memberType, typeParameters, resolver))
            {
                withheld = new Withholding(WithholdingReason.UnresolvableConstraint);
                return null;
            }
        }

        // Resolve return type.
        var returnType = resolver.ResolveInbound(
            method.ReturnType,
            methodScope,
            ClrNullability.ResultIsNonNull(method.ReturnParameter, method),
            TupleElementNames.Of(method.ReturnParameter, method.ReturnType));
        if(returnType is ErrorType or NotProjectedType)
        {
            withheld = new Withholding(WithholdingReason.UnresolvableType, method.ReturnType);
            return null;
        }

        // Resolve parameters via shared projector.
        var parameters = ParameterProjector.Project(method.GetParameters(), methodScope, resolver, out var unresolvable);
        if(parameters is null)
        {
            withheld = new Withholding(WithholdingReason.UnresolvableType, unresolvable);
            return null;
        }

        var modifiers = new MemberModifiers
        {
            Accessibility = ClrAccessibilityMap.FromMethod(method),
            IsStatic = method.IsStatic,
            IsRef = method.ReturnType.IsByRef,
            ReturnsReadonlyRef = ParameterProjector.IsReadonlyRef(method.ReturnParameter),
            IsClrProjected = true,
            ClrResultIsNonNull = ClrNullability.ResultIsNonNull(method.ReturnParameter, method),
        };

        var projected = new Method(method.Name, returnType, parameters, modifiers)
        {
            DeclaringType = memberType,
            Scope = methodScope,
            SequenceLengthOperation = ClrSequenceLengths.Of(method),
            HasNativeBody = method.DeclaringType is { IsInterface: true } && method.IsPublic && !method.IsStatic && !method.IsAbstract,
        };

        projected.TypeParametersList.AddRange(typeParameters);
        foreach(var typeParameter in projected.TypeParametersList)
        {
            typeParameter.DeclaringMethod = projected;
        }

        AttributeProjector.Project(method, projected.AttributesList, methodScope, resolver);
        SentinelAttributes.ApplyMethodAttributes(method, projected.AttributesList);

        return projected;
    }

    /// <summary>
    /// Projects method-level generic type parameters and their constraints.
    /// Uses a two-pass approach: first creates all type parameters and registers
    /// them in the method scope, then resolves constraints (so cross-parameter
    /// constraints like <c>where T : U</c> can resolve).
    /// </summary>
    private static bool ProjectMethodTypeParameters(
        MethodInfo method,
        ITypeNameScope methodScope,
        MemberType declaringType,
        List<TypeParameter> typeParameters,
        AmbientTypeResolver resolver)
    {
        try
        {
            var clrTypeParams = method.GetGenericArguments();
            for(var i = 0; i < clrTypeParams.Length; i++)
            {
                var clrParam = clrTypeParams[i];
                var typeParam = new TypeParameter(clrParam.Name, i, (Scope)methodScope, declaringType);
                typeParameters.Add(typeParam);
                methodScope.DeclareType(clrParam.Name, typeParam);
            }

            for(var i = 0; i < clrTypeParams.Length; i++)
            {
                if(!GenericConstraintProjector.Project(typeParameters[i], clrTypeParams[i], (Scope)methodScope, resolver))
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

    /// <summary>
    /// Determines whether a CLR method should be skipped during projection.
    /// Filters out: private/internal methods, property accessors, event accessors,
    /// operator overloads, and finalizers.
    /// </summary>
    private static WithholdingReason? SkipReason(MethodInfo method)
    {
        // Only project public and protected (family) methods.
        if(!method.IsPublic && !method.IsFamily && !method.IsFamilyOrAssembly)
        {
            return WithholdingReason.NonPublic;
        }

        // Property and event accessors are marked as SpecialName.
        if(method.IsSpecialName)
        {
            return WithholdingReason.AccessorOrOperator;
        }

        // Skip finalizers.
        if(method.Name == "Finalize" && method.GetParameters().Length == 0 && method.DeclaringType != typeof(object))
        {
            return WithholdingReason.Finalizer;
        }

        return null;
    }
}