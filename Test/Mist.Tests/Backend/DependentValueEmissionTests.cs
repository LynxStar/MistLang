using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// How a dependent parameter's value reaches C#: the property each projecting type declares, a read of
/// it named through the construction that declares it, and a read of the parameter itself, all over
/// hand-built types.
/// </summary>
public sealed class DependentValueEmissionTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    [Trait("Tier", "1")]
    public void AQualifiedReadIsClosedAsItsReceiverClosedIt()
    {
        var (box, _) = Box("Int32");
        var seven = Seven(box);

        var declaring = Assert.IsType<ConstructedType>(ProjectedValueAccess.Declaring(Read(seven, box)));

        Assert.Same(box, declaring.Definition);
        Assert.Equal(7L, Assert.IsType<LiftedType>(Assert.Single(declaring.TypeArguments)).Value);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AnInheritedReadIsClosedThroughTheBaseConstructionAndTheReceiversArguments()
    {
        var (box, _) = Box("Int32");
        var mid = new ClassType("Mid<>", "Probe", new Scope(null), null, null);
        var forwarded = new TypeParameter("M", 0, mid.Scope, mid) { IsDependent = true, Domain = built.Type("Int32") };
        mid.TypeParametersList.Add(forwarded);
        var throughBase = new ConstructedType(box, [forwarded]);
        mid.BaseType = throughBase;
        var three = new ConstructedType(mid, [new LiftedType(built.Type("Int32"), 3L)]);

        var access = new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), three), "N", built.Type("Int32"),
            member: new PropertyResult(box.TypeParametersList[0].ValueProperty!, box, [throughBase]));

        var declaring = Assert.IsType<ConstructedType>(ProjectedValueAccess.Declaring(access));
        Assert.Same(box, declaring.Definition);
        Assert.Equal(3L, Assert.IsType<LiftedType>(Assert.Single(declaring.TypeArguments)).Value);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AReadThroughAnAliasIsClosedAsTheAliasedConstruction()
    {
        var (box, _) = Box("Int32");
        var alias = new TypeAlias("Seven", "Probe", new Scope(null), null, null!) { AliasedType = Seven(box) };

        var declaring = Assert.IsType<ConstructedType>(ProjectedValueAccess.Declaring(Read(alias, box)));

        Assert.Equal(7L, Assert.IsType<LiftedType>(Assert.Single(declaring.TypeArguments)).Value);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AnOrdinaryPropertyReadIsNotAProjection()
    {
        var (box, _) = Box("Int32");
        var count = new Property("Count", built.Type("Int32"), new MemberModifiers { IsStatic = true }) { DeclaringType = box };
        var access = new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), Seven(box)), "Count", built.Type("Int32"),
            member: new PropertyResult(count, box, []));

        Assert.Null(ProjectedValueAccess.Declaring(access));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AProjectedReadIsSpelledOnTheDeclaringConstructionsCarrier()
    {
        var (box, _) = Box("Int32");

        Assert.EndsWith("Box<Const_7>.N", BodyEmitter.Render(Read(Seven(box), box), new DiagnosticBag()), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AnUnqualifiedReadFromAnImplementingTypeIsSpelledOnTheAbstract()
    {
        var shape = new AbstractType("IFixed<>", "Probe", new Scope(null), null, null);
        var size = new TypeParameter("Size", 0, shape.Scope, shape) { IsDependent = true, Domain = built.Type("Int32") };
        shape.TypeParametersList.Add(size);
        TypeParameterDomainResolver.ProjectValues(shape);
        var eight = new ConstructedType(shape, [new LiftedType(built.Type("Int32"), 8L)]);

        var read = new PropertyReferenceExpression(HandBuiltModel.Node(), size.ValueProperty!) { BaseConstructions = [eight] };

        Assert.EndsWith("IFixed<Const_8>.Size", BodyEmitter.Render(read, new DiagnosticBag()), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AProjectingTypeDeclaresItsPropertyAndItsKindsWitness()
    {
        var (box, _) = Box("Int64");
        box.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(box, ConstraintClosureMap.Build(built.Model, diagnostics), built.Model.VariadicUses, writer, diagnostics);

        var lines = writer.ToString().Split('\n').Select(line => line.Trim()).ToList();
        Assert.Contains("where TN : IConstIntegral", lines);
        Assert.Contains("public static long N => ((long)TN.Value);", lines);
    }

    private (ClassType Box, TypeParameter Size) Box(string domain)
    {
        var box = new ClassType("Box<>", "Probe", new Scope(null), null, null);
        var size = new TypeParameter("N", 0, box.Scope, box) { IsDependent = true, Domain = built.Type(domain) };
        box.TypeParametersList.Add(size);
        TypeParameterDomainResolver.ProjectValues(box);
        return (box, size);
    }

    private ConstructedType Seven(ClassType box)
    {
        return new ConstructedType(box, [new LiftedType(built.Type("Int32"), 7L)]);
    }

    private MemberAccessExpression Read(IMistType receiver, ClassType box)
    {
        return new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), receiver), "N", built.Type("Int32"),
            member: new PropertyResult(box.TypeParametersList[0].ValueProperty!, box, []));
    }
}