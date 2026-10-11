using Standard;

namespace Snippet;

public class Vehicle
{
    protected int speed;

    public Vehicle(int speed)
    {
        this.speed = default(int);
        this.speed = speed;
    }
}

public class Car : Snippet.Vehicle
{
    private string model;

    public Car(string model, int speed) : base(speed)
    {
        this.model = "";
        this.model = model;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"{model} going {speed} mph");
    }
}
