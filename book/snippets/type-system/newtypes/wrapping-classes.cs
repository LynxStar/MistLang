using System;

namespace Snippet;

public class Entity
{
    private string name;

    private int id;

    public Entity(string name, int id)
    {
        this.name = name;
        this.id = id;
    }

    public string GetName()
    {
        return this.name;
    }

    public int GetId()
    {
        return this.id;
    }

    public string Describe()
    {
        return $"Entity({this.name}, {this.id})";
    }

    protected Entity()
    {
        this.name = "";
        this.id = 0;
    }

    public static Entity __CreateDefault()
    {
        var inst = new Entity();
        return inst;
    }
}
public class Actor
{
    private string name;

    private int id;

    public Actor(string name, int id)
    {
        this.name = name;
        this.id = id;
    }

    public string GetName()
    {
        return this.name;
    }

    public int GetId()
    {
        return this.id;
    }

    public string Describe()
    {
        return $"Entity({this.name}, {this.id})";
    }
}
