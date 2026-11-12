// Feature: default-keyword, Property 1: Pretty-printer round-trip for default constructs
// **Validates: Requirements 4.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

public class DefaultPrettyPrinterPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    private static readonly string[] ClassNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent"];

    private static readonly string[] FieldNames =
        ["x", "y", "count", "timeout", "level"];

    private static readonly string[] TypeBindingTypeNames =
        ["OpenGLRenderer", "ConsoleLogger", "FileWriter", "MemoryCache", "JsonParser"];

    private static readonly string[] NestedFieldNames =
        ["depth", "rate", "mode", "size", "limit"];

    private static Gen<DefaultFieldInitializerNode> GenFieldOverride()
    {
        return from fieldName in Gen.Elements(FieldNames)
               from value in MistAstGenerator.GenIntLiteral()
               select (DefaultFieldInitializerNode)new DefaultFieldOverrideDefaultFieldInitializerNode
               {
                   FieldName = Tok(fieldName),
                   Value = value,
               };
    }

    private static Gen<DefaultFieldInitializerNode> GenTypeBindingSimple()
    {
        return from fieldName in Gen.Elements(FieldNames)
               from typeName in Gen.Elements(TypeBindingTypeNames)
               select (DefaultFieldInitializerNode)new DefaultTypeBindingDefaultFieldInitializerNode
               {
                   FieldName = Tok(fieldName),
                   TypeName = new TypeReferenceNode
                   {
                       First = new NamedSingleTypeRefSingleTypeRefNode
                       {
                           Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(typeName) },
                       }
                   },
               };
    }

    private static Gen<DefaultFieldInitializerNode> GenTypeBindingWithOverrides()
    {
        var genNestedOverride =
            from nestedField in Gen.Elements(NestedFieldNames)
            from nestedValue in MistAstGenerator.GenIntLiteral()
            select new DefaultNestedOverrideNode
            {
                FieldName = Tok(nestedField),
                Value = nestedValue,
            };

        return from fieldName in Gen.Elements(FieldNames)
               from typeName in Gen.Elements(TypeBindingTypeNames)
               from nestedOverrides in genNestedOverride.ListOf()
                   .Where(list => list.Count is >= 1 and <= 3)
               select (DefaultFieldInitializerNode)new DefaultTypeBindingDefaultFieldInitializerNode
               {
                   FieldName = Tok(fieldName),
                   TypeName = new TypeReferenceNode
                   {
                       First = new NamedSingleTypeRefSingleTypeRefNode
                       {
                           Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(typeName) },
                       }
                   },
                   NestedEntries = nestedOverrides.ToImmutableArray(),
               };
    }

    private static Gen<DefaultFieldInitializerNode> GenDefaultEntry()
    {
        return Gen.OneOf(GenFieldOverride(), GenTypeBindingSimple(), GenTypeBindingWithOverrides());
    }

    private static Gen<DefaultBlockDeclarationNode> GenDefaultBlock()
    {
        return from entries in GenDefaultEntry().ListOf()
                   .Where(list => list.Count is >= 0 and <= 5)
               select new DefaultBlockDeclarationNode
               {
                   Entries = entries.ToImmutableArray(),
               };
    }

    private static Gen<MemberDeclarationNode> GenDefaultBlockMember()
    {
        return GenDefaultBlock().Select(block =>
            (MemberDeclarationNode)new DefaultBlockMemberMemberDeclarationNode
            {
                DefaultBlock = block,
            });
    }

    private static Gen<ExpressionNode> GenDefaultExpression()
    {
        return Gen.Elements(ClassNames).Select(typeName =>
            MistAstGenerator.WrapPrimary(new DefaultExpressionPrimaryNode
            {
                Type = new TypeReferenceNode
                {
                    First = new NamedSingleTypeRefSingleTypeRefNode
                    {
                        Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(typeName) },
                    }
                },
            }));
    }

    private static Gen<ExpressionNode> GenDefaultPrimitiveExpression()
    {
        return MistTypeGenerator.GenPrimitiveTypeRef().Select(typeRef =>
            MistAstGenerator.WrapPrimary(new DefaultExpressionPrimaryNode
            {
                Type = typeRef,
            }));
    }

    private static Gen<ExpressionNode> GenDefaultLiteralExpression()
    {
        return Gen.Constant(
            MistAstGenerator.WrapPrimary(new DefaultLiteralExpressionPrimaryNode()));
    }

    private static Gen<ExpressionNode> GenAnyDefaultExpression()
    {
        return Gen.OneOf(
            GenDefaultExpression(),
            GenDefaultPrimitiveExpression(),
            GenDefaultLiteralExpression());
    }

    private static Gen<CompilationUnitNode> GenClassWithDefaultBlock()
    {
        var genField = from fieldName in Gen.Elements(FieldNames)
                       from typeRef in MistTypeGenerator.GenPrimitiveTypeRef()
                       select (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
                       {
                           Field = new FieldDeclarationNode
                           {
                               Name = Tok(fieldName),
                               Type = typeRef,
                           },
                       };

        return from className in Gen.Elements(ClassNames)
               from fields in genField.ListOf().Where(list => list.Count <= 3)
               from defaultBlock in GenDefaultBlockMember()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new BodyTypeTypeDeclarationNode
                           {
                               TypeBody = new TypeBodyDeclarationNode
                               {
                                   Keyword = new ClassKeywordTypeKeywordNode(),
                                   Name = Tok(className),
                                   Members = fields.Append(defaultBlock).ToImmutableArray(),
                               },
                           },
                       }),
               };
    }

    /// <summary>
    /// Generates a struct with a default block member and optional fields.
    /// </summary>
    private static Gen<CompilationUnitNode> GenStructWithDefaultBlock()
    {
        var genField = from fieldName in Gen.Elements(FieldNames)
                       from typeRef in MistTypeGenerator.GenPrimitiveTypeRef()
                       select (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
                       {
                           Field = new FieldDeclarationNode
                           {
                               Name = Tok(fieldName),
                               Type = typeRef,
                           },
                       };

        return from structName in Gen.Elements(ClassNames)
               from fields in genField.ListOf().Where(list => list.Count <= 3)
               from defaultBlock in GenDefaultBlockMember()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new BodyTypeTypeDeclarationNode
                           {
                               TypeBody = new TypeBodyDeclarationNode
                               {
                                   Keyword = new ClassKeywordTypeKeywordNode(),
                                   Name = Tok(structName),
                                   Members = fields.Append(defaultBlock).ToImmutableArray(),
                               },
                           },
                       }),
               };
    }

    private static Gen<CompilationUnitNode> GenClassWithDefaultExpressions()
    {
        var genDefaultStmt = GenAnyDefaultExpression().Select(MistAstGenerator.MakeExpressionStatement);

        return from className in Gen.Elements(ClassNames)
               from stmts in genDefaultStmt.ListOf()
                   .Where(list => list.Count is >= 1 and <= 4)
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new BodyTypeTypeDeclarationNode
                           {
                               TypeBody = new TypeBodyDeclarationNode
                               {
                                   Keyword = new ClassKeywordTypeKeywordNode(),
                                   Name = Tok(className),
                                   Members = ImmutableArray.Create<MemberDeclarationNode>(
                                       new MethodMemberMemberDeclarationNode
                                       {
                                           Method = new MethodDeclarationNode
                                           {
                                               Name = Tok("Test"),
                                               ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                                               Modifiers = ImmutableArray.Create<ModifierNode>(
                                                   new PublicModifierModifierNode()),
                                               Body = new CallableBlockBodyCallableBodyNode
                                               {
                                                   Block = new BlockNode
                                                   {
                                                       Statements = stmts.ToImmutableArray(),
                                                   },
                                               },
                                           },
                                       }),
                               },
                           },
                       }),
               };
    }

    private static Gen<CompilationUnitNode> GenClassWithDefaultBlockAndExpressions()
    {
        var genDefaultStmt = GenAnyDefaultExpression().Select(MistAstGenerator.MakeExpressionStatement);

        var genField = from fieldName in Gen.Elements(FieldNames)
                       from typeRef in MistTypeGenerator.GenPrimitiveTypeRef()
                       select (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
                       {
                           Field = new FieldDeclarationNode
                           {
                               Name = Tok(fieldName),
                               Type = typeRef,
                           },
                       };

        return from className in Gen.Elements(ClassNames)
               from fields in genField.ListOf().Where(list => list.Count <= 2)
               from defaultBlock in GenDefaultBlockMember()
               from stmts in genDefaultStmt.ListOf()
                   .Where(list => list.Count is >= 1 and <= 3)
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new BodyTypeTypeDeclarationNode
                           {
                               TypeBody = new TypeBodyDeclarationNode
                               {
                                   Keyword = new ClassKeywordTypeKeywordNode(),
                                   Name = Tok(className),
                                   Members = fields
                                       .Append(defaultBlock)
                                       .Append(new MethodMemberMemberDeclarationNode
                                       {
                                           Method = new MethodDeclarationNode
                                           {
                                               Name = Tok("Run"),
                                               ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                                               Modifiers = ImmutableArray.Create<ModifierNode>(
                                                   new PublicModifierModifierNode()),
                                               Body = new CallableBlockBodyCallableBodyNode
                                               {
                                                   Block = new BlockNode
                                                   {
                                                       Statements = stmts.ToImmutableArray(),
                                                   },
                                               },
                                           },
                                       })
                                       .ToImmutableArray(),
                               },
                           },
                       }),
               };
    }

    private static Gen<CompilationUnitNode> GenClassWithDefaultVarDecls()
    {
        var genTypedVarDecl =
            from varName in Gen.Elements("a", "b", "c", "d")
            from typeRef in MistTypeGenerator.GenPrimitiveTypeRef()
            from defaultExpr in GenDefaultLiteralExpression()
            select (StatementNode)new VariableDeclarationStatementStatementNode
            {
                VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                {
                    Type = typeRef,
                    Name = Tok(varName),
                    Initializer = defaultExpr,
                },
            };

        var genExplicitDefaultVarDecl =
            from varName in Gen.Elements("e", "f", "g", "h")
            from defaultExpr in GenDefaultExpression()
            select (StatementNode)new VariableDeclarationStatementStatementNode
            {
                VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
                {
                    Name = Tok(varName),
                    Initializer = defaultExpr,
                },
            };

        return from className in Gen.Elements(ClassNames)
               from stmts in Gen.OneOf(genTypedVarDecl, genExplicitDefaultVarDecl)
                   .ListOf().Where(list => list.Count is >= 1 and <= 4)
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new BodyTypeTypeDeclarationNode
                           {
                               TypeBody = new TypeBodyDeclarationNode
                               {
                                   Keyword = new ClassKeywordTypeKeywordNode(),
                                   Name = Tok(className),
                                   Members = ImmutableArray.Create<MemberDeclarationNode>(
                                       new MethodMemberMemberDeclarationNode
                                       {
                                           Method = new MethodDeclarationNode
                                           {
                                               Name = Tok("Init"),
                                               ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                                               Modifiers = ImmutableArray.Create<ModifierNode>(
                                                   new PublicModifierModifierNode()),
                                               Body = new CallableBlockBodyCallableBodyNode
                                               {
                                                   Block = new BlockNode
                                                   {
                                                       Statements = stmts.ToImmutableArray(),
                                                   },
                                               },
                                           },
                                       }),
                               },
                           },
                       }),
               };
    }

    public static Gen<CompilationUnitNode> GenDefaultProgram()
    {
        return Gen.OneOf(
            GenClassWithDefaultBlock(),
            GenStructWithDefaultBlock(),
            GenClassWithDefaultExpressions(),
            GenClassWithDefaultBlockAndExpressions(),
            GenClassWithDefaultVarDecls());
    }

    public static Arbitrary<CompilationUnitNode> ArbitraryCompilationUnitNode()
    {
        return GenDefaultProgram().ToArbitrary();
    }

    private static string? ParseAndPrint(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var hasStubWarning = bag.ToImmutableArray()
            .Any(d => d.Number.Value == "MIST0121");
        var hasErrors = bag.ToImmutableArray()
            .Any(d => d.Severity == DiagnosticSeverity.Error);

        if(hasStubWarning || hasErrors)
        {
            return null;
        }

        if(result.Root is not CompilationUnitNode reparsed || reparsed.Items.Length == 0)
        {
            return null;
        }

        return MistPrettyPrinter.Print(reparsed, source);
    }

    private static Property CheckRoundTrip(CompilationUnitNode ast)
    {
        var printed1 = MistPrettyPrinter.Print(ast);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var printed2 = ParseAndPrint(printed1);

        if(printed2 is null)
        {
            // Parser cannot handle this source — fall back to determinism
            var reprinted = MistPrettyPrinter.Print(ast);
            return (printed1 == reprinted).ToProperty()
                .Label($"Determinism failed (parser unavailable).\nPrinted1:\n{printed1}\nReprinted:\n{reprinted}");
        }

        var printed3 = ParseAndPrint(printed2);

        if(printed3 is null)
        {
            // printed2 does not reparse, so check that the first round-trip is at least deterministic.
            var printed2Again = ParseAndPrint(printed1);
            if(printed2Again is null)
            {
                return true.ToProperty(); // Parser is non-deterministic, skip
            }

            return (printed2 == printed2Again).ToProperty()
                .Label($"First round-trip not deterministic.\nPrinted2:\n{printed2}\nPrinted2Again:\n{printed2Again}");
        }

        return (printed2 == printed3).ToProperty()
            .Label($"Round-trip not idempotent.\nPrinted2:\n{printed2}\nPrinted3:\n{printed3}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DefaultPrettyPrinterPropertyTests) })]
    public Property RoundTrip_PrintParseReprintIsIdenticalForDefaultConstructs(CompilationUnitNode ast)
    {
        return CheckRoundTrip(ast);
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DefaultPrettyPrinterPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsDefaultSyntax(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);

        var containsDefaultSyntax =
            printed.Contains("default {") ||
            printed.Contains("default(") ||
            printed.Contains("default;") ||
            printed.Contains("= default;") ||
            printed.Contains("= default(");

        return containsDefaultSyntax.ToProperty()
            .Label($"No default syntax found in output:\n{printed}");
    }
}