namespace Traits;

interface Printable
{
    string Name { get; }
    int Id { get; }
}

public class User : Printable
{
    public string Name { get; set; }

    public int Id { get; set; }

    public User(string name, int id)
    {
        this.Name = name;
        this.Id = id;
    }
}
