using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ExceptionErrorAdaptationTests
{
    [Fact]
    public void ErrorEntryCallsTheSelectedAdapterWithoutDuplicatingTheOriginal()
    {
        var exception = new ClassType("Exception", "System", null, null, null);
        var error = new AbstractType("Error", "Standard", null, null, null);
        var adapter = new ClassType("ExceptionError", "Standard", null, null, null);
        adapter.BaseAbstractTypes.Add(error);
        var conversion = Conversion(adapter, exception, adapter);
        var operation = new Method("Original", exception, [], new MemberModifiers { IsStatic = true });
        var call = new InvocationExpression(Node(), new MethodReferenceExpression(Node(), operation), [], exception, operation, []);
        var converted = new ImplicitConversionExpression(Node(), call, error, [new ConversionFunction(conversion, exception, adapter)]);
        var writer = new CSharpWriter();
        BodyEmitter.EmitEntering(converted, error, writer, new DiagnosticBag());

        Assert.Equal("((ExceptionError)(Original()))", writer.ToString());
    }
}