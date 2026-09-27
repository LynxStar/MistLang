using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class AmbientTypeAvailability
{
    public static void Prepare(
        Dictionary<string, DeclaredType> shells,
        Dictionary<string, Type> clrTypes,
        Dictionary<string, IMistType> aliases,
        AmbientTypeResolver resolver)
    {
        var dependencies = new Dictionary<string, HashSet<string>>();
        var failed = new HashSet<string>();
        foreach(var (name, clrType) in clrTypes)
        {
            var required = new HashSet<string>();
            dependencies[name] = required;
            try
            {
                if(clrType.DeclaringType is { } parent)
                {
                    AddDependencies(parent, required, aliases);
                }

                foreach(var parameter in clrType.GetGenericArguments())
                {
                    foreach(var constraint in parameter.GetGenericParameterConstraints())
                    {
                        if(constraint.FullName != "System.ValueType")
                        {
                            AddDependencies(constraint, required, aliases);
                        }
                    }
                }

                if(required.Any(dependency => !shells.ContainsKey(dependency)))
                {
                    _ = failed.Add(name);
                }

                // Inheritance edges propagate constraint exclusions; existing gaps in base/interface
                // projection do not seed this constraint pass.
                if(clrType.BaseType is { FullName: not "System.Object" and not "System.ValueType" } baseType)
                {
                    AddDependencies(baseType, required, aliases);
                }

                foreach(var iface in clrType.GetInterfaces())
                {
                    AddDependencies(iface, required, aliases);
                }
            }
            catch(Exception exception) when(GenericConstraintProjector.IsMetadataFailure(exception))
            {
                _ = failed.Add(name);
            }
        }

        RemoveUnavailable(shells, clrTypes, resolver, Propagate(dependencies, failed));
        failed.Clear();
        foreach(var (name, clrType) in clrTypes)
        {
            if(shells[name] is MemberType member && !TypeParameterProjector.Initialize(member, clrType))
            {
                _ = failed.Add(name);
            }
        }

        while(true)
        {
            foreach(var (name, clrType) in clrTypes)
            {
                if(shells[name] is MemberType member && !TypeParameterProjector.ProjectConstraints(member, clrType, resolver))
                {
                    _ = failed.Add(name);
                }
            }

            if(failed.Count == 0)
            {
                return;
            }

            RemoveUnavailable(shells, clrTypes, resolver, Propagate(dependencies, failed));
            failed.Clear();
        }
    }

    public static HashSet<string> Propagate(
        IReadOnlyDictionary<string, HashSet<string>> dependencies,
        IEnumerable<string> failed)
    {
        var unavailable = failed.ToHashSet();
        var changed = true;
        while(changed)
        {
            changed = false;
            foreach(var (name, required) in dependencies)
            {
                if(!unavailable.Contains(name) && required.Overlaps(unavailable))
                {
                    _ = unavailable.Add(name);
                    changed = true;
                }
            }
        }

        return unavailable;
    }

    private static void AddDependencies(Type type, HashSet<string> required, Dictionary<string, IMistType> aliases)
    {
        if(type.IsGenericParameter)
        {
            return;
        }

        if(type.IsPointer || type.IsFunctionPointer)
        {
            throw new NotSupportedException();
        }

        if(type.HasElementType)
        {
            AddDependencies(type.GetElementType()!, required, aliases);
            return;
        }

        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        if(type.IsGenericType && !type.IsGenericTypeDefinition && definition.FullName == "System.Nullable`1")
        {
            AddDependencies(type.GetGenericArguments()[0], required, aliases);
            return;
        }

        if(ClrValueTuple.Cardinality(type) >= 2)
        {
            AddTupleDependencies(type, required, aliases);
            return;
        }

        var name = AmbientTypeResolver.BuildFQN(definition);
        if(!aliases.ContainsKey(name))
        {
            _ = required.Add(name);
        }

        if(type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            foreach(var argument in type.GetGenericArguments())
            {
                AddDependencies(argument, required, aliases);
            }
        }
    }

    private static void AddTupleDependencies(Type tuple, HashSet<string> required, Dictionary<string, IMistType> aliases)
    {
        var arguments = tuple.GetGenericArguments();
        foreach(var argument in arguments.Take(ClrValueTuple.RestPosition))
        {
            AddDependencies(argument, required, aliases);
        }

        if(arguments.Length > ClrValueTuple.RestPosition)
        {
            AddTupleDependencies(arguments[ClrValueTuple.RestPosition], required, aliases);
        }
    }

    private static void RemoveUnavailable(
        Dictionary<string, DeclaredType> shells,
        Dictionary<string, Type> clrTypes,
        AmbientTypeResolver resolver,
        HashSet<string> unavailable)
    {
        resolver.MarkUnavailable(unavailable);
        foreach(var name in unavailable)
        {
            if(shells.Remove(name, out var shell) && shell.DeclaringType is { } parent)
            {
                _ = parent.NestedTypes.Remove(shell);
            }

            _ = clrTypes.Remove(name);
        }
    }
}