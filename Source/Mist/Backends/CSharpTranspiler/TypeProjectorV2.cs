using System.Reflection;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Converts CLR System.Type instances into MistType subclasses with correct type kind.
/// Handles type kind determination based on CLR TypeAttributes.
/// </summary>
/// <remarks>
/// This class creates type SHELLS only — name, namespace, modifiers, and origin.
/// Member projection (methods, properties, fields, constructors) is handled separately
/// in a later task.
/// </remarks>
internal sealed class TypeProjectorV2
{
    private readonly VisibilityFilterV2 visibilityFilter;
    private readonly TypeInterner? typeInterner;

    public TypeProjectorV2(VisibilityFilterV2 visibilityFilter, TypeInterner? typeInterner = null)
    {
        this.visibilityFilter = visibilityFilter;
        this.typeInterner = typeInterner;
    }

    /// <summary>
    /// Creates the appropriate MistType shell for the given CLR type.
    /// Returns null for unrecognized type kinds.
    /// Does NOT project members — that happens in a separate step.
    /// </summary>
    /// <param name="clrType">The CLR type to project.</param>
    /// <param name="namespaceName">The namespace to register the type under.</param>
    /// <returns>The projected MistType shell, or null for unrecognized kinds.</returns>
    public MistType? CreateTypeShell(Type clrType, string namespaceName)
    {
        return CreateTypeShell(clrType, namespaceName, null);
    }

    /// <summary>
    /// Creates the appropriate MistType shell for the given CLR type with a declaring type.
    /// Returns null for unrecognized type kinds.
    /// Does NOT project members — that happens in a separate step.
    /// </summary>
    /// <param name="clrType">The CLR type to project.</param>
    /// <param name="namespaceName">The namespace to register the type under.</param>
    /// <param name="declaringType">The parent type for nested types, or null for top-level types.</param>
    /// <returns>The projected MistType shell, or null for unrecognized kinds.</returns>
    public MistType? CreateTypeShell(Type clrType, string namespaceName, MistType? declaringType)
    {
        if(visibilityFilter.IsTypeExcluded(clrType))
        {
            return null;
        }

        var name = GetNestedTypeMistName(clrType);
        var scope = new Scope(null);

        MistType? result;

        if(clrType.IsEnum)
        {
            result = CreateEnumShell(clrType, name, namespaceName, scope, declaringType);
        }
        else if(clrType.IsInterface)
        {
            result = new AbstractType(name, namespaceName, scope, declaringType, declarationNode: null);
        }
        else if(clrType.IsValueType)
        {
            result = new StructType(name, namespaceName, scope, declaringType, declarationNode: null)
            {
                Modifiers = new MemberModifiers { IsRef = IsRefStruct(clrType) },
            };
        }
        else if(clrType.IsClass)
        {
            result = new ClassType(name, namespaceName, scope, declaringType, declarationNode: null)
            {
                Modifiers = new MemberModifiers
                {
                    IsAbstract = clrType.IsAbstract && !clrType.IsSealed,
                    IsSealed = clrType.IsSealed && !clrType.IsAbstract,
                },
            };
        }
        else
        {
            // Unrecognized type kind — return null, no ProgramModel entry.
            return null;
        }

        result.Origin = TypeOrigin.Assembly;
        return result;
    }

