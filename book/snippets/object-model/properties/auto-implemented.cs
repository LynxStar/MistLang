using System;

namespace Demo;

public class Person
{
    public string Name { get; set; }

    public string Id { get; }

    public int Age { get; set; } = 0;

    public Person(string name, string id)
    {
        this.Name = name;
        this.Id = id;
    }

    protected Person()
    {
        this.Name = "";
        this.Id = "";
        this.Age = 0;
    }

    public static Person __CreateDefault()
    {
        var inst = new Person();
        return inst;
    }
}
