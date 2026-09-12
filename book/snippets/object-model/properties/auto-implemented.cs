using Standard;

namespace Snippet;

public class Person
{
    public Person(string name, string id)
    {
        this.Name = name;
        this.Id = id;
    }

    public string Name { get; set; }

    public string Id { get; }

    public int Age { get; set; } = 0;
}
