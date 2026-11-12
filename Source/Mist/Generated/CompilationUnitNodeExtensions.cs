using System.Collections.Immutable;

namespace Mist.Generated;

/// <summary>
/// <see cref="CompilationUnitNode"/> holds namespaces, types and statements in one list of top-level items;
/// these read that list by item kind and build it.
/// </summary>
public static class CompilationUnitNodeExtensions
{
    /// <summary>
    /// The first namespace item, or null.
    /// </summary>
    public static NamespaceDeclarationNode? GetNamespace(this CompilationUnitNode cu)
    {
        foreach(var item in cu.Items)
        {
            if(item is NamespaceItemTopLevelItemNode nsItem)
            {
                return nsItem.NamespaceDeclaration;
            }
        }

        return null;
    }

    public static ImmutableArray<TypeDeclarationNode> GetTypes(this CompilationUnitNode cu)
    {
        var builder = ImmutableArray.CreateBuilder<TypeDeclarationNode>();
        foreach(var item in cu.Items)
        {
            if(item is TypeItemTopLevelItemNode typeItem)
            {
                builder.Add(typeItem.TypeDeclaration);
            }
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<StatementItemTopLevelItemNode> GetTopLevelStatements(this CompilationUnitNode cu)
    {
        var builder = ImmutableArray.CreateBuilder<StatementItemTopLevelItemNode>();
        foreach(var item in cu.Items)
        {
            if(item is StatementItemTopLevelItemNode stmtItem)
            {
                builder.Add(stmtItem);
            }
        }

        return builder.ToImmutable();
    }

    public static bool HasTopLevelStatements(this CompilationUnitNode cu)
    {
        foreach(var item in cu.Items)
        {
            if(item is StatementItemTopLevelItemNode)
            {
                return true;
            }
        }

        return false;
    }

    public static TopLevelItemNode AsTopLevelItem(this TypeDeclarationNode typeDecl)
    {
        return new TypeItemTopLevelItemNode { TypeDeclaration = typeDecl };
    }

    public static TopLevelItemNode AsTopLevelItem(this NamespaceDeclarationNode nsDecl)
    {
        return new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsDecl };
    }

    public static ImmutableArray<TopLevelItemNode> AsTopLevelItems(this ImmutableArray<TypeDeclarationNode> types)
    {
        var builder = ImmutableArray.CreateBuilder<TopLevelItemNode>(types.Length);
        foreach(var t in types)
        {
            builder.Add(t.AsTopLevelItem());
        }

        return builder.MoveToImmutable();
    }

    public static CompilationUnitNode CreateCompilationUnit(
        ImmutableArray<UsingDeclarationNode>? usings = null,
        NamespaceDeclarationNode? ns = null,
        ImmutableArray<TypeDeclarationNode>? types = null)
    {
        var items = ImmutableArray.CreateBuilder<TopLevelItemNode>();

        if(ns is not null)
        {
            items.Add(ns.AsTopLevelItem());
        }

        if(types is not null)
        {
            foreach(var t in types.Value)
            {
                items.Add(t.AsTopLevelItem());
            }
        }

        return new CompilationUnitNode
        {
            Usings = usings ?? ImmutableArray<UsingDeclarationNode>.Empty,
            Items = items.ToImmutable(),
        };
    }

    public static NamespaceBodyItemNode AsNamespaceBodyItem(this TypeDeclarationNode typeDecl)
    {
        return new NamespaceTypeItemNamespaceBodyItemNode { TypeDeclaration = typeDecl };
    }

    public static ImmutableArray<NamespaceBodyItemNode> AsNamespaceBodyItems(this ImmutableArray<TypeDeclarationNode> types)
    {
        var builder = ImmutableArray.CreateBuilder<NamespaceBodyItemNode>(types.Length);
        foreach(var t in types)
        {
            builder.Add(t.AsNamespaceBodyItem());
        }

        return builder.MoveToImmutable();
    }
}

/// <summary>
/// A callable body is a block or a bare <c>;</c> (Mist Standard Library spec Requirements 4.1, 5.2). Whether
/// the no-body form is legal on a declaration, for example only with <c>intrinsic</c>, is enforced by the
/// semantic model, not the grammar.
/// </summary>
public static class CallableBodyNodeExtensions
{
    public static BlockNode? GetBlock(this CallableBodyNode body)
    {
        return body switch
        {
            CallableBlockBodyCallableBodyNode blockBody => blockBody.Block,
            _ => null,
        };
    }
}

public static class MethodBodyFactory
{
    public static CallableBodyNode AsCallableBody(this BlockNode block)
    {
        return new CallableBlockBodyCallableBodyNode { Block = block };
    }

    /// <summary>
    /// Backwards-compatible alias for <see cref="AsCallableBody"/>.
    /// </summary>
    public static CallableBodyNode AsMethodBody(this BlockNode block)
    {
        return new CallableBlockBodyCallableBodyNode { Block = block };
    }
}