// Feature: extension-methods, Property 2: Extension Registration Completeness
//
// **Validates: Requirements 8.1, 8.2, 8.3, 8.6, 8.7, 9.1, 20.7**
//
// For any set of N valid extension blocks declared on receiver type T (with
// distinct member signatures), the ExtensionRegistry SHALL contain exactly N
// registered ExtensionBlockInfo entries for T, and the union of their member
// scopes SHALL contain all declared methods and properties.
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for extension registration completeness.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.6, 8.7, 9.1, 20.7**
///
/// For any set of N valid extension blocks declared on receiver type T (with
/// distinct member signatures), the ExtensionRegistry SHALL contain exactly N
/// registered ExtensionBlockInfo entries for T, and the union of their member
/// scopes SHALL contain all declared methods and properties.
/// </summary>
public class ExtensionRegistrationPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ─── Scenario types ──────────────────────────────────────────────────

    /// <summary>
    /// Describes a single member (method or property) within an extension block.
    /// </summary>
#pragma warning disable CA1034
    public record ExtensionMemberInfo(
        string Name,
        bool IsMethod,
        string ReturnType)
    {
        public override string ToString()
        {
            return IsMethod ? $"{ReturnType} {Name}()" : $"{ReturnType} {Name} => ...";
        }
    }

    /// <summary>
    /// Describes a complete scenario: N extension blocks on a single receiver type,
    /// each with distinct member signatures.
    /// </summary>
    public record RegistrationScenario(
        string ReceiverTypeName,
        string ReceiverParamName,
        ImmutableArray<ImmutableArray<ExtensionMemberInfo>> Blocks)
    {
        public int BlockCount => Blocks.Length;

        public int TotalMemberCount => Blocks.Sum(b => b.Length);

        public override string ToString()
        {
            return $"{BlockCount} extension blocks on '{ReceiverTypeName}' with {TotalMemberCount} total members";
        }
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] ReceiverTypeNames =
        ["MyClass", "Widget", "Container", "Service", "Handler"];

    private static readonly string[] ReceiverParamNames =
        ["self", "value", "item"];

    private static readonly string[] MethodNames =
        ["DoWork", "Transform", "Compute", "Execute", "Process",
         "Handle", "Validate", "Convert", "Format", "Render"];

    private static readonly string[] PropertyNames =
        ["Length", "Count", "IsEmpty", "Name", "Status",
         "Size", "Capacity", "IsValid", "Label", "Tag"];

    private static readonly string[] ReturnTypes =
        ["int", "string", "bool", "void", "double"];

    /// <summary>
    /// Generates a single method member info with a unique name from the pool.
    /// </summary>
    private static Gen<ExtensionMemberInfo> GenMethodMember(string name)
    {
        return from retType in Gen.Elements(ReturnTypes)
               select new ExtensionMemberInfo(name, IsMethod: true, retType);
    }

    /// <summary>
    /// Generates a single property member info with a unique name from the pool.
    /// </summary>
    private static Gen<ExtensionMemberInfo> GenPropertyMember(string name)
    {
        // Properties cannot be void
        return from retType in Gen.Elements("int", "string", "bool", "double")
               select new ExtensionMemberInfo(name, IsMethod: false, retType);
    }

    /// <summary>
    /// Generates a registration scenario with 1–5 extension blocks on a single
    /// receiver type, each block having 1–3 members with globally distinct names.
    /// </summary>
    private static Gen<RegistrationScenario> GenScenario()
    {
        return from receiverType in Gen.Elements(ReceiverTypeNames)
               from receiverParam in Gen.Elements(ReceiverParamNames)
               from blockCount in Gen.Choose(1, 5)
               from membersPerBlock in Gen.ArrayOf(Gen.Choose(1, 3), blockCount)
               from scenario in BuildScenario(receiverType, receiverParam, membersPerBlock)
               select scenario;
    }

    private static Gen<RegistrationScenario> BuildScenario(
        string receiverType, string receiverParam, int[] membersPerBlock)
    {
        // Allocate distinct names across all blocks to avoid duplicate signature errors
        var allMethodNames = MethodNames.ToList();
        var allPropertyNames = PropertyNames.ToList();
        var blockGens = new List<Gen<ImmutableArray<ExtensionMemberInfo>>>();
        var methodIdx = 0;
        var propertyIdx = 0;

        foreach(var memberCount in membersPerBlock)
        {
            var memberGens = new List<Gen<ExtensionMemberInfo>>();
            for(var i = 0; i < memberCount; i++)
            {
                // Alternate between methods and properties
                if(i % 2 == 0 && methodIdx < allMethodNames.Count)
                {
                    memberGens.Add(GenMethodMember(allMethodNames[methodIdx]));
                    methodIdx++;
                }
                else if(propertyIdx < allPropertyNames.Count)
                {
                    memberGens.Add(GenPropertyMember(allPropertyNames[propertyIdx]));
                    propertyIdx++;
                }
                else if(methodIdx < allMethodNames.Count)
                {
                    memberGens.Add(GenMethodMember(allMethodNames[methodIdx]));
                    methodIdx++;
                }
            }

            if(memberGens.Count > 0)
            {
                var blockGen = from members in GenCombine(memberGens)
                               select members.ToImmutableArray();
                blockGens.Add(blockGen);
            }
        }

        if(blockGens.Count == 0)
        {
            // Fallback: at least one block with one method
            var fallback = from m in GenMethodMember(allMethodNames[0])
                           select ImmutableArray.Create(m);
            blockGens.Add(fallback);
        }

        return from blocks in GenCombine(blockGens)
               select new RegistrationScenario(
                   receiverType, receiverParam, blocks.ToImmutableArray());
    }

    /// <summary>
    /// Combines a list of generators into a single generator producing a list.
    /// </summary>
    private static Gen<List<T>> GenCombine<T>(List<Gen<T>> gens)
    {
        if(gens.Count == 0)
        {
            return Gen.Constant(new List<T>());
        }

        return gens.Aggregate(
            Gen.Constant(new List<T>()),
            (acc, gen) => from list in acc
                          from item in gen
                          select new List<T>(list) { item });
    }

    public static Arbitrary<RegistrationScenario> ArbitraryRegistrationScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static TypeReferenceNode MakeNamedTypeRef(string name)
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
            }
        };
    }

    private static TypeReferenceNode MakeReturnTypeRef(string typeName)
    {
        return typeName == "void"
            ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
            : MakeNamedTypeRef(typeName);
    }

    private static ExpressionNode MakeLiteralExpression(string typeName)
    {
        var literal = typeName switch
        {
            "bool" => (PrimaryNode)new TrueLiteralExpressionPrimaryNode(),
            "string" => new StringLiteralExpressionPrimaryNode { StringLiteral = Tok("\"x\"") },
            _ => new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("0") },
        };

        var postfix = new PostfixNode
        {
            Target = literal,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExtensionMemberDeclarationNode MakeMethodMember(ExtensionMemberInfo info)
    {
        var body = info.ReturnType == "void"
            ? new BlockNode { Statements = ImmutableArray<StatementNode>.Empty }
            : new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode
                        {
                            Value = MakeLiteralExpression(info.ReturnType),
                        },
                    }),
            };

        var methodDecl = new MethodDeclarationNode
        {
            Name = Tok(info.Name),
            ReturnType = MakeReturnTypeRef(info.ReturnType),
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode { Block = body },
            Modifiers = ImmutableArray<ModifierNode>.Empty,
        };

        return new ExtensionMethodMemberExtensionMemberDeclarationNode
        {
            Method = methodDecl,
        };
    }

    private static ExtensionMemberDeclarationNode MakePropertyMember(ExtensionMemberInfo info)
    {
        var property = new ExpressionBodiedPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            Type = MakeNamedTypeRef(info.ReturnType),
            Name = Tok(info.Name),
            Body = MakeLiteralExpression(info.ReturnType),
        };

        return new ExtensionPropertyMemberExtensionMemberDeclarationNode
        {
            Property = property,
        };
    }

    private static ExtensionMemberDeclarationNode MakeMember(ExtensionMemberInfo info)
    {
        return info.IsMethod ? MakeMethodMember(info) : MakePropertyMember(info);
    }

    private static CompilationUnitNode BuildAst(RegistrationScenario scenario)
    {
        // First, declare the receiver type as a class so it can be resolved
        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok(scenario.ReceiverTypeName),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var classTypeDecl = new BodyTypeTypeDeclarationNode { TypeBody = classDecl };

        // Build extension blocks
        var items = ImmutableArray.CreateBuilder<TopLevelItemNode>();
        items.Add(classTypeDecl.AsTopLevelItem());

        foreach(var block in scenario.Blocks)
        {
            var members = block.Select(MakeMember).ToImmutableArray();

            var extDecl = new ExtensionBlockDeclarationNode
            {
                ReceiverType = MakeNamedTypeRef(scenario.ReceiverTypeName),
                ReceiverName = Tok(scenario.ReceiverParamName),
                Members = members,
            };

            var extItem = new ExtensionItemTopLevelItemNode
            {
                ExtensionDeclaration = extDecl,
            };
            items.Add(extItem);
        }

        return new CompilationUnitNode
        {
            Items = items.ToImmutable(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Extension Registration Completeness
    //
    // **Validates: Requirements 8.1, 8.2, 8.3, 8.6, 8.7, 9.1, 20.7**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any set of N extension blocks on receiver type T, the ExtensionRegistry
    /// SHALL contain exactly N ExtensionBlockInfo entries for T.
    ///
    /// **Validates: Requirements 8.1, 8.2, 8.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ExtensionRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property ExtensionRegistry_ContainsExactlyNEntriesForReceiverType(
        RegistrationScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // Find the receiver type in the model
        var receiverTypeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ReceiverTypeName && s.Kind == SymbolKind.Class);

        if(receiverTypeSymbol is null)
        {
            return false.ToProperty()
                .Label($"Receiver type '{scenario.ReceiverTypeName}' not found in model. " +
                       $"All symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        var receiverType = receiverTypeSymbol.DeclaredType;
        if(receiverType is null)
        {
            return false.ToProperty()
                .Label($"Receiver type '{scenario.ReceiverTypeName}' has null DeclaredType");
        }

        // Query the ExtensionRegistry for exact extensions on this type
        var extensions = model.ExtensionRegistry.GetExactExtensions(receiverType);

        return (extensions.Count == scenario.BlockCount).ToProperty()
            .Label($"Expected {scenario.BlockCount} extension entries for '{scenario.ReceiverTypeName}', " +
                   $"got {extensions.Count}");
    }

    /// <summary>
    /// For any set of N extension blocks on receiver type T, the union of their
    /// member scopes SHALL contain all declared methods and properties.
    ///
    /// **Validates: Requirements 8.6, 8.7, 9.1, 20.7**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ExtensionRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property ExtensionRegistry_MemberScopesContainAllDeclaredMembers(
        RegistrationScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // Find the receiver type in the model
        var receiverTypeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ReceiverTypeName && s.Kind == SymbolKind.Class);

        if(receiverTypeSymbol is null)
        {
            return false.ToProperty()
                .Label($"Receiver type '{scenario.ReceiverTypeName}' not found in model");
        }

        var receiverType = receiverTypeSymbol.DeclaredType;
        if(receiverType is null)
        {
            return false.ToProperty()
                .Label($"Receiver type '{scenario.ReceiverTypeName}' has null DeclaredType");
        }

        // Query the ExtensionRegistry for exact extensions on this type
        var extensions = model.ExtensionRegistry.GetExactExtensions(receiverType);

        // Collect all member names from all extension block member scopes
        var registeredMembers = new HashSet<(string Name, SymbolKind Kind)>();
        foreach(var ext in extensions)
        {
            foreach(var member in ext.MemberScope.Members)
            {
                _ = registeredMembers.Add((member.Name, member.Kind));
            }
        }

        // Verify all expected members are present
        var allExpectedMembers = scenario.Blocks
            .SelectMany(b => b)
            .Select(m => (m.Name, Kind: m.IsMethod ? SymbolKind.Method : SymbolKind.Property))
            .ToList();

        foreach(var (name, kind) in allExpectedMembers)
        {
            if(!registeredMembers.Contains((name, kind)))
            {
                return false.ToProperty()
                    .Label($"Expected member '{name}' ({kind}) not found in extension registry. " +
                           $"Registered members: [{string.Join(", ", registeredMembers.Select(m => $"{m.Name}:{m.Kind}"))}]");
            }
        }

        // Also verify the total count matches (no extra members)
        var totalExpected = allExpectedMembers.Count;
        var totalRegistered = extensions.Sum(e => e.MemberScope.Members.Count);

        return (totalRegistered == totalExpected).ToProperty()
            .Label($"Expected {totalExpected} total members across all extension blocks, " +
                   $"got {totalRegistered}. " +
                   $"Registered: [{string.Join(", ", registeredMembers.Select(m => $"{m.Name}:{m.Kind}"))}]");
    }

    /// <summary>
    /// For any extension block on receiver type T, the registered ExtensionBlockInfo
    /// SHALL have the correct receiver parameter name.
    ///
    /// **Validates: Requirements 8.6**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ExtensionRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property ExtensionRegistry_ReceiverParameterNameIsCorrect(
        RegistrationScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // Find the receiver type in the model
        var receiverTypeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ReceiverTypeName && s.Kind == SymbolKind.Class);

        if(receiverTypeSymbol is null)
        {
            return false.ToProperty()
                .Label($"Receiver type '{scenario.ReceiverTypeName}' not found in model");
        }

        var receiverType = receiverTypeSymbol.DeclaredType;
        if(receiverType is null)
        {
            return false.ToProperty()
                .Label($"Receiver type '{scenario.ReceiverTypeName}' has null DeclaredType");
        }

        var extensions = model.ExtensionRegistry.GetExactExtensions(receiverType);

        foreach(var ext in extensions)
        {
            if(ext.ReceiverParameterName != scenario.ReceiverParamName)
            {
                return false.ToProperty()
                    .Label($"Expected receiver parameter name '{scenario.ReceiverParamName}', " +
                           $"got '{ext.ReceiverParameterName}'");
            }
        }

        return true.ToProperty()
            .Label("All extension blocks have correct receiver parameter name");
    }
}