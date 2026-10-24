using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
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

            try
            {
                ProjectExtensionMethod(method, namespaceName, resolver, programModel);
            }
            catch(Exception exception) when(GenericConstraintProjector.IsMetadataFailure(exception))
            {
                continue;
            }
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

        // The receiver is the first parameter, and its CLR shape decides which of the method's
        // type parameters belong to the block. Determined before anything is resolved, because the
        // block's own parameters have to be in place for the receiver type to reference them.
        var receiverParam = allParams[0];
        var receiverClrType = receiverParam.ParameterType.IsByRef
            ? receiverParam.ParameterType.GetElementType()!
            : receiverParam.ParameterType;

        if(HasPointerType(receiverClrType))
        {
            return;
        }

        // Parented to null: ambient extensions have no file scope.
        var blockScope = new ExtensionScope(null);
        var extensionBlock = new ExtensionBlock("self", namespaceName, blockScope, null);

        var methodScope = new MethodScope(blockScope) { IsStatic = true };

        var clrTypeParams = method.IsGenericMethodDefinition
            ? method.GetGenericArguments()
            : [];

        var methodTypeParameters = ProjectTypeParameters(
            clrTypeParams, receiverClrType, extensionBlock, blockScope, methodScope, resolver);

        if(methodTypeParameters is null)
        {
            return;
        }

        // The receiver may only name block-level parameters, so it resolves against the block.
        var receiverType = resolver.Resolve(receiverClrType, blockScope, TupleElementNames.Of(receiverParam, receiverClrType));
        if(receiverType is ErrorType or NotProjectedType)
        {
            return;
        }

        // Everything else may name both levels, so it resolves against the method.
        var returnType = resolver.ResolveInbound(
            method.ReturnType,
            methodScope,
            ClrNullability.ResultIsNonNull(method.ReturnParameter, method),
            TupleElementNames.Of(method.ReturnParameter, method.ReturnType));
        if(returnType is ErrorType or NotProjectedType)
        {
            return;
        }

        var remainingClrParams = allParams[1..];
        var parameters = ParameterProjector.Project(remainingClrParams, methodScope, resolver);
        if(parameters is null)
        {
            return;
        }

        extensionBlock.ResolvedReceiverType = receiverType;

        // NOT static, though the CLR method is. An extension block's members are reached THROUGH a
        // receiver, which is what `self` names and what makes them instance-shaped in Mist; a
        // Mist-declared block marks none of them static either. Carrying the CLR's own staticness
        // here made every projected extension invisible to a call on a receiver.
        var modifiers = new MemberModifiers
        {
            Accessibility = Accessibility.Public,
            IsClrProjected = true,
            ClrResultIsNonNull = ClrNullability.ResultIsNonNull(method.ReturnParameter, method),
        };

        var projected = new Method(method.Name, returnType, parameters, modifiers)
        {
            Scope = methodScope,
            SequenceLengthOperation = ClrSequenceLengths.Of(method),
        };

        projected.TypeParametersList.AddRange(methodTypeParameters);
        foreach(var typeParameter in projected.TypeParametersList)
        {
            typeParameter.DeclaringMethod = projected;
        }

        AttributeProjector.Project(method, projected.AttributesList, methodScope, resolver);

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
    /// Splits a CLR extension method's type parameters between the block and the method, creates
    /// both sets, and resolves their constraints. Returns the method-level ones; the block-level ones
    /// are added to <paramref name="extensionBlock"/>.
    /// <para>
    /// The CLR has no extension blocks -- <c>Select&lt;TSource, TResult&gt;(this
    /// IEnumerable&lt;TSource&gt; ...)</c> is one flat list -- so resugaring has to decide where each
    /// parameter came from. A parameter the receiver type mentions was the block's:
    /// <c>extension&lt;TSource&gt;(IEnumerable&lt;TSource&gt; self)</c>. The rest were the method's.
    /// </para>
    /// <para>
    /// Ordinals stay as the CLR numbered them rather than becoming positions within either list,
    /// because the constraint projection below indexes back into the flat CLR list by ordinal.
    /// </para>
    /// </summary>
    private static List<TypeParameter>? ProjectTypeParameters(
        Type[] clrTypeParams,
        Type receiverClrType,
        ExtensionBlock extensionBlock,
        ExtensionScope blockScope,
        MethodScope methodScope,
        AmbientTypeResolver resolver)
    {
        var methodTypeParameters = new List<TypeParameter>();
        if(clrTypeParams.Length == 0)
        {
            return methodTypeParameters;
        }

        var blockLevel = PartitionBlockLevel(clrTypeParams, receiverClrType);

        // Both sets are created before any constraint is resolved, so a constraint may name any
        // parameter regardless of which level it landed on.
        for(var i = 0; i < clrTypeParams.Length; i++)
        {
            var clrParam = clrTypeParams[i];

            // Null declaring type: these belong to the block or the method, never to a type.
            if(blockLevel.Contains(clrParam))
            {
                extensionBlock.TypeParametersList.Add(new TypeParameter(clrParam.Name, i, blockScope, null!));
            }
            else
            {
                var typeParam = new TypeParameter(clrParam.Name, i, methodScope, null!);
                methodTypeParameters.Add(typeParam);
                methodScope.DeclareType(clrParam.Name, typeParam);
            }
        }

        // Resolved against the method scope, which sees both levels through its parent.
        foreach(var typeParam in extensionBlock.TypeParametersList)
        {
            if(!GenericConstraintProjector.Project(typeParam, clrTypeParams[typeParam.Ordinal], methodScope, resolver))
            {
                return null;
            }
        }

        foreach(var typeParam in methodTypeParameters)
        {
            if(!GenericConstraintProjector.Project(typeParam, clrTypeParams[typeParam.Ordinal], methodScope, resolver))
            {
                return null;
            }
        }

        return methodTypeParameters;
    }

    /// <summary>
    /// The type parameters that belong to the block: those the receiver type mentions, plus any a
    /// block-level parameter's constraints mention.
    /// <para>
    /// The second part is what makes <c>where TSource : IEquatable&lt;TResult&gt;</c> representable.
    /// A block-level parameter constrained by a method-level one would need both visible at the block,
    /// and the method scope is the inner one, so the referenced parameter is promoted instead.
    /// Iterated to a fixed point, since a promoted parameter's own constraints may pull in more.
    /// </para>
    /// </summary>
    private static HashSet<Type> PartitionBlockLevel(Type[] clrTypeParams, Type receiverClrType)
    {
        var blockLevel = new HashSet<Type>();

        foreach(var clrParam in clrTypeParams)
        {
            if(Mentions(receiverClrType, clrParam))
            {
                _ = blockLevel.Add(clrParam);
            }
        }

        var changed = true;
        while(changed)
        {
            changed = false;

            foreach(var promoted in blockLevel.ToArray())
            {
                foreach(var constraint in promoted.GetGenericParameterConstraints())
                {
                    foreach(var clrParam in clrTypeParams)
                    {
                        if(!blockLevel.Contains(clrParam) && Mentions(constraint, clrParam))
                        {
                            _ = blockLevel.Add(clrParam);
                            changed = true;
                        }
                    }
                }
            }
        }

        return blockLevel;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> appears anywhere in <paramref name="type"/>, at any
    /// depth: <c>TValue</c> is mentioned by <c>Dictionary&lt;TKey, List&lt;TValue&gt;&gt;</c>.
    /// </summary>
    private static bool Mentions(Type type, Type candidate)
    {
        if(type.IsGenericParameter)
        {
            return type == candidate;
        }

        if(type.HasElementType)
        {
            var element = type.GetElementType();
            return element is not null && Mentions(element, candidate);
        }

        if(type.IsGenericType)
        {
            foreach(var argument in type.GetGenericArguments())
            {
                if(Mentions(argument, candidate))
                {
                    return true;
                }
            }
        }

        return false;
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
