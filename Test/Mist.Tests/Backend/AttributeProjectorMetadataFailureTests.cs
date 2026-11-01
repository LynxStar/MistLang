using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AttributeProjectorMetadataFailureTests
{
    public static TheoryData<bool, MetadataRead, int> MetadataFailures
    {
        get
        {
            var cases = new TheoryData<bool, MetadataRead, int>();
            foreach(var parameter in new[] { false, true })
            {
                foreach(var read in Enum.GetValues<MetadataRead>().Where(read => read != MetadataRead.None))
                {
                    for(var failure = 0; failure < 5; failure++)
                    {
                        cases.Add(parameter, read, failure);
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(MetadataFailures))]
    public void MetadataFailureOmitsOnlyTheFailingAttribute(bool parameter, MetadataRead read, int failure)
    {
        Exception exception = failure switch
        {
            0 => new TypeLoadException("Missing attribute type"),
            1 => new FileNotFoundException("Missing attribute assembly"),
            2 => new FileLoadException("Unreadable attribute assembly"),
            3 => new BadImageFormatException("Invalid attribute metadata"),
            4 => new NotSupportedException("Unsupported attribute metadata"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

        AssertProjection(parameter, new MetadataAttribute(read, exception));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolvableAttributesPreserveTheirData(bool parameter)
    {
        AssertProjection(parameter, null);
    }

    [Theory]
    [InlineData(false, MetadataRead.AttributeType)]
    [InlineData(false, MetadataRead.ConstructorArguments)]
    [InlineData(false, MetadataRead.NamedArguments)]
    [InlineData(true, MetadataRead.AttributeType)]
    [InlineData(true, MetadataRead.ConstructorArguments)]
    [InlineData(true, MetadataRead.NamedArguments)]
    [InlineData(false, MetadataRead.PositionalElement)]
    [InlineData(false, MetadataRead.NamedElement)]
    [InlineData(true, MetadataRead.PositionalElement)]
    [InlineData(true, MetadataRead.NamedElement)]
    public void OtherFailuresPropagate(bool parameter, MetadataRead read)
    {
        var exception = new InvalidOperationException("Projection bug");
        var target = new List<AttributeData>();

        var actual = Assert.Throws<InvalidOperationException>(() =>
            Project(parameter, [new MetadataAttribute(read, exception)], target));

        Assert.Same(exception, actual);
        Assert.Empty(target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingOutMarkerIsNotDuplicatedAfterMetadataFailure(bool hasOutFlag)
    {
        var marker = new AttributeData(new ClassType("OutAttribute", "System.Runtime.InteropServices", null, null, null), [], []);
        var target = new List<AttributeData> { marker };
        var parameter = new MetadataParameter([new MetadataAttribute(MetadataRead.AttributeType, new TypeLoadException())], hasOutFlag);

        AttributeProjector.Project(parameter, target, new Scope(null), Resolver());

        Assert.Same(marker, Assert.Single(target));
    }

    private static void AssertProjection(bool parameter, CustomAttributeData? failing)
    {
        var sentinel = new AttributeData(new ClassType("Existing", "Probe", null, null, null), [], []);
        var target = new List<AttributeData> { sentinel };
        var valid = new MetadataAttribute();
        IList<CustomAttributeData> attributes = [new OutMetadataAttribute(), valid];
        if(failing is not null)
        {
            attributes.Add(failing);
        }

        attributes.Add(valid);

        Project(parameter, attributes, target);

        Assert.Equal(parameter ? 4 : 3, target.Count);
        Assert.Same(sentinel, target[0]);
        foreach(var attribute in target.Skip(1).Take(2))
        {
            Assert.Equal("System.ObsoleteAttribute", attribute.AttributeType.FullyQualifiedName);
            Assert.Equal("kept", attribute.PositionalArguments[0]);
            Assert.Equal(new object?[] { 1, 2 }, Assert.IsType<object?[]>(attribute.PositionalArguments[1]));
            Assert.Equal(2, attribute.PositionalArguments.Count);
            Assert.Equal(("DiagnosticId", (object?)"MIST1110"), Assert.Single(attribute.NamedArguments));
        }

        if(parameter)
        {
            var marker = target[3];
            Assert.Equal("System.Runtime.InteropServices.OutAttribute", marker.AttributeType.FullyQualifiedName);
            Assert.Empty(marker.PositionalArguments);
            Assert.Empty(marker.NamedArguments);
        }
    }

    private static void Project(bool parameter, IList<CustomAttributeData> attributes, List<AttributeData> target)
    {
        var resolver = Resolver();
        var scope = new Scope(null);
        if(parameter)
        {
            AttributeProjector.Project(new MetadataParameter(attributes, true), target, scope, resolver);
        }
        else
        {
            AttributeProjector.Project(new MetadataMember(attributes), target, scope, resolver);
        }
    }

    private static AmbientTypeResolver Resolver()
    {
        var shells = new Dictionary<string, DeclaredType>
        {
            ["System.ObsoleteAttribute"] = new ClassType("ObsoleteAttribute", "System", null, null, null),
            ["System.Runtime.InteropServices.OutAttribute"] = new ClassType("OutAttribute", "System.Runtime.InteropServices", null, null, null),
        };
        return new AmbientTypeResolver(shells, [], new TypeInterner(), new DiagnosticBag());
    }

    private sealed class MetadataAttribute(MetadataRead failingRead = MetadataRead.None, Exception? failure = null) : CustomAttributeData
    {
        public override Type AttributeType => failingRead == MetadataRead.AttributeType ? throw failure! : typeof(ObsoleteAttribute);

        public override IList<CustomAttributeTypedArgument> ConstructorArguments => failingRead == MetadataRead.ConstructorArguments ? throw failure! :
        [
            new(typeof(string), "kept"),
            new(typeof(int[]), failingRead == MetadataRead.PositionalElement
                ? new UnreadableElements(failure!)
                : new CustomAttributeTypedArgument[] { new(typeof(int), 1), new(typeof(int), 2) }),
        ];

        public override IList<CustomAttributeNamedArgument> NamedArguments => failingRead == MetadataRead.NamedArguments ? throw failure! :
        [
            new(typeof(ObsoleteAttribute).GetProperty(nameof(ObsoleteAttribute.DiagnosticId))!, "MIST1110"),
            .. failingRead == MetadataRead.NamedElement
                ? new CustomAttributeNamedArgument[]
                {
                    new(typeof(ObsoleteAttribute).GetProperty(nameof(ObsoleteAttribute.UrlFormat))!,
                        new CustomAttributeTypedArgument(typeof(int[]), new UnreadableElements(failure!))),
                }
                : Array.Empty<CustomAttributeNamedArgument>(),
        ];
    }

    public enum MetadataRead
    {
        AttributeType,
        ConstructorArguments,
        NamedArguments,
        PositionalElement,
        NamedElement,
        None,
    }

    private sealed class UnreadableElements(Exception failure) : IReadOnlyCollection<CustomAttributeTypedArgument>
    {
        public int Count => 2;

        public IEnumerator<CustomAttributeTypedArgument> GetEnumerator()
        {
            yield return new CustomAttributeTypedArgument(typeof(int), 1);
            throw failure;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class OutMetadataAttribute : CustomAttributeData
    {
        public override Type AttributeType => typeof(OutAttribute);
    }

    private sealed class MetadataParameter : ParameterInfo
    {
        private readonly IList<CustomAttributeData> attributes;

        public MetadataParameter(IList<CustomAttributeData> attributes, bool hasOutFlag)
        {
            this.attributes = attributes;
            AttrsImpl = hasOutFlag ? ParameterAttributes.Out : ParameterAttributes.None;
        }

        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            return attributes;
        }
    }

    private sealed class MetadataMember(IList<CustomAttributeData> attributes) : MemberInfo
    {
        public override Type? DeclaringType => null;
        public override MemberTypes MemberType => MemberTypes.Method;
        public override string Name => "Probe";
        public override Type? ReflectedType => null;

        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            return attributes;
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotSupportedException();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotSupportedException();
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotSupportedException();
        }
    }
}