namespace Mist.Backends.CSharpTranspiler.Intrinsics;

/// <summary>
/// A simple wrapper around a format string used to emit intrinsic operations.
/// Template placeholders use <c>{0}</c>, <c>{1}</c>, etc. for positional
/// operand/argument substitution (e.g., <c>"{0} + {1}"</c> for binary addition).
/// </summary>
internal record IntrinsicEmissionTemplate(string Template);

/// <summary>
/// Registry that maps intrinsic stdlib members to their C# emission templates.
/// Consulted by the emitter when it encounters a call to an intrinsic operator
/// or method; the template is filled in with the emitted operand/argument
/// expressions to produce the final C# source fragment.
/// </summary>
internal interface IIntrinsicEmissionRegistry
{
    /// <summary>
    /// Looks up the emission template for a binary operator on a stdlib type.
    /// </summary>
    /// <param name="declaringType">Fully-qualified Mist type name (e.g., <c>"Standard.Int32"</c>).</param>
    /// <param name="op">Operator symbol (e.g., <c>"+"</c>, <c>"=="</c>).</param>
    /// <returns>The template, or <c>null</c> if no intrinsic is registered.</returns>
    IntrinsicEmissionTemplate? TryGetBinaryOperator(string declaringType, string op);

    /// <summary>
    /// Looks up the emission template for a unary operator on a stdlib type.
    /// </summary>
    /// <param name="declaringType">Fully-qualified Mist type name (e.g., <c>"Standard.Boolean"</c>).</param>
    /// <param name="op">Operator symbol (e.g., <c>"!"</c>, <c>"-"</c>).</param>
    /// <returns>The template, or <c>null</c> if no intrinsic is registered.</returns>
    IntrinsicEmissionTemplate? TryGetUnaryOperator(string declaringType, string op);

    /// <summary>
    /// Looks up the emission template for an intrinsic instance method on a stdlib type.
    /// </summary>
    /// <param name="declaringType">Fully-qualified Mist type name (e.g., <c>"Standard.String"</c>).</param>
    /// <param name="method">Method name (e.g., <c>"Length"</c>, <c>"Substring"</c>).</param>
    /// <param name="paramTypes">Parameter type names for overload disambiguation.</param>
    /// <returns>The template, or <c>null</c> if no intrinsic is registered.</returns>
    IntrinsicEmissionTemplate? TryGetInstanceMethod(string declaringType, string method, IReadOnlyList<string> paramTypes);

    /// <summary>
    /// Looks up the emission template for an intrinsic constructor on a stdlib type.
    /// Reserved for future use.
    /// </summary>
    /// <param name="declaringType">Fully-qualified Mist type name.</param>
    /// <param name="paramTypes">Parameter type names for overload disambiguation.</param>
    /// <returns>The template, or <c>null</c> if no intrinsic is registered.</returns>
    IntrinsicEmissionTemplate? TryGetConstructor(string declaringType, IReadOnlyList<string> paramTypes);

    /// <summary>
    /// Looks up the emission template for an intrinsic cast operator on a stdlib type.
    /// </summary>
    /// <param name="declaringType">Fully-qualified Mist type name (e.g., <c>"Standard.Int32"</c>).</param>
    /// <param name="targetType">The target type name of the cast (e.g., <c>"long"</c>).</param>
    /// <returns>The template, or <c>null</c> if no intrinsic cast is registered.</returns>
    IntrinsicEmissionTemplate? TryGetCastOperator(string declaringType, string targetType);
}
