namespace Snippet;

public class Animal
{
    private string name;

    private int age;

    public Animal(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string GetName()
    {
        return name;
    }

    public int GetAge()
    {
        return age;
    }

    public string Describe()
    {
        return $"{name} (age {age})";
    }

    protected Animal()
    {
        this.name = "";
        this.age = 0;
    }

    public static Animal __CreateDefault()
    {
        var inst = new Animal();
        return inst;
    }
}
public class Dog : Animal
{
    private string breed;

    public Dog(string name, int age, string breed) : base(name, age)
    {
        this.breed = breed;
    }

    public string GetBreed()
    {
        return breed;
    }

    protected Dog()
    {
        this.breed = "";
    }

    public static Dog __CreateDefault()
    {
        var inst = new Dog();
        return inst;
    }
}
