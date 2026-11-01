using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ArrayFindResultEmissionTests
{
    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnionElementsEnterTheClosedResultCarrierWithAnExplicitAbsentArm(bool containsVoid, bool conditional)
    {
        var constituents = new List<IMistType> { built.Type("Int32"), built.Type("String") };
        if(containsVoid)
        {
            constituents.Add(built.Type("Void"));
        }

        var element = built.Interner.GetOrCreateUnionType(constituents);
        var (method, receiver) = Find(element);
        BodyEmitter.ResetTemporaries();
        var call = conditional
            ? "Mist.Backends.CSharpTranspiler.Runtime.ArrayOperations.IfPresent(values, __e1 => Mist.Backends.CSharpTranspiler.Runtime.ArrayOperations.Find(__e1, predicate))"
            : "Mist.Backends.CSharpTranspiler.Runtime.ArrayOperations.Find(values, predicate)";
        var bound = conditional ? "__e2" : "__e1";
        var carrier = containsVoid ? "Union_Int32_String_Void" : "Union_Int32_String";

        Assert.Equal($"({call} is {carrier} {bound} ? (Union_Int32_String_Void){bound} : (Union_Int32_String_Void)Standard.Void.Instance)",
            CapabilityAdapters.Rewrite(method, "values", conditional ? Optional(receiver) : receiver, ["predicate"], conditional));
    }

    [Fact]
    public void AnOptionalElementDoesNotAddASecondNullableLayer()
    {
        var number = new StructType("Int32", "Standard", null, null, null);
        var (method, receiver) = Find(Optional(number));

        Assert.Equal("((int?)Mist.Backends.CSharpTranspiler.Runtime.ArrayOperations.Find(values, predicate))",
            CapabilityAdapters.Rewrite(method, "values", receiver, ["predicate"]));
    }

    [Fact]
    public void AGenericCallerClosesTheElementBeforeChoosingItsResultConversion()
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var (method, receiver) = Find(parameter);
        var union = built.Interner.GetOrCreateUnionType([built.Type("Int32"), built.Type("String")]);
        using var substitution = TypeNameEmitter.Substituting(TypeSubstitution.ForParameters([parameter], [union]));
        BodyEmitter.ResetTemporaries();

        Assert.Equal("(Mist.Backends.CSharpTranspiler.Runtime.ArrayOperations.Find(values, predicate) is Union_Int32_String __e1 ? (Union_Int32_String_Void)__e1 : (Union_Int32_String_Void)Standard.Void.Instance)",
            CapabilityAdapters.Rewrite(method, "values", receiver, ["predicate"]));
    }

    private (Method Method, ConstructedType Receiver) Find(IMistType element)
    {
        var array = new ClassType("Array<,>", "Standard", null, null, null);
        var parameter = new TypeParameter("TValue", 0, new Scope(null), array);
        array.TypeParametersList.Add(parameter);
        var method = new Method("Find", Optional(parameter),
            [new Parameter("predicate", new FunctionType([parameter], built.Type("Boolean")))],
            new MemberModifiers { IsIntrinsic = true })
        { DeclaringType = array };
        return (method, new ConstructedType(array, [element]));
    }

    private UnionType Optional(IMistType type)
    {
        return new UnionType([type, built.Type("Void")]);
    }
}