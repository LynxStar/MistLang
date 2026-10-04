using Standard;

namespace Snippet;

public class IntParam<TV>
    where TV : IConstIntegral
{
    public static int V => ((int)TV.Value);

    public int GetValue()
    {
        return ((int)TV.Value);
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
    public static bool V => TV.Value;

    public string Describe()
    {
        return $"BoolParam<{TV.Value}>";
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
    public static char V => TV.Value;

    public string Describe()
    {
        return $"CharParam<{TV.Value}>";
    }

    public CharParam()
    {
    }

    public static CharParam<TV> __CreateDefault()
    {
        return new CharParam<TV>();
    }
}
