using System;

namespace DependentTypes;

[DependentType("V", typeof(int))]
public class IntParam<TV>
    where TV : IConstInt
{
    public static int V => TV.Value;

    public int GetValue() { return V; }
}

[DependentType("V", typeof(bool))]
public class BoolParam<TV>
    where TV : IConstBool
{
    public static bool V => TV.Value;

    public string Describe() { return $"BoolParam<{V}>"; }
}

[DependentType("V", typeof(char))]
public class CharParam<TV>
    where TV : IConstChar
{
    public static char V => TV.Value;

    public string Describe() { return $"CharParam<{V}>"; }
}
