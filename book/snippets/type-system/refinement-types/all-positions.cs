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
    // Refinement as parameter type — runtime guard emitted
    private static string DescribeAge(int age)
    {
        if (!(age >= 18))
            throw new ArgumentException(
                $"Value {age} does not satisfy refinement '>= 18' on type 'AdultAge'",
                nameof(age));
        return $"Adult age is {age}";
    }

    // Refinement as return type
    private static int GetDefaultAge()
    {
        return 25;
    }

    public static void Main(string[] args)
    {
        // Refinement as variable type
        int score = 85;

        // Refinement as field type (via Person.Age)
        Person person = new Person("Alice");

        // Refinement as return type
        int defaultAge = Program.GetDefaultAge();

        // Refinement as parameter type
        string desc = Program.DescribeAge(defaultAge);
    }
}
