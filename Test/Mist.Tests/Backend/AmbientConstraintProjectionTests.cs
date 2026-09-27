using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures.ConstraintProjection;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientConstraintProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingConstraintsHideTypesAndDependenciesBeforePublication(bool reverse)
    {
        var types = new[]
        {
            typeof(Dependent<>), typeof(Derived), typeof(IInherited), typeof(Direct<>.Child.Grandchild),
            typeof(Nested<>), typeof(HiddenDelegate<>), typeof(Consumer), typeof(IMarker<>), typeof(IDeep<>),
        };
        var context = new ConstraintProjectionTestContext(reverse ? types.Reverse().ToArray() : types);
        var invalidParent = context.Type(typeof(Direct<>));
        var invalidChild = context.Type(typeof(Direct<>.Child));

        context.Project();

        foreach(var type in types.Where(type => type != typeof(Consumer) && type != typeof(IMarker<>) && type != typeof(IDeep<>)))
        {
            Assert.False(context.Shells.ContainsKey(AmbientTypeResolver.BuildFQN(type)));
            _ = Assert.IsType<ErrorType>(context.Resolver.Resolve(type, new Scope(null)));
        }

        Assert.Empty(invalidParent.NestedTypes);
        Assert.Empty(invalidChild.NestedTypes);
        var consumer = Assert.IsType<ClassType>(context.Type(typeof(Consumer)));
        Assert.Equal([nameof(Consumer.Valid)], consumer.MethodsList.Select(method => method.Name));
        Assert.Empty(consumer.FieldsList);
        Assert.Empty(consumer.PropertiesList);
        Assert.Empty(Assert.Single(consumer.ConstructorsList).ParametersList);
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidRecursiveSiblingAndNestedConstraintsKeepTheirIdentity(bool reverse)
    {
        var types = new[] { typeof(FBound<>), typeof(Siblings<,>), typeof(Outer<>.Inner<>), typeof(IMarker<>), typeof(AliasConstrained<>) };
        var context = new ConstraintProjectionTestContext(reverse ? types.Reverse().ToArray() : types);

        context.Project();

        var recursive = context.Type(typeof(FBound<>));
        var recursiveParameter = Assert.Single(recursive.TypeParametersList);
        var marker = Assert.IsType<ConstructedType>(Assert.IsType<TypeConstraint>(Assert.Single(recursiveParameter.ConstraintsList)).ConstraintType);
        Assert.Same(recursive, Assert.Single(marker.TypeArguments));

        var siblings = context.Type(typeof(Siblings<,>)).TypeParametersList;
        Assert.Same(siblings[1], Assert.IsType<TypeConstraint>(Assert.Single(siblings[0].ConstraintsList)).ConstraintType);

        var inner = context.Type(typeof(Outer<>.Inner<>));
        var innerConstraint = Assert.IsType<ConstructedType>(Assert.IsType<TypeConstraint>(Assert.Single(Assert.Single(inner.TypeParametersList).ConstraintsList)).ConstraintType);
        Assert.Same(Assert.Single(context.Type(typeof(Outer<>)).TypeParametersList), Assert.Single(innerConstraint.TypeArguments));
        Assert.Same(inner, Assert.Single(context.Type(typeof(Outer<>)).NestedTypes));

        var aliased = Assert.IsType<ConstructedType>(Assert.IsType<TypeConstraint>(Assert.Single(Assert.Single(context.Type(typeof(AliasConstrained<>)).TypeParametersList).ConstraintsList)).ConstraintType);
        Assert.Same(context.Aliases["System.IComparable`1"], aliased.Definition);
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidDependencyCycleKeepsBothTypesAndBaseIdentity(bool reverse)
    {
        var types = new[] { typeof(CycleA<>), typeof(CycleB) };
        var context = new ConstraintProjectionTestContext(reverse ? types.Reverse().ToArray() : types);

        context.Project();

        var generic = context.Type(typeof(CycleA<>));
        var derived = Assert.IsType<ClassType>(context.Type(typeof(CycleB)));
        Assert.Same(derived, Assert.IsType<TypeConstraint>(Assert.Single(Assert.Single(generic.TypeParametersList).ConstraintsList)).ConstraintType);
        var baseType = Assert.IsType<ConstructedType>(derived.BaseType);
        Assert.Same(generic, baseType.Definition);
        Assert.Same(derived, Assert.Single(baseType.TypeArguments));
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void StructuralConstraintsNeedTheirElementsWithoutNominalWrapperShells()
    {
        var context = new ConstraintProjectionTestContext(typeof(Structural<>), typeof(StructuralMissing<>), typeof(IMarker<>));

        context.Project();

        var parameter = Assert.Single(context.Type(typeof(Structural<>)).TypeParametersList);
        var marker = Assert.IsType<ConstructedType>(Assert.IsType<TypeConstraint>(Assert.Single(parameter.ConstraintsList)).ConstraintType);
        var tuple = Assert.IsType<TupleType>(Assert.Single(marker.TypeArguments));
        Assert.Equal(8, tuple.ElementTypes.Count);
        Assert.Same(context.Aliases["System.Int32"], tuple.ElementTypes[0]);
        var optional = Assert.IsType<UnionType>(tuple.ElementTypes[1]);
        Assert.Contains(context.Aliases["System.Int32"], optional.Constituents);
        Assert.Contains(context.Aliases["System.Void"], optional.Constituents);
        Assert.False(context.Shells.ContainsKey(AmbientTypeResolver.BuildFQN(typeof(StructuralMissing<>))));
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ClosedNestedTypesPreserveEnclosingAndAliasIdentity()
    {
        var context = new ConstraintProjectionTestContext(typeof(Outer<>.Inner<>), typeof(IMarker<>));
        context.Project();

        var resolved = Assert.IsType<ConstructedType>(context.Resolver.Resolve(typeof(Outer<object>.Inner<IMarker<object>>), new Scope(null)));

        Assert.Same(context.Type(typeof(Outer<>.Inner<>)), resolved.Definition);
        Assert.Same(context.Type(typeof(Outer<>)), resolved.Enclosing!.Definition);
        Assert.Same(context.Aliases["System.Object"], Assert.Single(resolved.Enclosing.TypeArguments));
        Assert.Same(context.Aliases["System.Object"], Assert.Single(Assert.IsType<ConstructedType>(Assert.Single(resolved.TypeArguments)).TypeArguments));
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void InvalidNestedTypeIsUnlinkedFromAnAvailableParent()
    {
        var context = new ConstraintProjectionTestContext(typeof(Outer<>.Invalid<>), typeof(Outer<>.Inner<>), typeof(IMarker<>));
        var invalid = context.Type(typeof(Outer<>.Invalid<>));

        context.Project();

        var parent = context.Type(typeof(Outer<>));
        Assert.DoesNotContain(invalid, parent.NestedTypes);
        Assert.Same(context.Type(typeof(Outer<>.Inner<>)), Assert.Single(parent.NestedTypes));
        _ = Assert.IsType<NotFoundResult>(parent.Scope.Lookup(new LookupRequest("Invalid<>", MemberVisibility.None, null)));
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void TypeProjectorReturnsFailureForDirectAndNestedMissingConstraints()
    {
        foreach(var clrType in new[] { typeof(Direct<>), typeof(Nested<>) })
        {
            var context = new ConstraintProjectionTestContext(clrType, typeof(IMarker<>), typeof(IDeep<>));

            Assert.False(TypeParameterProjector.Project(context.Type(clrType), clrType, context.Resolver));
            Assert.Empty(context.Diagnostics.ToImmutableArray());
        }
    }

    [Fact]
    public void MethodFailuresKeepTheDeclaringTypeAndUnrelatedMethods()
    {
        var context = new ConstraintProjectionTestContext(typeof(Methods), typeof(IMarker<>), typeof(IDeep<>));

        context.Project();

        var methods = context.Type(typeof(Methods)).MethodsList;
        Assert.Equal([nameof(Methods.Valid), nameof(Methods.Siblings), nameof(Methods.FBound), nameof(Methods.Special)], methods.Select(method => method.Name));
        var sibling = methods.Single(method => method.Name == nameof(Methods.Siblings));
        Assert.Same(sibling.TypeParametersList[1], Assert.IsType<TypeConstraint>(Assert.Single(sibling.TypeParametersList[0].ConstraintsList)).ConstraintType);
        Assert.Equal(2, Assert.Single(methods.Single(method => method.Name == nameof(Methods.Special)).TypeParametersList).ConstraintsList.Count);
        Assert.Same(context.Aliases["System.Int32"], methods[0].ReturnType);
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ExtensionsCannotReintroduceMethodsWithFailedBlockOrMethodConstraints()
    {
        var context = new ConstraintProjectionTestContext(typeof(Extensions), typeof(IMarker<>), typeof(IDeep<>), typeof(Direct<>));
        context.Project();

        ExtensionProjector.ProjectExtensions(context.ClrTypes, context.Resolver, context.Model);

        var blocks = context.Model.GetOrAddNamespace(typeof(Extensions).Namespace!).Extensions.Values.SelectMany(blocks => blocks).ToArray();
        Assert.Equal([nameof(Extensions.Valid), nameof(Extensions.Siblings)], blocks.SelectMany(block => block.MethodsList).Select(method => method.Name));
        var siblings = blocks.Single(block => block.MethodsList[0].Name == nameof(Extensions.Siblings));
        Assert.Equal(2, siblings.TypeParametersList.Count);
        Assert.Empty(siblings.MethodsList[0].TypeParametersList);
        Assert.Same(siblings.TypeParametersList[1], Assert.IsType<TypeConstraint>(Assert.Single(siblings.TypeParametersList[0].ConstraintsList)).ConstraintType);
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void FailedConstraintMetadataIsNotAnEmptyConstraintList()
    {
        var context = new ConstraintProjectionTestContext(typeof(Direct<>));
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var unreadable = new UnreadableParameter(typeof(Direct<>).GetGenericArguments()[0]);

        Assert.False(GenericConstraintProjector.Project(parameter, unreadable, parameter.Scope, context.Resolver));
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void FailureDuringConstraintProjectionInvalidatesDependentsBeforeMembers()
    {
        var unreadable = new DelayedFailureType(typeof(Direct<>));
        var context = new ConstraintProjectionTestContext(
            typeof(Dependent<>), unreadable, typeof(InteropFixtures.ConstraintProjection.Missing), typeof(IMarker<>), typeof(Consumer));

        context.Project();

        Assert.False(context.Shells.ContainsKey(AmbientTypeResolver.BuildFQN(typeof(Direct<>))));
        Assert.False(context.Shells.ContainsKey(AmbientTypeResolver.BuildFQN(typeof(Dependent<>))));
        Assert.Equal([nameof(Consumer.Delegate), nameof(Consumer.Valid)], context.Type(typeof(Consumer)).MethodsList.Select(method => method.Name));
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    private sealed class DelayedFailureType : TypeDelegator
    {
        private readonly Type parameter;

        public DelayedFailureType(Type type) : base(type)
        {
            parameter = new DelayedFailureParameter(type.GetGenericArguments()[0]);
        }

        public override bool IsGenericTypeDefinition => true;

        public override Type[] GetGenericArguments()
        {
            return [parameter];
        }
    }

    private sealed class DelayedFailureParameter(Type parameter) : TypeDelegator(parameter)
    {
        private int reads;

        public override GenericParameterAttributes GenericParameterAttributes => typeImpl.GenericParameterAttributes;

        public override Type[] GetGenericParameterConstraints()
        {
            if(reads++ == 0)
            {
                return typeImpl.GetGenericParameterConstraints();
            }

            throw new TypeLoadException("Constraint metadata is unavailable.");
        }
    }

    private sealed class UnreadableParameter(Type parameter) : TypeDelegator(parameter)
    {
        public override Type[] GetGenericParameterConstraints()
        {
            throw new TypeLoadException("Constraint metadata is unavailable.");
        }
    }
}