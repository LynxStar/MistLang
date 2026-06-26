// Backward-compatible extension methods for CompilationUnitNode.
// The grammar changed from:
//   CompilationUnit = usings:UsingDeclaration* namespace:NamespaceDeclaration? types:TypeDeclaration*
// to:
//   CompilationUnit = usings:UsingDeclaration* items:TopLevelItem*
//
// These extensions provide the old Namespace/Types accessors and factory methods
// so existing code can work with minimal changes.

using System.Collections.Immutable;

namespace Mist.Generated;

/// <summary>
/// Extension methods on CompilationUnitNode for backward compatibility with the old grammar structure.
/// </summary>
public static class CompilationUnitNodeExtensions
{
    /// <summary>
    /// Gets the first namespace declaration from the Items list, or null if none exists.
    /// Equivalent to the old <c>cu.Namespace</c> property.
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

    /// <summary>
    /// Gets all top-level type declarations from the Items list.
    /// Equivalent to the old <c>cu.Types</c> property.
    /// </summary>
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

    /// <summary>
    /// Gets all top-level statement items from the Items list.
    /// Returns the <see cref="StatementItemTopLevelItemNode"/> wrappers for each statement at file scope.
    /// </summary>
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

    /// <summary>
    /// Returns <c>true</c> if the compilation unit contains any top-level statements.
    /// </summary>
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

    /// <summary>
    /// Wraps a single TypeDeclarationNode into a TopLevelItemNode.
    /// </summary>
    public static TopLevelItemNode AsTopLevelItem(this TypeDeclarationNode typeDecl)
    {
        return new TypeItemTopLevelItemNode { TypeDeclaration = typeDecl };
    }

    /// <summary>
    /// Wraps a NamespaceDeclarationNode into a TopLevelItemNode.
    /// </summary>
    public static TopLevelItemNode AsTopLevelItem(this NamespaceDeclarationNode nsDecl)
    {
        return new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsDecl };
    }

    /// <summary>
    /// Converts an array of TypeDeclarationNode into an array of TopLevelItemNode.
    /// </summary>
    public static ImmutableArray<TopLevelItemNode> AsTopLevelItems(this ImmutableArray<TypeDeclarationNode> types)
    {
        var builder = ImmutableArray.CreateBuilder<TopLevelItemNode>(types.Length);
        foreach(var t in types)
        {
            builder.Add(t.AsTopLevelItem());
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Creates a CompilationUnitNode with the old-style parameters for backward compatibility.
    /// </summary>
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

    /// <summary>
    /// Wraps a single TypeDeclarationNode into a NamespaceBodyItemNode.
    /// </summary>
    public static NamespaceBodyItemNode AsNamespaceBodyItem(this TypeDeclarationNode typeDecl)
    {
        return new NamespaceTypeItemNamespaceBodyItemNode { TypeDeclaration = typeDecl };
    }

    /// <summary>
    /// Converts an array of TypeDeclarationNode into an array of NamespaceBodyItemNode.
    /// Used for backward compatibility when constructing namespace bodies.
    /// </summary>
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
/// Extension methods for <see cref="MethodBodyNode"/> and <see cref="OperatorBodyNode"/>.
///
/// The grammar was changed to let method, constructor, and operator declarations
/// omit their body with a trailing <c>;</c> (see Requirements 4.1, 5.2 of the
/// Mist Standard Library spec). The body field is now a discriminated node:
///   MethodBody   = block:Block as MethodBlockBody   | SEMICOLON as MethodNoBody
///   OperatorBody = block:Block as OperatorBlockBody | SEMICOLON as OperatorNoBody
///
/// For non-intrinsic, non-abstract members the body is still a block and most
/// callers want the old <see cref="BlockNode"/> directly. These helpers unwrap
/// a block body when one is present and return <c>null</c> for a no-body form.
/// Whether a no-body form is legal on a particular declaration (e.g., requires
/// <c>intrinsic</c>) is enforced in the semantic model, not the grammar.
/// </summary>
public static class MethodBodyNodeExtensions
{
    /// <summary>
    /// Returns the <see cref="BlockNode"/> for a block-bodied method/constructor
    /// body, or <c>null</c> for a no-body (<c>;</c>-terminated) form.
    /// </summary>
    public static BlockNode? GetBlock(this MethodBodyNode body)
    {
        return body switch
        {
            MethodBlockBodyMethodBodyNode blockBody => blockBody.Block,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the <see cref="BlockNode"/> for a block-bodied operator body,
    /// or <c>null</c> for a no-body (<c>;</c>-terminated) form.
    /// </summary>
    public static BlockNode? GetBlock(this OperatorBodyNode body)
    {
        return body switch
        {
            OperatorBlockBodyOperatorBodyNode blockBody => blockBody.Block,
            _ => null,
        };
    }
}

/// <summary>
/// Factory helpers for constructing <see cref="MethodBodyNode"/> and
/// <see cref="OperatorBodyNode"/> instances from a <see cref="BlockNode"/>.
///
/// These exist so test helpers and AST-construction utilities that used to
/// assign <c>Body = new BlockNode { ... }</c> directly can continue to
/// express a block body concisely without having to wrap it in the
/// generated discriminated variant each time.
/// </summary>
public static class MethodBodyFactory
{
    /// <summary>
    /// Wraps the given <see cref="BlockNode"/> in a
    /// <see cref="MethodBlockBodyMethodBodyNode"/>, suitable for assignment to
    /// <see cref="MethodDeclarationNode.Body"/> or
    /// <see cref="ConstructorDeclarationNode.Body"/>.
    /// </summary>
    public static MethodBodyNode AsMethodBody(this BlockNode block)
    {
        return new MethodBlockBodyMethodBodyNode { Block = block };
    }

    /// <summary>
    /// Wraps the given <see cref="BlockNode"/> in an
    /// <see cref="OperatorBlockBodyOperatorBodyNode"/>, suitable for assignment
    /// to <see cref="OperatorDeclarationNode.Body"/>.
    /// </summary>
    public static OperatorBodyNode AsOperatorBody(this BlockNode block)
    {
        return new OperatorBlockBodyOperatorBodyNode { Block = block };
    }
}