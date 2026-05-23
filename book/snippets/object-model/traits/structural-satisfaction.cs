using System;
using System.Text.RegularExpressions;

namespace Traits;

interface Printable
{
    string Name { get; }
    int Id { get; }
}
public class User
{
    public string Name { get; set; }

    public int Id { get; set; }

    public User(string name, int id)
    {
        this.Name = name;
        this.Id = id;
    }

    protected User()
    {
        this.Name = "";
        this.Id = 0;
    }

    public static User __CreateDefault()
    {
        var inst = new User();
        return inst;
    }
}
