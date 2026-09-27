using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

internal sealed partial class PurityInference
{
    private void Seed()
    {
        foreach(var type in context.Types.OfType<MemberType>())
        {
            foreach(var property in type.PropertiesList)
            {
                Note(property);
            }

            foreach(var indexer in type.IndexersList)
            {
                Note(indexer);
            }
        }

        foreach(var block in context.Extensions)
        {
            foreach(var property in block.PropertiesList)
            {
                Note(property);
            }

            shadowed.UnionWith(block.MethodsList.Where(RunsTheObjectsOwn));
        }

        foreach(var root in context.Bodies)
        {
            object? member = root.Member switch
            {
                Method method when ReferenceEquals(method.Body, root.Root) => method,
                Constructor constructor when root.Root is Expression argument && constructor.ChainArgumentsList.Contains(argument) => constructor,
                Property property => AccessorOf(property.Getter, property.Setter, root.Root),
                Indexer indexer => AccessorOf(indexer.Getter, indexer.Setter, root.Root),
                _ => null,
            };

            if(member is not null && !(member is Method extension && shadowed.Contains(extension)))
            {
                Register(member, root.Root);
            }

            if(member is Constructor built && ReferenceEquals(built.Body, root.Root))
            {
                RegisterInitializers(built);
            }
        }
    }

    /// <summary>
    /// An extension member the target never calls: C# binds a call spelled on an instance to the
    /// object's own member before any extension, and every object has these, so the body written here
    /// is not what runs and the member keeps only what it declares (Requirement 9.1).
    /// </summary>
    private static bool RunsTheObjectsOwn(Method method)
    {
        return !method.Modifiers.IsStatic && (method.Name, method.ParametersList.Count) is ("ToString", 0) or ("GetHashCode", 0) or ("Equals", 1);
    }

    /// <summary>
    /// The instance initializers a constructor runs before its body -- every one of its type's, unless
    /// it chains to another constructor of the same type, which runs them instead -- so they are roots
    /// of its promise as its body is.
    /// </summary>
    public static IEnumerable<Expression> InstanceInitializers(Constructor constructor)
    {
        if(constructor.Modifiers.IsStatic || constructor.ChainKind == ConstructorChainKind.This || constructor.DeclaringType is not ConcreteType type)
        {
            return [];
        }

        return InstanceInitializers(type);
    }

    public static IEnumerable<Expression> InstanceInitializers(ConcreteType type)
    {
        foreach(var field in type.FieldsList)
        {
            if(field is { Modifiers.IsStatic: false, Initializer: { } initializer })
            {
                yield return initializer;
            }
        }

        foreach(var property in type.PropertiesList)
        {
            if(property is { Initializer: { } initializer, HasBackingStorage: true, Modifiers.IsStatic: false })
            {
                yield return initializer;
            }
        }
    }

    public static IEnumerable<Expression> ImplicitConstructorInitializers(ConcreteType type)
    {
        return Defaultability.IsDefaultable(type) ? [] : InstanceInitializers(type);
    }

    internal static PropertyAccessor? AccessorOf(PropertyAccessor? getter, PropertyAccessor? setter, object root)
    {
        if(getter is not null && (ReferenceEquals(getter.Body, root) || ReferenceEquals(getter.ExpressionBody, root)))
        {
            return getter;
        }

        if(setter is not null && (ReferenceEquals(setter.Body, root) || ReferenceEquals(setter.ExpressionBody, root)))
        {
            return setter;
        }

        return null;
    }

    private void RegisterInitializers(Constructor constructor)
    {
        foreach(var initializer in InstanceInitializers(constructor))
        {
            Register(constructor, initializer);
        }
    }

    private void Register(object member, object root)
    {
        if(!roots.TryGetValue(member, out var known))
        {
            known = [];
            roots[member] = known;
        }

        if(known.Contains(root))
        {
            return;
        }

        known.Add(root);
        memberOfRoot[root] = member;
        dirty = true;
        _ = inferred.TryAdd(member, Promise.All);
        Collect(root, root);
    }

    private object? OriginsMember(object? member)
    {
        return member is PropertyAccessor accessor && accessors.TryGetValue(accessor, out var owner)
            ? (object?)owner.Property ?? owner.Indexer
            : member;
    }

    private void Collect(object node, object scope)
    {
        NoteAccessed(node);

        foreach(var child in BodyWalker.ChildrenOf(node))
        {
            switch(child)
            {
                case LambdaExpression lambda:
                    enclosingOf[lambda] = scope;
                    closureOf[lambda] = lambda;
                    _ = inferred.TryAdd(lambda, Promise.All);
                    Collect(lambda, lambda);
                    break;

                case LocalFunctionStatement function:
                    enclosingOf[function] = scope;
                    closureOf[function.Function] = function;
                    _ = inferred.TryAdd(function.Function, Promise.All);
                    Collect(function, function);
                    break;

                default:
                    Collect(child, scope);
                    break;
            }
        }
    }

    private void NoteAccessed(object node)
    {
        switch(node)
        {
            case PropertyReferenceExpression reference:
                Note(reference.Property);
                break;

            case MemberAccessExpression { Member: PropertyResult found }:
                Note(found.Property);
                break;

            case ExtensionPropertyAccessExpression extension:
                Note(extension.Property);
                break;

            case ObjectInitializerAssignment { Member: PropertyResult found }:
                Note(found.Property);
                break;

            case PropertySubpattern { Member: PropertyResult found }:
                Note(found.Property);
                break;

            case IndexExpression index:
                if(index.Indexer is { } indexer)
                {
                    Note(indexer);
                }

                if(index.FromEnd is { } counted)
                {
                    Note(counted.Count);
                }

                break;

            case ListPattern { Reads: { } reads }:
                Note(reads.Count);
                Note(reads.Indexer);
                break;
        }
    }

    private void Note(Property property)
    {
        if(property.Getter is { } getter)
        {
            accessors[getter] = new AccessorOwner(property.Modifiers, property.DeclaringType, IsSetter: false, property, null);
        }

        if(property.Setter is { } setter)
        {
            accessors[setter] = new AccessorOwner(property.Modifiers, property.DeclaringType, IsSetter: true, property, null);
        }
    }

    private void Note(Indexer indexer)
    {
        if(indexer.Getter is { } getter)
        {
            accessors[getter] = new AccessorOwner(indexer.Modifiers, indexer.DeclaringType, IsSetter: false, null, indexer);
        }

        if(indexer.Setter is { } setter)
        {
            accessors[setter] = new AccessorOwner(indexer.Modifiers, indexer.DeclaringType, IsSetter: true, null, indexer);
        }
    }
}