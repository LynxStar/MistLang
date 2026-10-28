using Standard;

namespace Updates;

public class Program
{
    public static void Main()
    {
        Union_Int32_String[] values = new Union_Int32_String[] { 42, "hello" };
        Union_Int32_String[]? maybeValues = values;
        System.Console.WriteLine(values.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