    /// <summary>
    /// Populates the TypeParametersList on a projected type with one entry per CLR generic parameter.
    /// Projects constraints (class, struct, new(), base type, interface).
    /// Returns false if any constraint type is unresolvable (type should be hidden).
    /// </summary>
    /// <param name="shell">The projected MistType shell to populate type parameters on.</param>
    /// <param name="clrType">The CLR generic type definition whose type parameters to project.</param>
    /// <param name="resolveType">
    /// Callback that resolves a type by namespace and name through the projection cache.
    /// </param>
    /// <returns>True if all type parameters and constraints projected successfully; false if the type should be hidden.</returns>
    public static bool ProjectTypeParameters(
        MistType shell,
        Type clrType,
        Func<string, string, IMistType?> resolveType)
    {
        if(shell is not MemberType memberShell)
        {
            return true;
        }

        if(!clrType.IsGenericTypeDefinition)
        {
            return true;
        }

        var genericParams = clrType.GetGenericArguments();
        for(var i = 0; i < genericParams.Length; i++)
        {
            var param = genericParams[i];
            var typeParam = new TypeParameter(param.Name, i, new Scope(null), memberShell);

            // Project constraints
            var attrs = param.GenericParameterAttributes;

            // class constraint
            if((attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
            {
                typeParam.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
            }

            // struct constraint
            if((attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
            {
                typeParam.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));
            }

            // new() constraint — struct implies new(), so skip if already struct-constrained
            if((attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0
                && (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
            {
                typeParam.ConstraintsList.Add(new ConstructorConstraint(Array.Empty<IMistType>()));
            }

            // Type constraints (base type and interfaces)
            var constraints = param.GetGenericParameterConstraints();
            foreach(var constraint in constraints)
            {
                // ValueType is the CLR marker for the struct constraint — already handled above
                if(constraint.FullName == "System.ValueType")
                {
                    continue;
                }

                var constraintNs = constraint.Namespace ?? string.Empty;
                var constraintName = constraint.Name;
                var resolved = resolveType(constraintNs, constraintName);
                if(resolved is null)
                {
                    // Unresolvable constraint → hide entire type
                    return false;
                }

                typeParam.ConstraintsList.Add(new TypeConstraint(resolved));
            }

            memberShell.TypeParametersList.Add(typeParam);
        }

        return true;
    }

    /// <summary>
    /// Resolves the base type of a projected CLR type and sets it on the shell.
    /// Returns false if the base type is unresolvable (the type should be hidden).
    /// </summary>
    /// <param name="shell">The projected MistType shell to set BaseType on.</param>
    /// <param name="clrType">The CLR type whose base class to resolve.</param>
    /// <param name="resolveType">
    /// Callback that resolves a type by namespace and name through the projection cache.
    /// </param>
    /// <returns>True if base type resolution succeeded (or was not needed); false if the type should be hidden.</returns>
    public static bool ResolveBaseType(MistType shell, Type clrType, Func<string, string, IMistType?> resolveType)
    {
        if(shell is not ConcreteType concreteShell)
        {
            return true; // Non-concrete types (AbstractType, EnumType) don't have base types in this sense
        }

        var baseType = clrType.BaseType;

        // System.Object is implicit — don't set it
        if(baseType is null || baseType.FullName == "System.Object")
        {
            return true;
        }

        // System.ValueType is implicit for structs
        if(baseType.FullName == "System.ValueType")
        {
            return true;
        }

        // System.Enum is implicit for enums
        if(baseType.FullName == "System.Enum")
        {
            return true;
        }

        // Resolve the base type through the provider's cache (recursive)
        var baseNs = baseType.Namespace ?? string.Empty;
        var baseName = baseType.Name;
        var resolved = resolveType(baseNs, baseName);

        if(resolved is null)
        {
            // Unresolvable base → hide entire type
            return false;
        }

        concreteShell.BaseType = (MistType)resolved;
        return true;
    }

    /// <summary>
    /// Resolves the interfaces implemented by a CLR type and adds them to BaseAbstractTypes.
    /// Returns false if ANY interface is unresolvable (the type should be hidden).
    /// </summary>
    /// <param name="shell">The projected MistType shell to populate interfaces on.</param>
    /// <param name="clrType">The CLR type whose interfaces to resolve.</param>
    /// <param name="resolveType">
    /// Callback that resolves a type by namespace and name through the projection cache.
    /// </param>
    /// <returns>True if all interfaces resolved successfully; false if the type should be hidden.</returns>
    public static bool ResolveInterfaces(MistType shell, Type clrType, Func<string, string, IMistType?> resolveType)
    {
        if(shell is not MemberType memberShell)
        {
            return true; // Non-member types don't have BaseAbstractTypes
        }

        var interfaces = clrType.GetInterfaces();
        foreach(var iface in interfaces)
        {
            // Skip non-public interfaces
            if(!iface.IsPublic && !iface.IsNestedPublic)
            {
                continue;
            }

            var ifaceNs = iface.Namespace ?? string.Empty;
            var ifaceName = iface.Name;

            // Map IEnumerable<T> and IEnumerator<T> to Standard equivalents
            if(IsStandardInterfaceMapping(ifaceNs, ifaceName))
            {
                var standardName = GetStandardInterfaceName(ifaceName);
                var resolved = resolveType("Standard", standardName);
                if(resolved is null)
                {
                    // Standard interface not yet loaded — skip rather than hiding the type
                    continue;
                }

                memberShell.BaseAbstractTypes.Add((MistType)resolved);
                continue;
            }

            var resolvedIface = resolveType(ifaceNs, ifaceName);
            if(resolvedIface is null)
            {
                // Unresolvable interface → hide entire type
                return false;
            }

            memberShell.BaseAbstractTypes.Add((MistType)resolvedIface);
        }

        return true;
    }

    /// <summary>
    /// Determines whether a CLR interface should be mapped to a Standard Library equivalent.
    /// </summary>
    private static bool IsStandardInterfaceMapping(string namespaceName, string typeName)
    {
        if(namespaceName != "System.Collections.Generic")
        {
            return false;
        }

        var baseName = StripArityFromName(typeName);
        return baseName is "IEnumerable" or "IEnumerator";
    }

    /// <summary>
    /// Gets the Standard Library interface name for a CLR interface that should be mapped.
    /// The returned name uses arity-bearing angle-bracket format (e.g., "IEnumerable&lt;&gt;").
    /// </summary>
    private static string GetStandardInterfaceName(string clrTypeName)
    {
        var baseName = StripArityFromName(clrTypeName);
        var backtickIndex = clrTypeName.IndexOf('`');
        if(backtickIndex >= 0)
        {
            var arityStr = clrTypeName[(backtickIndex + 1)..];
            if(int.TryParse(arityStr, out var arity) && arity > 0)
            {
                var commas = arity > 1 ? new string(',', arity - 1) : "";
                return $"{baseName}<{commas}>";
            }
        }

        return baseName;
    }

    /// <summary>
    /// Creates an EnumType shell with the underlying type and enum members (constants).
    /// </summary>
    private static EnumType CreateEnumShell(
        Type clrType, string name, string namespaceName, Scope scope, MistType? declaringType)
    {
        var enumType = new EnumType(name, namespaceName, scope, declaringType, declarationNode: null);

        // Project enum members (constants).
        foreach(var field in clrType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if(field.IsLiteral)
            {
                enumType.MembersList.Add(new EnumMember(field.Name, isDefault: false, value: null));
            }
        }

        return enumType;
    }

    /// <summary>
    /// Gets the Mist-appropriate type name for a CLR type, accounting for nested type arity.
    /// For nested generic types, the own arity is computed by subtracting the parent's generic
    /// parameter count (since CLR includes all inherited type parameters in the nested type).
    /// </summary>
    private static string GetNestedTypeMistName(Type clrType)
    {
        if(!clrType.IsGenericTypeDefinition)
        {
            return clrType.Name;
        }

        var baseName = StripArityFromName(clrType.Name);
        var totalArity = clrType.GetGenericArguments().Length;

        // For nested types, subtract the parent's generic parameter count
        var parentArity = clrType.DeclaringType?.GetGenericArguments().Length ?? 0;
        var ownArity = totalArity - parentArity;

        if(ownArity <= 0)
        {
            return baseName;
        }

        var commas = ownArity > 1 ? new string(',', ownArity - 1) : "";
        return $"{baseName}<{commas}>";
    }

    /// <summary>
    /// Strips the backtick-arity suffix from a CLR type name (e.g., "List`1" → "List").
    /// </summary>
    private static string StripArityFromName(string clrName)
    {
        var backtickIndex = clrName.IndexOf('`');
        return backtickIndex >= 0 ? clrName[..backtickIndex] : clrName;
    }

    /// <summary>
    /// Projects public instance and static methods from a CLR type onto the shell.
    /// Also projects protected virtual/abstract methods for override detection.
    /// Skips members whose signatures reference excluded types.
    /// Applies alias substitution for all type references via the resolveType callback.
    /// </summary>
    /// <param name="shell">The projected MistType shell to populate methods on.</param>
    /// <param name="clrType">The CLR type whose methods to project.</param>
    /// <param name="resolveType">
    /// Callback that resolves a CLR type by namespace and name through the projection cache.
    /// Returns null if the type cannot be resolved (signature should be skipped).
    /// </param>
    public void ProjectMethods(
        MistType shell,
        Type clrType,
        Func<string, string, IMistType?> resolveType)
    {
        if(shell is not MemberType memberShell)
        {
            return;
        }

        // Get the containing type's type parameters for resolution context.
        var containingTypeParams = memberShell.TypeParametersList.Count > 0
            ? (IReadOnlyList<TypeParameter>)memberShell.TypeParametersList
            : null;

        // Project public instance and static methods.
        foreach(var method in clrType.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if(visibilityFilter.IsMemberExcluded(method))
            {
                continue;
            }

            // Skip property accessors, event accessors, operators (handled elsewhere).
            if(method.IsSpecialName)
            {
                continue;
            }

            var projected = ProjectMethod(method, resolveType, containingTypeParams);
            if(projected is null)
            {
                continue;
            }

            // Mark virtual/abstract public methods for implicit override matching.
            if(method.IsVirtual || method.IsAbstract)
            {
                projected.IsVirtualOrAbstract = true;
                projected.ClrAccessModifier = "public";
            }

            memberShell.MethodsList.Add(projected);
        }

        // Project protected virtual/abstract methods as override slot candidates.
        foreach(var method in clrType.GetMethods(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if(!method.IsVirtual && !method.IsAbstract)
            {
                continue;
            }

            if(!method.IsFamily && !method.IsFamilyOrAssembly)
            {
                continue;
            }

            if(method.IsSpecialName)
            {
                continue;
            }

            // Skip if a public method with the same name already exists.
            if(memberShell.MethodsList.Any(m => m.Name == method.Name))
            {
                continue;
            }

            var projected = ProjectMethod(method, resolveType, containingTypeParams);
            if(projected is null)
            {
                continue;
            }

            projected.IsVirtualOrAbstract = true;
            projected.ClrAccessModifier = method.IsFamilyOrAssembly
                ? "protected"
                : "protected";

            memberShell.MethodsList.Add(projected);
        }
    }

    /// <summary>
    /// Projects public constructors from a CLR type onto the shell.
    /// Skips constructors whose parameter types cannot be resolved.
    /// </summary>
    /// <param name="shell">The projected MistType shell to populate constructors on.</param>
    /// <param name="clrType">The CLR type whose constructors to project.</param>
    /// <param name="resolveType">
    /// Callback that resolves a CLR type by namespace and name through the projection cache.
    /// </param>
    public void ProjectConstructors(
        MistType shell,
        Type clrType,
        Func<string, string, IMistType?> resolveType)
    {
        if(shell is not ConcreteType concreteShell)
        {
            return;
        }

        // Get the containing type's type parameters for resolution context.
        var typeParams = concreteShell is MemberType mt && mt.TypeParametersList.Count > 0
            ? (IReadOnlyList<TypeParameter>)mt.TypeParametersList
            : null;

        foreach(var ctor in clrType.GetConstructors(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if(visibilityFilter.IsMemberExcluded(ctor))
            {
                continue;
            }

            var parameters = ProjectParameters(ctor.GetParameters(), resolveType, typeParams, typeInterner);
            if(parameters is null)
            {
                continue;
            }

            var projected = new Constructor(
                concreteShell.Name,
                concreteShell,
                parameters,
                default);
            concreteShell.ConstructorsList.Add(projected);
        }
    }

    /// <summary>
    /// Projects public properties from a CLR type onto the shell.
    /// Skips indexers (properties with index parameters) and members whose types cannot be resolved.
    /// </summary>
    /// <param name="shell">The projected MistType shell to populate properties on.</param>
    /// <param name="clrType">The CLR type whose properties to project.</param>
    /// <param name="resolveType">
    /// Callback that resolves a CLR type by namespace and name through the projection cache.
    /// </param>
    public void ProjectProperties(
        MistType shell,
        Type clrType,
        Func<string, string, IMistType?> resolveType)
    {
        if(shell is not MemberType memberShell)
        {
            return;
        }

        // Get the containing type's type parameters for resolution context.
        var typeParams = memberShell.TypeParametersList.Count > 0
            ? (IReadOnlyList<TypeParameter>)memberShell.TypeParametersList
            : null;

        foreach(var prop in clrType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if(visibilityFilter.IsMemberExcluded(prop))
            {
                continue;
            }

            // Skip indexers (they have parameters)
            if(prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var type = ResolveClrType(prop.PropertyType, resolveType, typeParams, typeInterner);
            if(type is null)
            {
                continue;
            }

            var modifiers = new MemberModifiers
            {
                IsStatic = prop.GetMethod?.IsStatic ?? prop.SetMethod?.IsStatic ?? false,
            };
            var property = new Property(prop.Name, type, modifiers)
            {
                Getter = prop.GetMethod is { IsPublic: true } ? new PropertyAccessor() : null,
                Setter = prop.SetMethod is { IsPublic: true } ? new PropertyAccessor() : null,
            };
            memberShell.PropertiesList.Add(property);
        }
    }

    /// <summary>
    /// Projects public fields from a CLR type onto the shell.
    /// Skips fields on enum types (enum constants are handled by CreateEnumShell).
    /// Skips the special <c>value__</c> field on enums.
    /// </summary>
    /// <param name="shell">The projected MistType shell to populate fields on.</param>
    /// <param name="clrType">The CLR type whose fields to project.</param>
    /// <param name="resolveType">
    /// Callback that resolves a CLR type by namespace and name through the projection cache.
    /// </param>
    public void ProjectFields(
        MistType shell,
        Type clrType,
        Func<string, string, IMistType?> resolveType)
    {
        if(shell is not ConcreteType concreteShell)
        {
            return;
        }

        // Enum constants are handled by CreateEnumShell — skip entirely for enum types
        if(clrType.IsEnum)
        {
            return;
        }

        // Get the containing type's type parameters for resolution context.
        var typeParams = concreteShell is MemberType mt && mt.TypeParametersList.Count > 0
            ? (IReadOnlyList<TypeParameter>)mt.TypeParametersList
            : null;

        foreach(var field in clrType.GetFields(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if(visibilityFilter.IsMemberExcluded(field))
            {
                continue;
            }

            var type = ResolveClrType(field.FieldType, resolveType, typeParams, typeInterner);
            if(type is null)
            {
                continue;
            }

            var modifiers = new MemberModifiers
            {
                IsStatic = field.IsStatic,
                IsReadonly = field.IsInitOnly || field.IsLiteral,
            };
            var projected = new Field(field.Name, type, OwnershipKind.Unrestricted, modifiers);
            concreteShell.FieldsList.Add(projected);
        }
    }

    /// <summary>
    /// Projects public indexers from a CLR type onto the shell.
    /// An indexer is a property with one or more index parameters.
    /// </summary>
    /// <param name="shell">The projected MistType shell to populate indexers on.</param>
    /// <param name="clrType">The CLR type whose indexers to project.</param>
    /// <param name="resolveType">
    /// Callback that resolves a CLR type by namespace and name through the projection cache.
    /// </param>
    public void ProjectIndexers(
        MistType shell,
        Type clrType,
        Func<string, string, IMistType?> resolveType)
    {
        if(shell is not MemberType memberShell)
        {
            return;
        }

        // Get the containing type's type parameters for resolution context.
        var typeParams = memberShell.TypeParametersList.Count > 0
            ? (IReadOnlyList<TypeParameter>)memberShell.TypeParametersList
            : null;

        foreach(var prop in clrType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var indexParams = prop.GetIndexParameters();
            if(indexParams.Length == 0)
            {
                continue; // Not an indexer
            }

            if(visibilityFilter.IsMemberExcluded(prop))
            {
                continue;
            }

            var type = ResolveClrType(prop.PropertyType, resolveType, typeParams, typeInterner);
            if(type is null)
            {
                continue;
            }

            var parameters = ProjectParameters(indexParams, resolveType, typeParams, typeInterner);
            if(parameters is null)
            {
                continue;
            }

            var modifiers = new MemberModifiers
            {
                IsStatic = prop.GetMethod?.IsStatic ?? false,
            };
            var indexer = new Indexer(type, parameters, modifiers)
            {
                Getter = prop.GetMethod is { IsPublic: true } ? new PropertyAccessor() : null,
                Setter = prop.SetMethod is { IsPublic: true } ? new PropertyAccessor() : null,
            };
            memberShell.IndexersList.Add(indexer);
        }
    }

    /// <summary>
    /// Projects public operator overloads from a CLR type onto the shell.
    /// Only operators with recognized <see cref="OperatorKind"/> mappings are projected.
    /// </summary>
    /// <param name="shell">The projected MistType shell to populate operators on.</param>
    /// <param name="clrType">The CLR type whose operators to project.</param>
    /// <param name="resolveType">
    /// Callback that resolves a CLR type by namespace and name through the projection cache.
    /// </param>
    public void ProjectOperators(
        MistType shell,
        Type clrType,
        Func<string, string, IMistType?> resolveType)
    {
        if(shell is not MemberType memberShell)
        {
            return;
        }

        // Get the containing type's type parameters for resolution context.
        var typeParams = memberShell.TypeParametersList.Count > 0
            ? (IReadOnlyList<TypeParameter>)memberShell.TypeParametersList
            : null;

        foreach(var method in clrType.GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if(!method.IsSpecialName)
            {
                continue; // Operators are special-named
            }

            if(!method.Name.StartsWith("op_", StringComparison.Ordinal))
            {
                continue;
            }

            if(visibilityFilter.IsMemberExcluded(method))
            {
                continue;
            }

            var kind = MapClrOperatorName(method.Name);
            if(kind is null)
            {
                continue; // Unrecognized operator
            }

            var returnType = ResolveClrType(method.ReturnType, resolveType, typeParams, typeInterner);
            if(returnType is null)
            {
                continue;
            }

            var parameters = ProjectParameters(method.GetParameters(), resolveType, typeParams, typeInterner);
            if(parameters is null)
            {
                continue;
            }

            var op = new Operator(kind.Value, returnType, parameters, new MemberModifiers { IsStatic = true });
            memberShell.OperatorsList.Add(op);
        }
    }

    /// <summary>
    /// Maps a CLR operator method name (e.g., "op_Addition") to the corresponding
    /// <see cref="OperatorKind"/>. Returns null for unrecognized operator names.
    /// </summary>
    private static OperatorKind? MapClrOperatorName(string clrName)
    {
        return clrName switch
        {
            "op_Addition" => OperatorKind.Plus,
            "op_Subtraction" => OperatorKind.Minus,
            "op_Multiply" => OperatorKind.Star,
            "op_Division" => OperatorKind.Slash,
            "op_Modulus" => OperatorKind.Modulo,
            "op_Equality" => OperatorKind.EqualEqual,
            "op_Inequality" => OperatorKind.NotEqual,
            "op_LessThan" => OperatorKind.LessThan,
            "op_GreaterThan" => OperatorKind.GreaterThan,
            "op_LessThanOrEqual" => OperatorKind.LessThanOrEqual,
            "op_GreaterThanOrEqual" => OperatorKind.GreaterThanOrEqual,
            "op_LogicalNot" or "op_OnesComplement" => OperatorKind.Exclamation,
            _ => null,
        };
    }

    /// <summary>
    /// Builds the dot-separated nested type key for registration in the namespace types dictionary.
    /// Format: "Parent.Nested" for non-generic, "Parent&lt;&gt;.Nested" for generic parent, etc.
    /// </summary>
    /// <param name="parentShell">The projected parent type shell.</param>
    /// <param name="nestedMistName">The Mist name of the nested type (already arity-adjusted).</param>
    /// <returns>The dot-separated key for namespace registration.</returns>
    internal static string BuildNestedTypeKey(MistType parentShell, string nestedMistName)
    {
        return $"{parentShell.Name}.{nestedMistName}";
    }

    /// <summary>
    /// Projects a single CLR method into a ProgramModel Method instance.
    /// Returns null if any type in the signature cannot be resolved.
    /// Handles method-level generic type parameters and their constraints.
    /// </summary>
    private Method? ProjectMethod(
        MethodInfo method,
        Func<string, string, IMistType?> resolveType,
        IReadOnlyList<TypeParameter>? containingTypeParams)
    {
        // Build method-level type parameters first (needed for signature resolution).
        List<TypeParameter>? methodTypeParams = null;
        if(method.IsGenericMethodDefinition)
        {
            var genericArgs = method.GetGenericArguments();
            methodTypeParams = new List<TypeParameter>(genericArgs.Length);
            for(var i = 0; i < genericArgs.Length; i++)
            {
                var param = genericArgs[i];
                var typeParam = new TypeParameter(param.Name, i, new Scope(null), declaringType: null);
                methodTypeParams.Add(typeParam);
            }
        }

        // Combine containing type params + method type params for resolution context.
        var allTypeParams = CombineTypeParams(containingTypeParams, methodTypeParams);

        // Resolve return type (unwrap by-ref if needed).
        var returnClrType = method.ReturnType;
        var isRefReturn = returnClrType.IsByRef;
        if(isRefReturn)
        {
            returnClrType = returnClrType.GetElementType() ?? returnClrType;
        }

        var returnType = ResolveClrType(returnClrType, resolveType, allTypeParams, typeInterner);
        if(returnType is null)
        {
            return null;
        }

        var parameters = ProjectParameters(method.GetParameters(), resolveType, allTypeParams, typeInterner);
        if(parameters is null)
        {
            return null;
        }

        var modifiers = new MemberModifiers { IsStatic = method.IsStatic };
        var projected = new Method(method.Name, returnType, parameters, modifiers);

        // Set ReturnRefKind for by-ref returns.
        if(isRefReturn)
        {
            projected.ReturnRefKind = RefKind.Ref;
        }

        // Project method-level generic constraints and attach type parameters.
        if(methodTypeParams is not null)
        {
            var genericArgs = method.GetGenericArguments();
            for(var i = 0; i < methodTypeParams.Count; i++)
            {
                var typeParam = methodTypeParams[i];
                var param = genericArgs[i];

                // Project constraints
                var attrs = param.GenericParameterAttributes;

                // class constraint
                if((attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                {
                    typeParam.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
                }

                // struct constraint
                if((attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                {
                    typeParam.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));
                }

                // new() constraint — struct implies new(), so skip if already struct-constrained
                if((attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0
                    && (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                {
                    typeParam.ConstraintsList.Add(new ConstructorConstraint(Array.Empty<IMistType>()));
                }

                // Type constraints (base type and interfaces)
                var constraints = param.GetGenericParameterConstraints();
                foreach(var constraint in constraints)
                {
                    // ValueType is the CLR marker for the struct constraint — already handled above
                    if(constraint.FullName == "System.ValueType")
                    {
                        continue;
                    }

                    var resolved = ResolveClrType(constraint, resolveType, allTypeParams, typeInterner);
                    if(resolved is null)
                    {
                        // Unresolvable method-level constraint → hide this method
                        return null;
                    }

                    typeParam.ConstraintsList.Add(new TypeConstraint(resolved));
                }

                projected.TypeParametersList.Add(typeParam);
            }
        }

        return projected;
    }

    /// <summary>
    /// Projects CLR parameters into ProgramModel Parameter instances.
    /// Returns null if any parameter type cannot be resolved (the member should be skipped).
    /// </summary>
    private static List<Parameter>? ProjectParameters(
        ParameterInfo[] clrParameters,
        Func<string, string, IMistType?> resolveType,
        IReadOnlyList<TypeParameter>? typeParams = null,
        TypeInterner? interner = null)
    {
        var parameters = new List<Parameter>(clrParameters.Length);

        foreach(var param in clrParameters)
        {
            var paramType = param.ParameterType;
            var isByRef = paramType.IsByRef;

            // Unwrap by-ref types (ref/in/out parameters are T& in metadata).
            if(isByRef)
            {
                paramType = paramType.GetElementType() ?? paramType;
            }

            // Unwrap Nullable<T> before resolving inner type.
            if(paramType.IsConstructedGenericType)
            {
                var paramDef = paramType.GetGenericTypeDefinition();
                if(paramDef.Namespace == "System" && paramDef.Name == "Nullable`1")
                {
                    var innerArgs = paramType.GetGenericArguments();
                    var innerResolved = ResolveClrType(innerArgs[0], resolveType, typeParams, interner);
                    if(innerResolved is null)
                    {
                        return null;
                    }

                    var voidType = resolveType("Standard", "Void");
                    var resolved = voidType is null || interner is null
                        ? innerResolved
                        : interner.GetOrCreateUnionType(new List<IMistType> { innerResolved, voidType });

                    var name = param.Name ?? $"arg{param.Position}";
                    var p = new Parameter(name, resolved);

                    if(isByRef)
                    {
                        p.RefKind = RefKind.Ref;
                    }

                    ProjectParameterAttributes(p, param, resolveType);
                    parameters.Add(p);
                    continue;
                }
            }

            var resolvedType = ResolveClrType(paramType, resolveType, typeParams, interner);
            if(resolvedType is null)
            {
                return null;
            }

            var paramName = param.Name ?? $"arg{param.Position}";
            var parameter = new Parameter(paramName, resolvedType);

            // Set RefKind for ref/in/out parameters.
            if(isByRef)
            {
                parameter.RefKind = RefKind.Ref;
            }

            ProjectParameterAttributes(parameter, param, resolveType);
            parameters.Add(parameter);
        }

        return parameters;
    }

    /// <summary>
    /// Projects CLR custom attributes from a parameter's metadata onto the ProgramModel Parameter.
    /// Skips attributes whose type cannot be resolved through the projection cache.
    /// </summary>
    private static void ProjectParameterAttributes(
        Parameter parameter,
        ParameterInfo clrParam,
        Func<string, string, IMistType?> resolveType)
    {
        IList<CustomAttributeData> attributesData;
        try
        {
            attributesData = clrParam.GetCustomAttributesData();
        }
        catch
        {
            // Metadata may be malformed — skip attribute projection entirely.
            return;
        }

        foreach(var attrData in attributesData)
        {
            var attrType = attrData.AttributeType;
            var attrNs = attrType.Namespace ?? string.Empty;
            var attrName = attrType.Name;

            var resolved = resolveType(attrNs, attrName);
            if(resolved is null)
            {
                continue;
            }

            var positionalArgs = new List<object?>(attrData.ConstructorArguments.Count);
            foreach(var arg in attrData.ConstructorArguments)
            {
                positionalArgs.Add(arg.Value);
            }

            var namedArgs = new List<(string Name, object? Value)>(attrData.NamedArguments.Count);
            foreach(var arg in attrData.NamedArguments)
            {
                namedArgs.Add((arg.MemberName, arg.TypedValue.Value));
            }

            parameter.AttributesList.Add(new AttributeData(resolved, positionalArgs, namedArgs));
        }
    }

    /// <summary>
    /// Resolves a CLR type to its Mist equivalent via the resolveType callback.
    /// Handles generic type definitions, constructed generic types, and generic parameters.
    /// </summary>
    /// <param name="clrType">The CLR type to resolve.</param>
    /// <param name="resolveType">Callback to resolve types by namespace and name.</param>
    /// <param name="typeParams">
    /// Combined type parameter list (containing type params + method-level params) for resolving
    /// generic parameter references. Type-level params come first, method-level params follow.
    /// </param>
    /// <param name="interner">
    /// TypeInterner for creating constructed types. When null, constructed generics fall back
    /// to returning just the definition.
    /// </param>
    private static IMistType? ResolveClrType(
        Type clrType,
        Func<string, string, IMistType?> resolveType,
        IReadOnlyList<TypeParameter>? typeParams = null,
        TypeInterner? interner = null)
    {
        // Generic parameters (T, TKey, etc.) — resolve via the type parameter list.
        if(clrType.IsGenericParameter)
        {
            if(typeParams is null)
            {
                return null;
            }

            // CLR generic parameters have a GenericParameterPosition.
            // For method-level params on type-owning methods, we need to distinguish
            // type-level vs method-level using DeclaringMethod.
            var position = clrType.GenericParameterPosition;

            if(clrType.DeclaringMethod is not null)
            {
                // Method-level generic parameter — offset past the containing type's params.
                var typeParamCount = clrType.DeclaringType?.GetGenericArguments().Length ?? 0;
                var index = typeParamCount + position;
                if(index < typeParams.Count)
                {
                    return typeParams[index];
                }

                return null;
            }

            // Type-level generic parameter
            if(position < typeParams.Count)
            {
                return typeParams[position];
            }

            return null;
        }

        // Handle array types — skip for now (full array projection is a separate concern).
        if(clrType.IsArray)
        {
            return null;
        }

        // Handle constructed generic types — resolve the definition AND type arguments.
        if(clrType.IsConstructedGenericType)
        {
            var def = clrType.GetGenericTypeDefinition();
            var ns = def.Namespace ?? string.Empty;
            var name = def.Name;

            // Nullable<T> → UnionType(T, Standard.Void)
            if(ns == "System" && name == "Nullable`1")
            {
                var clrInnerArgs = clrType.GetGenericArguments();
                var innerArg = ResolveClrType(clrInnerArgs[0], resolveType, typeParams, interner);
                if(innerArg is null)
                {
                    return null;
                }

                var voidType = resolveType("Standard", "Void");
                if(voidType is null || interner is null)
                {
                    return innerArg;
                }

                return interner.GetOrCreateUnionType(new List<IMistType> { innerArg, voidType });
            }

            var definition = resolveType(ns, name);
            if(definition is null)
            {
                return null;
            }

            if(interner is not null)
            {
                var clrArgs = clrType.GetGenericArguments();

                // Detect System.Action`N constructed types: the definition resolves to
                // Standard.Func with arity N+1 (N params + Void return). We need to
                // append Standard.Void as the final type argument.
                var isAction = ns == "System"
                    && name.StartsWith("Action`", StringComparison.Ordinal);

                var resolvedArgs = new List<IMistType>(clrArgs.Length + (isAction ? 1 : 0));
                foreach(var arg in clrArgs)
                {
                    var resolved = ResolveClrType(arg, resolveType, typeParams, interner);
                    if(resolved is null)
                    {
                        // If any type argument is unresolvable, fall back to just the definition
                        return definition;
                    }

                    resolvedArgs.Add(resolved);
                }

                // For Action types, append Standard.Void as the return type argument
                if(isAction)
                {
                    var voidType = resolveType("Standard", "Void");
                    if(voidType is null)
                    {
                        return definition;
                    }

                    resolvedArgs.Add(voidType);
                }

                return interner.GetOrCreateConstructedType(definition, resolvedArgs);
            }

            return definition;
        }

        var typeNs = clrType.Namespace ?? string.Empty;
        var typeName = clrType.Name;
        return resolveType(typeNs, typeName);
    }

    /// <summary>
    /// Combines containing type parameters and method-level type parameters into a single list.
    /// Type-level params come first (positions 0..N-1), method-level params follow (positions N..N+M-1).
    /// Returns null if both inputs are null/empty.
    /// </summary>
    private static IReadOnlyList<TypeParameter>? CombineTypeParams(
        IReadOnlyList<TypeParameter>? typeParams,
        List<TypeParameter>? methodTypeParams)
    {
        if(methodTypeParams is null or { Count: 0 })
        {
            return typeParams;
        }

        if(typeParams is null or { Count: 0 })
        {
            return methodTypeParams;
        }

        var combined = new List<TypeParameter>(typeParams.Count + methodTypeParams.Count);
        combined.AddRange(typeParams);
        combined.AddRange(methodTypeParams);
        return combined;
    }

    /// <summary>
    /// Determines whether a type is a ref struct (carries
    /// <c>System.Runtime.CompilerServices.IsByRefLikeAttribute</c>).
    /// </summary>
    private static bool IsRefStruct(Type type)
    {
        try
        {
            return type.GetCustomAttributesData()
                .Any(a => a.AttributeType.FullName ==
                    "System.Runtime.CompilerServices.IsByRefLikeAttribute");
        }
        catch
        {
            // If metadata cannot be loaded, default to false.
            return false;
        }
    }
}