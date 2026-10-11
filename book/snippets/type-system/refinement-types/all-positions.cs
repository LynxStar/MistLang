using Standard;

namespace Snippet;

public class Person
{
    public int Age;
    public string Name;

    public Person(string name)
    {
        this.Name = "";
        this.Name = name;
        this.Age = 18;
    }
}

public class Program
{
    private static string DescribeAge(int age)
    {
        return System.FormattableString.Invariant($"Adult age is {age}");
    }

    private static int GetDefaultAge()
    {
        return 25;
    }

    public static void Main(string[] args)
    {
        int score = 85;
        Snippet.Person person = new Snippet.Person("Alice");
        int defaultAge = Snippet.Program.GetDefaultAge();
        string desc = Snippet.Program.DescribeAge(defaultAge);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
