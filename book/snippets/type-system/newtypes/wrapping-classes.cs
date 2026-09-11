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
}

class Actor
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
