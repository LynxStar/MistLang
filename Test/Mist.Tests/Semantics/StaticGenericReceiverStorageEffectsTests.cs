using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.StorageWrites;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class StaticGenericReceiverStorageEffectsTests
{
    [Fact]
    public void AStaticCallClassifiesWritesUsingTheConstructedReceiver()
    {
        var fragments = new BindingFragments();
        var voidType = fragments.Built.Type("Void");
        var intType = fragments.Built.Type("Int32");
        var staticMember = DeclarationFragments.Public with { IsStatic = true };
        var cell = HandBuiltModel.Class("Cell<>");
        var t = new TypeParameter("T", 0, cell.Scope, cell);
        cell.TypeParametersList.Add(t);
        var current = new Field("Current", t, OwnershipKind.Unrestricted, staticMember) { DeclaringType = cell };
        var saved = new Field("Saved", t, OwnershipKind.Unrestricted, staticMember) { DeclaringType = cell };
        cell.FieldsList.AddRange([current, saved]);

        var assignment = new AssignmentExpression(HandBuiltModel.Node(),
            new FieldReferenceExpression(HandBuiltModel.Node(), current),
            new FieldReferenceExpression(HandBuiltModel.Node(), saved), t);
        var restore = new Method("Restore", voidType, [], staticMember)
        {
            DeclaringType = cell,
            Body = HandBuiltModel.Block(new ExpressionStatement(assignment)),
        };
        cell.MethodsList.Add(restore);

        var caller = HandBuiltModel.Class("Caller<,>");
        var u = new TypeParameter("U", 0, caller.Scope, caller);
        var v = new TypeParameter("V", 1, caller.Scope, caller);
        caller.TypeParametersList.AddRange([u, v]);
        var restoreGeneric = createCaller("RestoreGeneric", u);
        var restoreConcrete = createCaller("RestoreConcrete", intType);

        var writes = StorageWriteSummaries.Solve(
            [
                new(restore, StorageAccess.Read, restore.Body, cell, [t]),
                new(restoreGeneric, StorageAccess.Read, restoreGeneric.Body, caller, [u, v]),
                new(restoreConcrete, StorageAccess.Read, restoreConcrete.Body, caller, [u, v]),
            ], fragments.Builder);

        Assert.Same(t, Assert.Single(writes[(restore, StorageAccess.Read)]));
        Assert.Same(u, Assert.Single(writes[(restoreGeneric, StorageAccess.Read)]));
        Assert.Empty(writes[(restoreConcrete, StorageAccess.Read)]);

        Method createCaller(string name, IMistType typeArgument)
        {
            var owner = fragments.Built.Interner.GetOrCreateConstructedType(cell, [typeArgument]);
            var receiver = new TypeReferenceExpression(HandBuiltModel.Node(), owner);
            var callee = new MemberAccessExpression(HandBuiltModel.Node(), receiver, restore.Name, voidType);
            var call = new InvocationExpression(HandBuiltModel.Node(), callee, [], voidType, restore, parameterTypes: []);
            var method = new Method(name, voidType, [], staticMember)
            {
                DeclaringType = caller,
                Body = HandBuiltModel.Block(new ExpressionStatement(call)),
            };
            caller.MethodsList.Add(method);
            return method;
        }
    }
}