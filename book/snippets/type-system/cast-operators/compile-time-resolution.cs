namespace CastOperators;

public class Measurement
{
    private string unit;

    public Measurement(string unit)
    {
        this.unit = unit;
    }

    protected Measurement()
    {
        this.unit = "";
    }

    public static Measurement __CreateDefault()
    {
        var inst = new Measurement();
        return inst;
    }
}
public class Weight : Measurement
{
    private double value;

    public Weight(double value, string unit) : base(unit)
    {
        this.value = value;
    }

    public double GetValue()
    {
        return value;
    }

    public static explicit operator Kelvin(Weight w)
    {
        return new Kelvin(w.GetValue());
    }

    protected Weight()
    {
        this.value = 0;
    }

    public static Weight __CreateDefault()
    {
        var inst = new Weight();
        return inst;
    }
}
