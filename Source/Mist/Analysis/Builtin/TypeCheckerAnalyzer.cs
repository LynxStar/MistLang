using System.Collections.Immutable;
using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Built-in analyzer that verifies Mist type rules against the semantic model.
/// Walks ASTs via the semantic model and checks each expression/statement
/// using expression types computed by the SemanticModelBuilder (Pass 2).
/// </summary>
public sealed class TypeCheckerAnalyzer : IAnalyzer
{
    public string AnalyzerId => "TypeChecker";

    // ── Numeric types for operator checking ──────────────────────────────
    private static readonly HashSet<IType> NumericTypes =
    [
        PrimitiveType.Int, PrimitiveType.Uint,
        PrimitiveType.Long, PrimitiveType.Ulong,
        PrimitiveType.Short, PrimitiveType.Ushort,
        PrimitiveType.Byte, PrimitiveType.Sbyte,
        PrimitiveType.Float, PrimitiveType.Double,
        PrimitiveType.Decimal,
    ];

    // ── Current enclosing type during analysis (set per method/constructor) ──
    private IType? _currentEnclosingType;

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Walk each method and constructor body to check type rules.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                    CheckMethodBody(symbol, model, diagnostics);
                    break;
                case SymbolKind.Constructor:
                    CheckConstructorBody(symbol, model, diagnostics);
                    break;
                case SymbolKind.Field:
                    CheckFieldDeclaration(symbol, model, diagnostics);
                    break;
            }
        }

        // Entry point validation — once per compilation (R10.1, R10.2, R10.3).
        CheckEntryPoint(model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Entry point validation (R10.1, R10.2, R10.3)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that exactly one class contains a valid entry point:
    /// <c>static void Main()</c> or <c>static void Main(string[] args)</c>.
    /// Emits MIST_TC_0017 if no entry point is found, MIST_TC_0018 if multiple are found.
    /// </summary>
    private static void CheckEntryPoint(ISemanticModel model, DiagnosticBag diagnostics)
    {
        var entryPoints = new List<(ISymbol Symbol, SyntaxNode Node)>();

        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind != SymbolKind.Method || symbol.Name != "Main")
                continue;

            // Get the declaring AST node to inspect modifiers and parameters.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            if (declaringNode is not MethodDeclarationNode methodDecl)
                continue;

            // Must be static.
            var isStatic = false;
            foreach (var mod in methodDecl.Modifiers)
            {
                if (mod is StaticModifierModifierNode)
                {
                    isStatic = true;
                    break;
                }
            }
            if (!isStatic)
                continue;

            // Must return void.
            if (methodDecl.ReturnType is not VoidTypeRefTypeReferenceNode)
                continue;

            // Must have either no parameters or a single string[] parameter.
            if (methodDecl.Parameters is null)
            {
                // static void Main() — valid entry point.
                entryPoints.Add((symbol, declaringNode));
            }
            else
            {
                // Check for exactly one parameter of type string[].
                var parameters = new List<ParameterNode>();
                foreach (var child in methodDecl.Parameters.Children())
                {
                    if (child is ParameterNode param)
                        parameters.Add(param);
                }

                if (parameters.Count == 1)
                {
                    var paramType = parameters[0].Type;
                    // string[] is represented as PrimitiveTypeRef with StringType and an array rank,
                    // or as a NamedTypeRef. Check for the string[] pattern.
                    if (IsStringArrayTypeRef(paramType))
                    {
                        entryPoints.Add((symbol, declaringNode));
                    }
                }
                // Any other parameter count or type is not a valid entry point signature.
            }
        }

        if (entryPoints.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_TC_0017"),
                DiagnosticSeverity.Error,
                PipelineStage.Analysis,
                "Program does not contain a valid entry point (static void Main)",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: "TypeChecker"));
        }
        else if (entryPoints.Count > 1)
        {
            // Emit a diagnostic listing all entry point locations.
            foreach (var (_, node) in entryPoints)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0018",
                    "Program contains multiple entry points",
                    node));
            }
        }
    }

    /// <summary>
    /// Checks whether a type reference AST node represents <c>string[]</c>.
    /// </summary>
    private static bool IsStringArrayTypeRef(TypeReferenceNode typeRef)
    {
        // string[] as a primitive type ref: PrimitiveTypeRef with StringType and array rank.
        if (typeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef)
        {
            return primitiveRef.PrimitiveType is StringTypePrimitiveTypeNode
                   && primitiveRef.ArrayRank.HasValue;
        }

        // string[] could also appear as a named type ref (unlikely but handle it).
        // In practice, the grammar parses "string" as a PrimitiveType keyword,
        // so this case shouldn't occur for valid Mist code.
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Enclosing type resolution — determines the type that contains a symbol
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds the declaring type (ClassType or StructType) that contains the given
    /// method or constructor symbol by walking up the scope chain.
    /// </summary>
    private static IType? FindDeclaringType(ISymbol memberSymbol)
    {
        // The member's ContainingScope is the type scope. Look for a Class/Struct
        // symbol in the parent scope that owns this type scope.
        var typeScope = memberSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if (parentScope is null)
            return null;

        foreach (var member in parentScope.Members)
        {
            if (member.Kind is SymbolKind.Class or SymbolKind.Struct
                && member.DeclaredType is ClassType or StructType)
            {
                // Check if this type's MemberScope is the same as the member's ContainingScope.
                var memberScope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };
                if (ReferenceEquals(memberScope, typeScope))
                    return member.DeclaredType;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Top-level check entry points
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckMethodBody(ISymbol methodSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        if (declaringNode is not MethodDeclarationNode methodDecl)
            return;

        var returnType = methodSymbol.DeclaredType ?? UnknownType.Instance;
        _currentEnclosingType = FindDeclaringType(methodSymbol);
        CheckBlock(methodDecl.Body, returnType, model, diagnostics);
    }

    private void CheckConstructorBody(ISymbol ctorSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(ctorSymbol); }
        catch { return; }

        if (declaringNode is not ConstructorDeclarationNode ctorDecl)
            return;

        _currentEnclosingType = FindDeclaringType(ctorSymbol);

        // Check constructor chaining (: this(args))
        if (ctorDecl.Chain is not null)
        {
            CheckConstructorChain(ctorDecl.Chain, ctorSymbol, model, diagnostics);
        }

        CheckBlock(ctorDecl.Body, VoidType.Instance, model, diagnostics);
    }

    private static void CheckFieldDeclaration(ISymbol fieldSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(fieldSymbol); }
        catch { return; }

        if (declaringNode is not FieldDeclarationNode fieldDecl)
            return;

        // Field initializers: the grammar stores the initializer as part of the
        // field declaration. The semantic model builder resolves the field type.
        // For field initializers, we check that the expression type is assignable
        // to the field's declared type. However, the current AST stores the
        // initializer as a Token? (the EQUAL token), not as an ExpressionNode.
        // The actual expression is in the Children() of the field declaration.
        // Since the semantic model builder already handles field type resolution,
        // and field initializer expressions are not separately tracked in the
        // expression type map for the current grammar structure, we skip
        // field initializer type checking here. The C# backend will catch
        // any remaining issues.
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Block and statement checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckBlock(BlockNode block, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var stmt in block.Statements)
        {
            CheckStatement(stmt, returnType, model, diagnostics);
        }
    }

    private void CheckStatement(StatementNode stmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                CheckVariableDeclarationStatement(varDeclStmt.VariableDeclaration, model, diagnostics);
                break;
            case ReturnStatementStatementNode returnStmt:
                CheckReturnStatement(returnStmt.ReturnStatement, returnType, model, diagnostics);
                break;
            case IfStatementStatementNode ifStmt:
                CheckIfStatement(ifStmt.IfStatement, returnType, model, diagnostics);
                break;
            case WhileStatementStatementNode whileStmt:
                CheckWhileStatement(whileStmt.WhileStatement, returnType, model, diagnostics);
                break;
            case ForStatementStatementNode forStmt:
                CheckForStatement(forStmt.ForStatement, returnType, model, diagnostics);
                break;
            case ForeachStatementStatementNode foreachStmt:
                CheckForeachStatement(foreachStmt.ForeachStatement, returnType, model, diagnostics);
                break;
            case ExpressionStatementStatementNode exprStmt:
                CheckExpression(exprStmt.ExpressionStatement.Expression, model, diagnostics);
                break;
            case BreakStatementStatementNode:
                // No type checking needed for break.
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Variable declaration checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckVariableDeclarationStatement(
        VariableDeclarationStatementNode varDecl,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
            {
                // Check the initializer expression.
                CheckExpression(typed.Initializer, model, diagnostics);

                // Verify RHS type is assignable to declared type (MIST_TC_0001).
                var declaredType = model.GetTypeOf(typed.Type);
                var initType = model.GetTypeOf(typed.Initializer);
                if (declaredType is not null && initType is not null)
                {
                    CheckAssignability(initType, declaredType, typed.Initializer, diagnostics);
                }
                break;
            }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
            {
                // var with initializer — check the initializer expression.
                CheckExpression(inferred.Initializer, model, diagnostics);

                // MIST_TC_0009: var without initializer.
                // The grammar requires an initializer for var declarations,
                // but if the initializer expression type is null, the var
                // type cannot be inferred.
                var inferredType = model.GetTypeOf(inferred.Initializer);
                if (inferredType is null)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0009",
                        "'var' declaration requires an initializer",
                        inferred));
                }
                break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Return statement checking (MIST_TC_0008)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckReturnStatement(
        ReturnStatementNode returnStmt,
        IType methodReturnType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (IsUnknown(methodReturnType))
            return;

        if (returnStmt.Value is not null)
        {
            // return expr; — check the expression and verify type.
            CheckExpression(returnStmt.Value, model, diagnostics);
            var valueType = model.GetTypeOf(returnStmt.Value);

            if (methodReturnType is VoidType)
            {
                // Void methods must not return a value.
                if (valueType is not null && !IsUnknown(valueType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0008",
                        $"Cannot return '{valueType.DisplayName}' from method with return type 'void'",
                        returnStmt));
                }
            }
            else if (valueType is not null)
            {
                if (!IsAssignable(valueType, methodReturnType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0008",
                        $"Cannot return '{valueType.DisplayName}' from method with return type '{methodReturnType.DisplayName}'",
                        returnStmt));
                }
            }
        }
        else
        {
            // return; — only valid in void methods.
            if (methodReturnType is not VoidType)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0008",
                    $"Cannot return '' from method with return type '{methodReturnType.DisplayName}'",
                    returnStmt));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Control flow condition checking (MIST_TC_0007)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckIfStatement(IfStatementNode ifStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(ifStmt.Condition, model, diagnostics);
        CheckConditionIsBool(ifStmt.Condition, model, diagnostics);
        CheckBlock(ifStmt.ThenBranch, returnType, model, diagnostics);

        if (ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
        {
            CheckIfStatement(elseIf.ElseIf, returnType, model, diagnostics);
        }
        else if (ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
        {
            CheckBlock(elseBlock.ElseBlock, returnType, model, diagnostics);
        }
    }

    private void CheckWhileStatement(WhileStatementNode whileStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(whileStmt.Condition, model, diagnostics);
        CheckConditionIsBool(whileStmt.Condition, model, diagnostics);
        CheckBlock(whileStmt.Body, returnType, model, diagnostics);
    }

    private void CheckForStatement(ForStatementNode forStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Check initializer.
        if (forStmt.Initializer is ForInitializerVariableDeclarationForInitializerNode varInit)
        {
            CheckExpression(varInit.Initializer, model, diagnostics);
            // Check assignability of initializer to declared type.
            var declaredType = model.GetTypeOf(varInit.VariableDeclaration);
            var initType = model.GetTypeOf(varInit.Initializer);
            if (declaredType is not null && initType is not null)
            {
                CheckAssignability(initType, declaredType, varInit.Initializer, diagnostics);
            }
        }
        else if (forStmt.Initializer is ForInitializerExpressionForInitializerNode exprInit)
        {
            CheckExpression(exprInit.Expression, model, diagnostics);
        }

        // Check condition.
        if (forStmt.Condition is not null)
        {
            CheckExpression(forStmt.Condition, model, diagnostics);
            CheckConditionIsBool(forStmt.Condition, model, diagnostics);
        }

        // Check iterator.
        if (forStmt.Iterator is not null)
        {
            CheckExpression(forStmt.Iterator, model, diagnostics);
        }

        CheckBlock(forStmt.Body, returnType, model, diagnostics);
    }

    private void CheckForeachStatement(ForeachStatementNode foreachStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(foreachStmt.Collection, model, diagnostics);
        CheckBlock(foreachStmt.Body, returnType, model, diagnostics);
    }

    private static void CheckConditionIsBool(ExpressionNode condition, ISemanticModel model, DiagnosticBag diagnostics)
    {
        var condType = model.GetTypeOf(condition);
        if (condType is null || IsUnknown(condType))
            return;

        if (condType is not PrimitiveType pt || pt != PrimitiveType.Bool)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0007",
                $"Condition must be of type 'bool', but found '{condType.DisplayName}'",
                condition));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Expression checking — recursive walk
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckExpression(ExpressionNode expr, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckAssignmentNode(expr.Assignment, model, diagnostics);
    }

    private void CheckAssignmentNode(AssignmentNode assignment, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckLogicalOrNode(assignment.Target, model, diagnostics);

        if (assignment.Value is not null)
        {
            // This is an assignment: target = value.
            CheckAssignmentNode(assignment.Value, model, diagnostics);

            var lhsType = model.GetTypeOf(assignment.Target);
            var rhsType = model.GetTypeOf(assignment.Value);

            if (lhsType is not null && rhsType is not null)
            {
                CheckAssignability(rhsType, lhsType, assignment, diagnostics);
            }
        }
    }

    private void CheckLogicalOrNode(LogicalOrNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckLogicalAndNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckLogicalAndNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (lhsType != PrimitiveType.Bool || rhsType != PrimitiveType.Bool)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0006",
                        "Operator '||' requires operands of type 'bool'",
                        node));
                }
            }
        }
    }

    private void CheckLogicalAndNode(LogicalAndNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckEqualityNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckEqualityNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (lhsType != PrimitiveType.Bool || rhsType != PrimitiveType.Bool)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0006",
                        "Operator '&&' requires operands of type 'bool'",
                        node));
                }
            }
        }
    }

    private void CheckEqualityNode(EqualityNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckRelationalNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckRelationalNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            var op = node.Operator.Lexeme ?? "==";

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (!AreEqualityCompatible(lhsType, rhsType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0005",
                        $"Operator '{op}' cannot be applied to operands of type '{lhsType.DisplayName}' and '{rhsType.DisplayName}'",
                        node));
                }
            }
        }
    }

    private void CheckRelationalNode(RelationalNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckAdditiveNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckAdditiveNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            var op = node.Operator.Lexeme ?? "<";

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (!IsNumeric(lhsType) || !IsNumeric(rhsType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0004",
                        $"Operator '{op}' cannot be applied to operands of type '{lhsType.DisplayName}' and '{rhsType.DisplayName}'",
                        node));
                }
            }
        }
    }

    private void CheckAdditiveNode(AdditiveNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckMultiplicativeNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckMultiplicativeNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            var op = node.Operator.Lexeme ?? "+";

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                // + allows string concatenation.
                if (op == "+")
                {
                    var lhsIsString = lhsType is StringType;
                    var rhsIsString = rhsType is StringType;
                    if (lhsIsString || rhsIsString)
                    {
                        // String concatenation is allowed.
                        return;
                    }
                }

                if (!IsNumeric(lhsType) || !IsNumeric(rhsType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0003",
                        $"Operator '{op}' cannot be applied to operands of type '{lhsType.DisplayName}' and '{rhsType.DisplayName}'",
                        node));
                }
            }
        }
    }

    private void CheckMultiplicativeNode(MultiplicativeNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckUnaryNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckUnaryNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            var op = node.Operator.Lexeme ?? "*";

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (!IsNumeric(lhsType) || !IsNumeric(rhsType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0003",
                        $"Operator '{op}' cannot be applied to operands of type '{lhsType.DisplayName}' and '{rhsType.DisplayName}'",
                        node));
                }
            }
        }
    }

    private void CheckUnaryNode(UnaryNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case LogicalNotUnaryNode logicalNot:
            {
                CheckUnaryNode(logicalNot.Operand, model, diagnostics);
                var operandType = model.GetTypeOf(logicalNot.Operand);
                if (operandType is not null && !IsUnknown(operandType))
                {
                    if (operandType != PrimitiveType.Bool)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0006",
                            "Operator '!' requires operands of type 'bool'",
                            logicalNot));
                    }
                }
                break;
            }
            case NegateUnaryNode negate:
            {
                CheckUnaryNode(negate.Operand, model, diagnostics);
                var operandType = model.GetTypeOf(negate.Operand);
                if (operandType is not null && !IsUnknown(operandType))
                {
                    if (!IsNumeric(operandType))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0003",
                            $"Operator '-' cannot be applied to operands of type '{operandType.DisplayName}' and '{operandType.DisplayName}'",
                            negate));
                    }
                }
                break;
            }
            case PostfixExpressionUnaryNode postfix:
                CheckPostfixNode(postfix.Primary, model, diagnostics);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Postfix and primary expression checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckPostfixNode(PostfixNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckPrimaryNode(node.Target, model, diagnostics);

        // Determine if the target is a type name (for static access checks).
        var targetIsTypeName = IsTargetTypeName(node.Target, model);

        // Track the running type of the target as we process suffixes.
        var currentType = model.GetTypeOf(node.Target);

        foreach (var suffix in node.Suffix)
        {
            switch (suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                    CheckMethodCallArguments(methodCall, currentType, model, diagnostics);
                    CheckMemberAccessModifiers(methodCall.Name.Lexeme, currentType, targetIsTypeName, methodCall, model, diagnostics);
                    currentType = model.GetTypeOf(suffix);
                    // After the first suffix, subsequent accesses are on instances (the result of the method call).
                    targetIsTypeName = false;
                    break;

                case InvocationSuffixPostfixSuffixNode invocation:
                    CheckInvocationArguments(invocation, currentType, model, diagnostics);
                    currentType = model.GetTypeOf(suffix);
                    targetIsTypeName = false;
                    break;

                case IndexSuffixPostfixSuffixNode indexSuffix:
                    CheckIndexSuffix(indexSuffix, currentType, model, diagnostics);
                    currentType = model.GetTypeOf(suffix);
                    targetIsTypeName = false;
                    break;

                case MemberAccessSuffixPostfixSuffixNode memberAccess:
                    CheckMemberAccessModifiers(memberAccess.Name.Lexeme, currentType, targetIsTypeName, memberAccess, model, diagnostics);
                    currentType = model.GetTypeOf(suffix);
                    // After accessing a field, subsequent accesses are on the field's value (an instance).
                    targetIsTypeName = false;
                    break;
            }
        }
    }

    private void CheckPrimaryNode(PrimaryNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case ArrayCreationExpressionPrimaryNode arrayCreation:
                CheckArrayCreation(arrayCreation, model, diagnostics);
                break;

            case ObjectCreationExpressionPrimaryNode objectCreation:
                CheckObjectCreation(objectCreation, model, diagnostics);
                break;

            case ParenthesizedExpressionPrimaryNode paren:
                CheckExpression(paren.Inner, model, diagnostics);
                break;

            case InterpolatedStringExpressionPrimaryNode interpolated:
                foreach (var part in interpolated.InterpolatedString.Parts)
                {
                    if (part is ExpressionPartInterpolatedPartNode exprPart)
                    {
                        CheckExpression(exprPart.Expression, model, diagnostics);
                    }
                }
                break;

            // Literals and identifiers don't need type checking.
            default:
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Method invocation checking (MIST_TC_0002)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckMethodCallArguments(
        MethodCallSuffixPostfixSuffixNode methodCall,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check argument expressions recursively.
        if (methodCall.Arguments is not null)
        {
            CheckArgumentListExpressions(methodCall.Arguments, model, diagnostics);
        }

        // Look up the method symbol to check argument types against parameter types.
        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if (methodSymbol is null || methodSymbol.Kind != SymbolKind.Method)
            return;

        CheckArgumentTypes(methodCall.Arguments, methodSymbol, model, diagnostics, methodCall);
    }

    private void CheckInvocationArguments(
        InvocationSuffixPostfixSuffixNode invocation,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check argument expressions recursively.
        if (invocation.Arguments is not null)
        {
            CheckArgumentListExpressions(invocation.Arguments, model, diagnostics);
        }

        // Look up the invoked symbol.
        var invokedSymbol = model.GetDeclaredSymbol(invocation);
        if (invokedSymbol is null)
            return;

        if (invokedSymbol.Kind == SymbolKind.Method)
        {
            CheckArgumentTypes(invocation.Arguments, invokedSymbol, model, diagnostics, invocation);
        }
    }

    private void CheckArgumentListExpressions(ArgumentListNode argList, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // The ArgumentListNode has an Expression property and additional expressions in Children.
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr)
            {
                CheckExpression(expr, model, diagnostics);
            }
        }
    }

    private static void CheckArgumentTypes(
        ArgumentListNode? argList,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        SyntaxNode callSite)
    {
        // Get parameter symbols from the method's containing scope.
        // The semantic model builder registers parameters in the method's scope.
        SyntaxNode methodNode;
        try { methodNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        ImmutableArray<ISymbol> paramSymbols;
        if (methodNode is MethodDeclarationNode methodDecl && methodDecl.Parameters is not null)
        {
            var paramList = new List<ISymbol>();
            foreach (var child in methodDecl.Parameters.Children())
            {
                if (child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if (paramSym is not null)
                        paramList.Add(paramSym);
                }
            }
            paramSymbols = [.. paramList];
        }
        else
        {
            return; // No parameters to check.
        }

        // Collect argument expressions.
        var argExprs = new List<ExpressionNode>();
        if (argList is not null)
        {
            foreach (var child in argList.Children())
            {
                if (child is ExpressionNode expr)
                    argExprs.Add(expr);
            }
        }

        // Check each argument against the corresponding parameter.
        var count = Math.Min(argExprs.Count, paramSymbols.Length);
        for (var i = 0; i < count; i++)
        {
            var argType = model.GetTypeOf(argExprs[i]);
            var paramType = paramSymbols[i].DeclaredType;

            if (argType is not null && paramType is not null)
            {
                if (!IsAssignable(argType, paramType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0002",
                        $"Argument type '{argType.DisplayName}' is not assignable to parameter type '{paramType.DisplayName}'",
                        argExprs[i]));
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Array operation checking (MIST_TC_0010, MIST_TC_0012)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckArrayCreation(
        ArrayCreationExpressionPrimaryNode arrayCreation,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the size expression.
        CheckExpression(arrayCreation.Size, model, diagnostics);

        var sizeType = model.GetTypeOf(arrayCreation.Size);
        if (sizeType is not null && !IsUnknown(sizeType))
        {
            if (sizeType != PrimitiveType.Int)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0012",
                    $"Array size must be of type 'int', but found '{sizeType.DisplayName}'",
                    arrayCreation.Size));
            }
        }
    }

    private void CheckIndexSuffix(
        IndexSuffixPostfixSuffixNode indexSuffix,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the index expression.
        CheckExpression(indexSuffix.Index, model, diagnostics);

        var indexType = model.GetTypeOf(indexSuffix.Index);
        if (indexType is not null && !IsUnknown(indexType))
        {
            if (indexType != PrimitiveType.Int)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0010",
                    $"Array index must be of type 'int', but found '{indexType.DisplayName}'",
                    indexSuffix.Index));
            }
        }

        // Verify the target is an array type (the target type should be ArrayType).
        // If targetType is not an array, the semantic model builder would have
        // already flagged it or assigned UnknownType. We don't emit a separate
        // diagnostic here to avoid cascading.
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Object creation checking (MIST_TC_0011)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckObjectCreation(
        ObjectCreationExpressionPrimaryNode objectCreation,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check argument expressions.
        if (objectCreation.Arguments is not null)
        {
            CheckArgumentListExpressions(objectCreation.Arguments, model, diagnostics);
        }

        // Get the type being constructed.
        var createdType = model.GetTypeOf(objectCreation);
        if (createdType is null || IsUnknown(createdType))
            return;

        // Find constructors on the type.
        IScope? memberScope = createdType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return;

        // Collect argument types.
        var argTypes = CollectArgumentTypes(objectCreation.Arguments, model);

        // Find matching constructor.
        var constructors = memberScope.Members
            .Where(m => m.Kind == SymbolKind.Constructor)
            .ToList();

        if (constructors.Count == 0)
        {
            // If no constructors are defined and there are no arguments, it's OK
            // (default constructor). Otherwise, emit diagnostic.
            if (argTypes.Count > 0)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0011",
                    $"Type '{createdType.DisplayName}' does not contain a constructor matching the given arguments",
                    objectCreation));
            }
            return;
        }

        // Check if any constructor matches the argument types.
        var hasMatch = constructors.Any(ctor => ConstructorMatches(ctor, argTypes, model));
        if (!hasMatch)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0011",
                $"Type '{createdType.DisplayName}' does not contain a constructor matching the given arguments",
                objectCreation));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor chain checking (MIST_TC_0013)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckConstructorChain(
        ConstructorChainNode chain,
        ISymbol ctorSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check argument expressions.
        if (chain.Arguments is not null)
        {
            CheckArgumentListExpressions(chain.Arguments, model, diagnostics);
        }

        // Find the enclosing type.
        var enclosingType = FindEnclosingType(ctorSymbol);
        if (enclosingType is null || IsUnknown(enclosingType))
            return;

        IScope? memberScope = enclosingType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return;

        var argTypes = CollectArgumentTypes(chain.Arguments, model);

        // Find other constructors on the same type (excluding the current one).
        var otherConstructors = memberScope.Members
            .Where(m => m.Kind == SymbolKind.Constructor && !ReferenceEquals(m, ctorSymbol))
            .ToList();

        var hasMatch = otherConstructors.Any(ctor => ConstructorMatches(ctor, argTypes, model));
        if (!hasMatch)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0013",
                $"No constructor on '{enclosingType.DisplayName}' matches the chained arguments",
                chain));
        }
    }

    private static bool ConstructorMatches(ISymbol ctor, List<IType> argTypes, ISemanticModel model)
    {
        // Get the constructor's parameters.
        SyntaxNode ctorNode;
        try { ctorNode = model.GetDeclaringNode(ctor); }
        catch { return false; }

        if (ctorNode is not ConstructorDeclarationNode ctorDecl)
            return false;

        var paramTypes = new List<IType>();
        if (ctorDecl.Parameters is not null)
        {
            foreach (var child in ctorDecl.Parameters.Children())
            {
                if (child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if (paramSym?.DeclaredType is not null)
                        paramTypes.Add(paramSym.DeclaredType);
                }
            }
        }

        if (argTypes.Count != paramTypes.Count)
            return false;

        for (var i = 0; i < argTypes.Count; i++)
        {
            if (!IsAssignable(argTypes[i], paramTypes[i]))
                return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Access modifier enforcement (MIST_TC_0014, MIST_TC_0015, MIST_TC_0016)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines whether the target of a postfix expression is a type name
    /// (e.g., <c>Console</c> in <c>Console.WriteLine()</c>) rather than an instance.
    /// </summary>
    private static bool IsTargetTypeName(PrimaryNode target, ISemanticModel model)
    {
        if (target is not IdentifierExpressionPrimaryNode identifier)
            return false;

        // If the identifier resolves to a type (ClassType or StructType), it's a type name access.
        var resolvedType = model.GetTypeOf(target);
        return resolvedType is ClassType or StructType;
    }

    /// <summary>
    /// Checks access modifier rules for a member access or method call.
    /// Emits MIST_TC_0014 (private access), MIST_TC_0015 (static via instance),
    /// or MIST_TC_0016 (instance via type name).
    /// </summary>
    private void CheckMemberAccessModifiers(
        string memberName,
        IType? targetType,
        bool targetIsTypeName,
        SyntaxNode accessNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (targetType is null || IsUnknown(targetType))
            return;

        // Find the member symbol on the target type.
        var memberSymbol = FindMemberSymbol(targetType, memberName, model);
        if (memberSymbol is null)
            return;

        // Backend symbols are assumed public and valid — skip access modifier checks
        // for them since they have no AST declaring node with modifiers.
        // The type checker treats all symbols uniformly for type checking, but
        // access modifier enforcement only applies to user-defined members.
        if (memberSymbol.Origin == SymbolOrigin.Backend)
            return;

        // Get the declaring AST node to inspect modifiers.
        SyntaxNode? declaringNode;
        try { declaringNode = model.GetDeclaringNode(memberSymbol); }
        catch { return; }

        // Extract modifier information from the declaring node.
        var modifiers = GetMemberModifiers(declaringNode);

        // R9.1, R9.3: Private member access check.
        // Members with no modifier default to private (R9.3).
        if (!modifiers.IsPublic)
        {
            // Private members are only accessible within the declaring type.
            var declaringType = GetDeclaringTypeOfMember(memberSymbol);
            if (declaringType is not null && !IsSameType(declaringType, _currentEnclosingType))
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0014",
                    $"'{memberName}' is inaccessible due to its protection level",
                    accessNode));
                return; // Don't emit additional diagnostics for the same access.
            }
        }

        // R9.4: Static members must be accessed via type name, not instance.
        if (modifiers.IsStatic && !targetIsTypeName)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0015",
                $"Static member '{memberName}' cannot be accessed via an instance",
                accessNode));
            return;
        }

        // R9.5: Instance members must be accessed via instance, not type name.
        if (!modifiers.IsStatic && targetIsTypeName)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0016",
                $"Instance member '{memberName}' cannot be accessed via a type name",
                accessNode));
        }
    }

    /// <summary>
    /// Finds a member symbol by name on the given type.
    /// </summary>
    private static ISymbol? FindMemberSymbol(IType targetType, string memberName, ISemanticModel model)
    {
        IScope? memberScope = targetType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return null;

        foreach (var member in memberScope.Members)
        {
            if (member.Name == memberName)
                return member;
        }

        return null;
    }

    /// <summary>
    /// Gets the type that declares a given member symbol by finding the type
    /// whose MemberScope matches the member's ContainingScope.
    /// </summary>
    private static IType? GetDeclaringTypeOfMember(ISymbol memberSymbol)
    {
        var typeScope = memberSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if (parentScope is null)
            return null;

        foreach (var member in parentScope.Members)
        {
            if (member.Kind is SymbolKind.Class or SymbolKind.Struct
                && member.DeclaredType is ClassType or StructType)
            {
                var scope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };
                if (ReferenceEquals(scope, typeScope))
                    return member.DeclaredType;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if two types refer to the same type (by reference or display name).
    /// </summary>
    private static bool IsSameType(IType? a, IType? b)
    {
        if (a is null || b is null)
            return false;
        if (ReferenceEquals(a, b))
            return true;
        // Fallback: compare by display name for types that may not be reference-equal.
        return a.DisplayName == b.DisplayName;
    }

    /// <summary>
    /// Modifier flags extracted from a member's AST declaring node.
    /// </summary>
    private readonly record struct MemberModifiers(bool IsPublic, bool IsPrivate, bool IsStatic)
    {
        /// <summary>
        /// True if the member has no explicit access modifier (defaults to private per R9.3).
        /// </summary>
        public bool HasNoAccessModifier => !IsPublic && !IsPrivate;
    }

    /// <summary>
    /// Extracts modifier information from a member's AST declaring node.
    /// Returns default (all false) for backend symbols or nodes without modifiers.
    /// </summary>
    private static MemberModifiers GetMemberModifiers(SyntaxNode? declaringNode)
    {
        IReadOnlyList<ModifierNode>? modifiers = declaringNode switch
        {
            FieldDeclarationNode field => field.Modifiers,
            MethodDeclarationNode method => method.Modifiers,
            ConstructorDeclarationNode ctor => ctor.Modifiers,
            _ => null,
        };

        if (modifiers is null || modifiers.Count == 0)
        {
            // No modifiers → defaults to private (R9.3), not static.
            return new MemberModifiers(IsPublic: false, IsPrivate: false, IsStatic: false);
        }

        var isPublic = false;
        var isPrivate = false;
        var isStatic = false;

        foreach (var mod in modifiers)
        {
            switch (mod)
            {
                case PublicModifierModifierNode:
                    isPublic = true;
                    break;
                case PrivateModifierModifierNode:
                    isPrivate = true;
                    break;
                case StaticModifierModifierNode:
                    isStatic = true;
                    break;
            }
        }

        return new MemberModifiers(isPublic, isPrivate, isStatic);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Assignability and type compatibility helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if <paramref name="source"/> is assignable to <paramref name="target"/>.
    /// Emits MIST_TC_0001 if not.
    /// </summary>
    private static void CheckAssignability(IType source, IType target, SyntaxNode node, DiagnosticBag diagnostics)
    {
        if (!IsAssignable(source, target))
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0001",
                $"Cannot assign '{source.DisplayName}' to '{target.DisplayName}'",
                node));
        }
    }

    /// <summary>
    /// Returns true if <paramref name="source"/> is assignable to <paramref name="target"/>.
    /// Rules:
    /// - Same type (identity)
    /// - Both numeric with implicit widening conversion
    /// - Either is UnknownType (error suppression)
    /// </summary>
    private static bool IsAssignable(IType source, IType target)
    {
        // UnknownType suppresses cascading errors.
        if (IsUnknown(source) || IsUnknown(target))
            return true;

        // Identity.
        if (ReferenceEquals(source, target))
            return true;

        // Same display name (for types that may not be reference-equal).
        if (source.DisplayName == target.DisplayName)
            return true;

        // Numeric widening.
        if (IsNumeric(source) && IsNumeric(target))
            return HasImplicitNumericConversion(source, target);

        return false;
    }

    /// <summary>
    /// Checks if two types are compatible for equality operators (==, !=).
    /// Compatible means: same type, both numeric, both bool, both string,
    /// or both reference types.
    /// </summary>
    private static bool AreEqualityCompatible(IType left, IType right)
    {
        if (IsUnknown(left) || IsUnknown(right))
            return true;

        if (ReferenceEquals(left, right))
            return true;

        if (left.DisplayName == right.DisplayName)
            return true;

        // Both numeric.
        if (IsNumeric(left) && IsNumeric(right))
            return true;

        // Both reference types (class types) can be compared.
        if (left is ClassType or StringType or ArrayType
            && right is ClassType or StringType or ArrayType)
            return true;

        return false;
    }

    private static bool IsNumeric(IType type)
    {
        return NumericTypes.Contains(type);
    }

    private static bool IsUnknown(IType type)
    {
        return type is UnknownType;
    }

    /// <summary>
    /// Checks for implicit numeric widening conversions following C# rules.
    /// </summary>
    private static bool HasImplicitNumericConversion(IType source, IType target)
    {
        // Simplified C# implicit numeric conversion table.
        // byte → short, ushort, int, uint, long, ulong, float, double, decimal
        // sbyte → short, int, long, float, double, decimal
        // short → int, long, float, double, decimal
        // ushort → int, uint, long, ulong, float, double, decimal
        // int → long, float, double, decimal
        // uint → long, ulong, float, double, decimal
        // long → float, double, decimal
        // ulong → float, double, decimal
        // float → double
        // char → ushort, int, uint, long, ulong, float, double, decimal

        if (source == PrimitiveType.Byte)
            return target == PrimitiveType.Short || target == PrimitiveType.Ushort
                || target == PrimitiveType.Int || target == PrimitiveType.Uint
                || target == PrimitiveType.Long || target == PrimitiveType.Ulong
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Sbyte)
            return target == PrimitiveType.Short
                || target == PrimitiveType.Int
                || target == PrimitiveType.Long
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Short)
            return target == PrimitiveType.Int
                || target == PrimitiveType.Long
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Ushort)
            return target == PrimitiveType.Int || target == PrimitiveType.Uint
                || target == PrimitiveType.Long || target == PrimitiveType.Ulong
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Int)
            return target == PrimitiveType.Long
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Uint)
            return target == PrimitiveType.Long || target == PrimitiveType.Ulong
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Long)
            return target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Ulong)
            return target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Float)
            return target == PrimitiveType.Double;

        // char → numeric widening
        if (source == PrimitiveType.Char)
            return target == PrimitiveType.Ushort || target == PrimitiveType.Int
                || target == PrimitiveType.Uint || target == PrimitiveType.Long
                || target == PrimitiveType.Ulong || target == PrimitiveType.Float
                || target == PrimitiveType.Double || target == PrimitiveType.Decimal;

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Utility helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static List<IType> CollectArgumentTypes(ArgumentListNode? argList, ISemanticModel model)
    {
        var types = new List<IType>();
        if (argList is null)
            return types;

        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr)
            {
                var type = model.GetTypeOf(expr);
                types.Add(type ?? UnknownType.Instance);
            }
        }

        return types;
    }

    /// <summary>
    /// Finds the enclosing type for a symbol by walking up the scope chain.
    /// </summary>
    private static IType? FindEnclosingType(ISymbol symbol)
    {
        var scope = symbol.ContainingScope;
        while (scope is not null)
        {
            foreach (var member in scope.Members)
            {
                if (member.Kind is SymbolKind.Class or SymbolKind.Struct)
                    return member.DeclaredType;
            }
            scope = scope.Parent;
        }
        return null;
    }

    /// <summary>
    /// Creates a diagnostic for the type checker.
    /// </summary>
    private static Diagnostic MakeDiagnostic(string id, string message, SyntaxNode node)
    {
        Common.Text.LinePositionSpan? location = null;

        // Try to extract location from the node's span.
        var span = node.Span;
        if (span.Length > 0)
        {
            location = new Common.Text.LinePositionSpan(
                new Common.Text.LinePosition(0, span.Start),
                new Common.Text.LinePosition(0, span.End));
        }

        return new Diagnostic(
            new DiagnosticId(id),
            DiagnosticSeverity.Error,
            PipelineStage.Analysis,
            message,
            Location: location,
            CompilationUnitPath: null,
            AnalyzerName: "TypeChecker");
    }
}
