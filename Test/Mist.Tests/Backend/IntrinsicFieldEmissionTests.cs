using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class IntrinsicFieldEmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFieldReadUsesTheClrFieldAndRefusesMissingSupply(bool bare)
    {
        var built = new HandBuiltModel();
        var owner = built.Type("Int32");
        owner.Modifiers = new MemberModifiers { IsIntrinsic = true };
        var field = new Field("MaxValue", owner, OwnershipKind.Unrestricted,
            new MemberModifiers { IsIntrinsic = true, IsStatic = true, IsReadonly = true })
        { DeclaringType = owner };
        owner.FieldsList.Add(field);
        var unit = new CompilationUnit { SourcePath = "Int32.mist" };
        unit.Types.Add(owner);
        built.Model.AddStandardAssembly().CompilationUnits.Add(unit);
        Expression read = bare ? new FieldReferenceExpression(HandBuiltModel.Node(), field)
            : new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), owner),
                "MaxValue", owner, member: new FieldResult(field, owner, []));
        var diagnostics = new DiagnosticBag();
        using(IntrinsicSupply.Enter(built.Model, Type.GetType))
        {
            Assert.Equal("int.MaxValue", BodyEmitter.Render(read, diagnostics));
            Assert.Empty(diagnostics.ToImmutableArray());
        }

        using(IntrinsicSupply.Enter(built.Model, _ => null))
        {
            Assert.DoesNotContain("int.MaxValue", BodyEmitter.Render(read, diagnostics));
            var refusal = Assert.Single(diagnostics.ToImmutableArray());
            Assert.Equal(BackendDiagnostics.MissingIntrinsicEmissionTemplate.Number, refusal.Number);
            Assert.Contains("Standard.Int32.MaxValue", refusal.Message);
        }
    }
}