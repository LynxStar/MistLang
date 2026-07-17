using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Vehicle
{
    protected int speed;

    public Vehicle(int speed)
    {
        this.speed = speed;
    }
}
public class Car : Vehicle
{
    private string model;

    public Car(string model, int speed) : base(speed)
    {
        this.model = model;
    }

    public string Describe()
    {
        return $"{model} going {speed} mph";
    }
}
