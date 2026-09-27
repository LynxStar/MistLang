using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PuritySpecFragments
{
    private IReadOnlyList<Diagnostic>? Returns(string name)
    {
        if(!name.StartsWith("owned-return-of-", StringComparison.Ordinal))
        {
            return null;
        }

        var box = HandBuiltModel.Class("Box");
        _ = Field(box, "N", integer);
        var shared = Field(box, "Shared", box, isStatic: true);
        var x = Input("x", box);
        var owned = Input("x", box, OwnershipKind.Owned);
        var b = Variable("b", box, OwnershipKind.Owned);

        var method = name switch
        {
            "owned-return-of-this" => OwnedReturn(box, "Same", box, Public, Returning(new ThisExpression(Node(37), box))),
            "owned-return-of-an-unrestricted-parameter" => OwnedReturn(box, "Id", box, Static, Returning(Ref(x, 37)), x),
            "owned-return-of-a-static-field" => OwnedReturn(box, "Get", box, Static, Returning(StaticRead(shared, 37))),
            "owned-return-of-a-fresh-allocation" => OwnedReturn(box, "Make", box, Static, Returning(Alloc(box, null, 37))),
            "owned-return-of-an-owned-local" => OwnedReturn(box, "Build", box, Static, HandBuiltModel.Block(Declare(b, Alloc(box, null)), Return(Ref(b, 37)))),
            "owned-return-of-an-owned-parameter" => OwnedReturn(box, "Pass", box, Static, Returning(Ref(owned, 37)), owned),
            "owned-return-of-a-string-literal" => OwnedReturn(box, "Text", text, Static, Returning(Literal(text, "text"))),
            "owned-return-of-an-integer-literal" => OwnedReturn(box, "Num", integer, Static, Returning(Number(1))),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        return Judge(new OwnershipAnalyzer(), method);
    }

    private static Method OwnedReturn(ClassType box, string name, IMistType returns, MemberModifiers modifiers, Block body, params LocalVariable[] inputs)
    {
        var method = new Method(name, returns, [.. inputs.Select(input => input.Parameter!)], modifiers)
        {
            DeclaringType = box,
            ReturnOwnership = OwnershipKind.Owned,
            Body = body,
        };
        box.MethodsList.Add(method);
        return method;
    }
}