using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures.HeritageTupleNames;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientHeritageTupleNameTests
{
    private static readonly Type[] Fixtures =
    [
        typeof(IPairs<>), typeof(IOther<>), typeof(PairBase<>), typeof(NamedBase), typeof(UnnamedBase),
        typeof(NamedInterfaces), typeof(UnnamedInterface), typeof(PartlyNamedInterface), typeof(GenericNamedInterface<,>),
        typeof(Container<>.Items), typeof(INamedChild), typeof(LongNamedInterface), typeof(NamedConstraint<>),
        typeof(UnnamedConstraint<>), typeof(NamedMethodConstraint),
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaseTypeNamesComeFromTheTypeDefinition(bool metadataOnly)
    {
        var context = Project(metadataOnly);

        _ = Named(Argument(Assert.IsType<ClassType>(context.Type(typeof(NamedBase))).BaseType), "Left", "Right");
        _ = Assert.IsType<TupleType>(Argument(Assert.IsType<ClassType>(context.Type(typeof(UnnamedBase))).BaseType));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachImplementedInterfaceTakesItsOwnRowsNames(bool metadataOnly)
    {
        var context = Project(metadataOnly);

        _ = Named(Interface(context, typeof(NamedInterfaces), typeof(IPairs<>)), "Key", "Value");
        _ = Named(Interface(context, typeof(NamedInterfaces), typeof(IOther<>)), "Low", "High");
        _ = Named(Interface(context, typeof(PartlyNamedInterface), typeof(IPairs<>)), "Named", null);
        _ = Named(Interface(context, typeof(INamedChild), typeof(IPairs<>)), "Up", "Down");
        _ = Named(Interface(context, typeof(LongNamedInterface), typeof(IPairs<>)), "A", "B", "C", "D", "E", "F", "G", "H");
        _ = Assert.IsType<TupleType>(Interface(context, typeof(UnnamedInterface), typeof(IPairs<>)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterfaceRowsOverTypeParametersKeepThoseParameters(bool metadataOnly)
    {
        var context = Project(metadataOnly);

        var generic = context.Type(typeof(GenericNamedInterface<,>));
        var pair = Named(Interface(context, typeof(GenericNamedInterface<,>), typeof(IPairs<>)), "First", "Second");
        Assert.Equal(generic.TypeParametersList, pair.ElementTypes);

        var container = context.Type(typeof(Container<>));
        var queued = Named(Interface(context, typeof(Container<>.Items), typeof(IPairs<>)), "Element", "Priority");
        Assert.Same(Assert.Single(container.TypeParametersList), queued.ElementTypes[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeAndMethodParameterConstraintsTakeTheirRowsNames(bool metadataOnly)
    {
        var context = Project(metadataOnly);

        _ = Named(Constraint(Assert.Single(context.Type(typeof(NamedConstraint<>)).TypeParametersList)), "Key", "Value");
        _ = Assert.IsType<TupleType>(Constraint(Assert.Single(context.Type(typeof(UnnamedConstraint<>)).TypeParametersList)));
        var take = context.Type(typeof(NamedMethodConstraint)).MethodsList.Single(method => method.Name == nameof(NamedMethodConstraint.Take));
        _ = Named(Constraint(Assert.Single(take.TypeParametersList)), "Ticks", "Done");
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void RowsThatCannotBeReadLeaveTheTupleUnnamed()
    {
        var rows = new TupleNameRows();
        var iface = typeof(NamedInterfaces).GetInterfaces().Single(type => type.GetGenericTypeDefinition() == typeof(IPairs<>));
        var parameter = typeof(NamedConstraint<>).GetGenericArguments()[0];
        var constraint = Assert.Single(parameter.GetGenericParameterConstraints());

        Assert.NotNull(rows.OfInterface(typeof(NamedInterfaces), iface));
        Assert.NotNull(rows.OfConstraint(parameter, constraint));
        Assert.Null(rows.OfInterface(new Elsewhere(typeof(NamedInterfaces), new MissingModule()), iface));
        Assert.Null(rows.OfInterface(new Elsewhere(typeof(NamedInterfaces), typeof(AmbientHeritageTupleNameTests).Module), iface));
        Assert.Null(rows.OfConstraint(new Elsewhere(parameter, new MissingModule()), constraint));
    }

    private static ConstraintProjectionTestContext Project(bool metadataOnly)
    {
        var location = typeof(IPairs<>).Assembly.Location;
        using var metadata = AmbientProviderTestHelper.RuntimeResolvingContext(location);
        var loaded = metadata.LoadFromAssemblyPath(location);
        var context = new ConstraintProjectionTestContext(
            metadataOnly ? [.. Fixtures.Select(fixture => loaded.GetType(fixture.FullName!)!)] : Fixtures);
        context.Aliases["System.String"] = new ClassType("String", "Standard", null, null, null);
        context.Aliases["System.Boolean"] = new StructType("Boolean", "Standard", null, null, null);

        context.Project();

        return context;
    }

    private static IMistType Argument(IMistType? constructed)
    {
        return Assert.Single(Assert.IsType<ConstructedType>(constructed).TypeArguments);
    }

    private static IMistType Interface(ConstraintProjectionTestContext context, Type implementer, Type definition)
    {
        var target = context.Type(definition);
        return Argument(context.Type(implementer).BaseAbstractTypes
            .Single(type => type is ConstructedType constructed && ReferenceEquals(constructed.Definition, target)));
    }

    private static IMistType Constraint(TypeParameter parameter)
    {
        return Argument(Assert.IsType<TypeConstraint>(Assert.Single(parameter.ConstraintsList)).ConstraintType);
    }

    private static NamedTupleType Named(IMistType type, params string?[] names)
    {
        var tuple = Assert.IsType<NamedTupleType>(type);
        Assert.Equal(names, tuple.ElementNames);
        return tuple;
    }

    private sealed class Elsewhere(Type type, Module module) : TypeDelegator(type)
    {
        public override Module Module => module;
    }

    private sealed class MissingModule : Module
    {
        public override string FullyQualifiedName => Path.Combine(AppContext.BaseDirectory, "missing", "Absent.dll");
    }
}