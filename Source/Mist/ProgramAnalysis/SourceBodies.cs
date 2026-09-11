using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Every root under which the project's own source holds code -- member bodies, constructor chains,
/// accessors, initializers, default blocks, enum values, extension blocks and top-level statements.
/// <para>
/// SCOPED AS EMISSION IS: source assemblies other than Standard. Standard's code is the library's, and
/// a finding there lands on a file nobody in the project wrote. Nested types are absent from a unit's
/// type list by design, so they are reached through their declaring type.
/// </para>
/// </summary>
internal static class SourceBodies
{
    public static IEnumerable<object> Of(ProgramModelClass model)
    {
        foreach(var assembly in model.Assemblies)
        {
            if(!assembly.HasMistSource || assembly.Kind == MistAssemblyKind.Standard)
            {
                continue;
            }

            foreach(var unit in assembly.CompilationUnits)
            {
                if(unit.TopLevelMethod?.Body is { } topLevel)
                {
                    yield return topLevel;
                }

                foreach(var type in unit.Types)
                {
                    foreach(var root in OfType(type))
                    {
                        yield return root;
                    }
                }

                foreach(var block in unit.Extensions)
                {
                    foreach(var root in OfExtension(block))
                    {
                        yield return root;
                    }
                }
            }
        }
    }

    private static IEnumerable<object> OfType(MistType type)
    {
        if(type is MemberType member)
        {
            foreach(var root in OfMembers(member))
            {
                yield return root;
            }
        }
        else if(type is EnumType enumType)
        {
            foreach(var enumMember in enumType.MembersList)
            {
                if(enumMember.Value is { } value)
                {
                    yield return value;
                }
            }
        }

        foreach(var nested in type.NestedTypes)
        {
            foreach(var root in OfType(nested))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<object> OfMembers(MemberType member)
    {
        foreach(var method in member.EnumerateDelegates())
        {
            if(method.Body is { } body)
            {
                yield return body;
            }

            if(method is Constructor constructor)
            {
                foreach(var argument in constructor.ChainArgumentsList)
                {
                    yield return argument;
                }
            }
        }

        foreach(var property in member.PropertiesList)
        {
            if(property.Initializer is { } initializer)
            {
                yield return initializer;
            }

            foreach(var root in OfAccessors(property.Getter, property.Setter))
            {
                yield return root;
            }
        }

        foreach(var indexer in member.IndexersList)
        {
            foreach(var root in OfAccessors(indexer.Getter, indexer.Setter))
            {
                yield return root;
            }
        }

        if(member is not ConcreteType concrete)
        {
            yield break;
        }

        foreach(var field in concrete.FieldsList)
        {
            if(field.Initializer is { } initializer)
            {
                yield return initializer;
            }
        }

        foreach(var root in OfDefaults(concrete.DefaultBlock?.Entries ?? []))
        {
            yield return root;
        }
    }

    private static IEnumerable<object> OfExtension(ExtensionBlock block)
    {
        foreach(var method in block.MethodsList)
        {
            if(method.Body is { } body)
            {
                yield return body;
            }
        }

        foreach(var property in block.PropertiesList)
        {
            foreach(var root in OfAccessors(property.Getter, property.Setter))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<object> OfAccessors(params PropertyAccessor?[] accessors)
    {
        foreach(var accessor in accessors)
        {
            if(accessor?.Body is { } body)
            {
                yield return body;
            }

            if(accessor?.ExpressionBody is { } expressionBody)
            {
                yield return expressionBody;
            }
        }
    }

    private static IEnumerable<object> OfDefaults(IReadOnlyList<DefaultEntry> entries)
    {
        foreach(var entry in entries)
        {
            switch(entry)
            {
                case DefaultValueEntry valued:
                    yield return valued.Value;
                    break;

                case DefaultBindingEntry binding:
                    foreach(var root in OfDefaults(binding.NestedEntries))
                    {
                        yield return root;
                    }

                    break;
            }
        }
    }
}