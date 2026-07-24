using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelType = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Scans projected static classes for extension methods (methods with
/// [ExtensionAttribute] on a static class with [ExtensionAttribute]) and
/// registers them as <see cref="ExtensionBlock"/> instances on the appropriate
/// <see cref="MistNamespace"/>.
/// </summary>
internal static class ExtensionProjector
{
    private const string ExtensionAttributeFullName =
        "System.Runtime.CompilerServices.ExtensionAttribute";

    /// <summary>
    /// Scans all projected CLR types for extension methods and registers them
    /// as extension blocks on their declaring namespace in the ProgramModel.
    /// Each extension method gets its own <see cref="ExtensionBlock"/> because
    /// generic extension methods resolve their receiver to distinct type instances
    /// (e.g., each method's TSource is a unique TypeParameter).
    /// Should be called after <see cref="AmbientTypeProviderV2.ProjectTypes"/>
    /// completes, so all type shells are fully projected and resolvable.
    /// </summary>
    public static void ProjectExtensions(
        Dictionary<string, Type> clrTypes,
        AmbientTypeResolver resolver,
        ProgramModelType programModel)
    {
        foreach(var (_, clrType) in clrTypes)
        {
            // Extension methods live in static classes with [ExtensionAttribute].
            if(!IsExtensionClass(clrType))
            {
                continue;
            }

            ScanExtensionClass(clrType, resolver, programModel);
        }
    }

    /// <summary>
    /// Scans a single static extension class for qualifying extension methods,
    /// projects each one, and registers it on the appropriate namespace.
    /// </summary>
    private static void ScanExtensionClass(
        Type extensionClass,
        AmbientTypeResolver resolver,
        ProgramModelType programModel)
    {
        var namespaceName = extensionClass.Namespace ?? "";

        MethodInfo[] methods;
        try
        {
            methods = extensionClass.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        }
        catch
        {
            return;
        }

        foreach(var method in methods)
        {
            if(!IsExtensionMethod(method))
            {
                continue;
            }

            ProjectExtensionMethod(method, namespaceName, resolver, programModel);
        }
    }

    /// <summary>
    /// Projects a single CLR extension method: resolves the receiver type from the
    /// first parameter, projects the remaining parameters as a regular method, and
    /// registers a new <see cref="ExtensionBlock"/> on the namespace.
    /// </summary>
    private static void ProjectExtensionMethod(
        MethodInfo method,
        string namespaceName,
        AmbientTypeResolver resolver,
        ProgramModelType programModel)
    {
        ParameterInfo[] allParams;
        try
        {
            allParams = method.GetParameters();
        }
        catch
        {
            return;
        }

        if(allParams.Length == 0)
        {
            return;
        }

        // The extension block needs a scope for method-level type parameter resolution.
        // Use a fresh scope parented to null — ambient extensions don't have a file scope.
        var blockScope = new Scope(null) { Namespace = namespaceName };

        // Project method-level type parameters first so the receiver and remaining
        // parameters can reference them (e.g., Where<TSource>(this IEnumerable<TSource>...)).
        var typeParameters = new List<TypeParameter>();
        if(method.IsGenericMethodDefinition)
        {
            ProjectMethodTypeParameters(method, blockScope, typeParameters, resolver);
        }

        // Resolve the receiver type (first parameter).
        var receiverParam = allParams[0];
        var receiverClrType = receiverParam.ParameterType.IsByRef
            ? receiverParam.ParameterType.GetElementType()!
            : receiverParam.ParameterType;

        if(HasPointerType(receiverClrType))
        {
            return;
        }

        var receiverType = resolver.Resolve(receiverClrType, blockScope);
        if(receiverType is ErrorType or NotProjectedType)
        {
            return;
        }

        // Resolve return type.
        var returnType = resolver.Resolve(method.ReturnType, blockScope);
        if(returnType is ErrorType or NotProjectedType)
        {
            return;
        }

        // Project remaining parameters (skip receiver).
        var remainingClrParams = allParams[1..];
        var parameters = ParameterProjector.Project(remainingClrParams, blockScope, resolver);
        if(parameters is null)
        {
            return;
        }

        // Create the extension block for this method.
        var extensionBlock = new ExtensionBlock("self", namespaceName, blockScope, null)
        {
            ResolvedReceiverType = receiverType,
        };

        // Build the Method instance.
        var modifiers = new MemberModifiers
        {
            Accessibility = Accessibility.Public,
            IsStatic = true,
        };

        var projected = new Method(method.Name, returnType, parameters, modifiers)
        {
            Scope = blockScope,
        };

        projected.TypeParametersList.AddRange(typeParameters);
        AttributeProjector.Project(method, projected.AttributesList, blockScope, resolver);

        extensionBlock.MethodsList.Add(projected);

        // Register on namespace.
        var ns = programModel.GetOrAddNamespace(namespaceName);
        if(!ns.Extensions.TryGetValue(receiverType, out var list))
        {
            list = [];
            ns.Extensions[receiverType] = list;
        }

        list.Add(extensionBlock);
    }

    /// <summary>
    /// Projects method-level generic type parameters and their constraints
    /// into the given scope. Mirrors <see cref="MethodProjector"/>'s logic.
    /// </summary>
    private static void ProjectMethodTypeParameters(
        MethodInfo method,
        Scope methodScope,
        List<TypeParameter> typeParameters,
        AmbientTypeResolver resolver)
    {
        var clrTypeParams = method.GetGenericArguments();

        // Pass 1: Create all type parameters and register in scope.
        for(var i = 0; i < clrTypeParams.Length; i++)
        {
            var clrParam = clrTypeParams[i];
            // Use null for declaring type — extension method type params belong to the method, not a type.
            var typeParam = new TypeParameter(clrParam.Name, i, methodScope, null!);
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
    /// Determines whether a CLR type is a static class with [ExtensionAttribute]
    /// (i.e., a class that contains extension methods).
    /// </summary>
    private static bool IsExtensionClass(Type clrType)
    {
        // Static class in CLR = abstract + sealed.
        if(!clrType.IsAbstract || !clrType.IsSealed)
        {
            return false;
        }

        return HasExtensionAttribute(clrType);
    }

    /// <summary>
    /// Determines whether a CLR method is an extension method
    /// ([ExtensionAttribute], public, static, at least 1 parameter).
    /// SpecialName methods (property/event accessors) are excluded.
    /// </summary>
    private static bool IsExtensionMethod(MethodInfo method)
    {
        if(method.IsSpecialName)
        {
            return false;
        }

        return HasExtensionAttribute(method);
    }

    /// <summary>
    /// Checks whether a type or method has [System.Runtime.CompilerServices.ExtensionAttribute]
    /// using metadata-only inspection.
    /// </summary>
    private static bool HasExtensionAttribute(MemberInfo member)
    {
        try
        {
            var attributes = member.GetCustomAttributesData();
            foreach(var attr in attributes)
            {
                if(attr.AttributeType.FullName == ExtensionAttributeFullName)
                {
                    return true;
                }
            }
        }
        catch
        {
            // If GetCustomAttributesData() throws, treat as not having the attribute.
        }

        return false;
    }

    private static bool HasPointerType(Type type)
    {
        return type.IsPointer || (type.IsByRef && type.GetElementType()!.IsPointer);
    }
}