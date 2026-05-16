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
}
