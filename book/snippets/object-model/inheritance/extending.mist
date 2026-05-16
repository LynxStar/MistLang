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
}
