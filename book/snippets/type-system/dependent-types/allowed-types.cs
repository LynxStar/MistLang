using Standard;

namespace Snippet;

public class IntParam<TV>
    where TV : IConstInt
{
    public int GetValue()
    {
        return TV.Value;
    }

    public IntParam()
    {
    }

    public static IntParam<TV> __CreateDefault()
    {
        return new IntParam<TV>();
    }
}

public class BoolParam<TV>
    where TV : IConstBool
{
    public string Describe()
    {
        return $"BoolParam<{MistInvariant.Text(TV.Value)}>";
    }

    public BoolParam()
    {
    }

    public static BoolParam<TV> __CreateDefault()
    {
        return new BoolParam<TV>();
    }
}

public class CharParam<TV>
    where TV : IConstChar
{
    public string Describe()
    {
        return $"CharParam<{MistInvariant.Text(TV.Value)}>";
    }

    public CharParam()
    {
    }

    public static CharParam<TV> __CreateDefault()
    {
        return new CharParam<TV>();
    }
}
