using System;

namespace RefinementTypes;

public class Person
{
    public int Age;

    public string Name;

    public Person(string name)
    {
        this.Name = name;
        this.Age = 18;
    }
}
public class Program
{
    private static string DescribeAge(int age)
    {
        if (!(age >= 18))
            throw new ArgumentException($"Value {age} does not satisfy refinement '>= 18' on type 'AdultAge'", nameof(age));
        return $"Adult age is {age}";
    }

    private static int GetDefaultAge()
    {
        return 25;
    }

    public static void Main(string[] args)
    {
        int score = 85;
        var person = new Person("Alice");
        int defaultAge = Program.GetDefaultAge();
        var desc = Program.DescribeAge(defaultAge);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
