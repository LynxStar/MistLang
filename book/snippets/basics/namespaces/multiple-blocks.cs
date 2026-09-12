using Standard;

namespace Namespaces.MixedA
{
    public class AlphaType
    {
        public string Name()
        {
            return "AlphaType";
        }

        public AlphaType()
        {
        }

        public static AlphaType __CreateDefault()
        {
            return new AlphaType();
        }
    }
}

namespace Namespaces.MixedB
{
    public class BetaType
    {
        public string Name()
        {
            return "BetaType";
        }

        public BetaType()
        {
        }

        public static BetaType __CreateDefault()
        {
            return new BetaType();
        }
    }
}
