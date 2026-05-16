namespace CastOperators;

public class Celsius
{
    private double degrees;

    public Celsius(double degrees)
    {
        this.degrees = degrees;
    }

    public double GetDegrees()
    {
        return degrees;
    }

    public static implicit operator Fahrenheit(Celsius c)
    {
        return new Fahrenheit(c.GetDegrees() * 1.8 + 32.0);
    }
}

public class Fahrenheit
{
    private double degrees;

    public Fahrenheit(double degrees)
    {
        this.degrees = degrees;
    }

    public string Describe()
    {
        return $"{degrees} F";
    }
}
