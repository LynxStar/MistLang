namespace Mist.ProgramModel.Model;

/// <summary>
/// The kind of operator declared on a type.
/// </summary>
public enum OperatorKind
{
    /// <summary>The + operator.</summary>
    Plus,

    /// <summary>The - operator.</summary>
    Minus,

    /// <summary>The * operator.</summary>
    Star,

    /// <summary>The / operator.</summary>
    Slash,

    /// <summary>The % operator.</summary>
    Modulo,

    /// <summary>The == operator.</summary>
    EqualEqual,

    /// <summary>The != operator.</summary>
    NotEqual,

    /// <summary>The &lt;= operator.</summary>
    LessThanOrEqual,

    /// <summary>The &gt;= operator.</summary>
    GreaterThanOrEqual,

    /// <summary>The &lt; operator.</summary>
    LessThan,

    /// <summary>The &gt; operator.</summary>
    GreaterThan,

    /// <summary>The &amp;&amp; operator.</summary>
    LogicalAnd,

    /// <summary>The || operator.</summary>
    LogicalOr,

    /// <summary>The ! operator.</summary>
    Exclamation,
}