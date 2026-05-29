using System;

namespace Namespaces.MixedA
{
    public class AlphaType
    {
        public string Name()
        {
            return "AlphaType";
        }

        public static AlphaType __CreateDefault()
        {
            var inst = new AlphaType();
            return inst;
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

        public static BetaType __CreateDefault()
        {
            var inst = new BetaType();
            return inst;
        }
    }
}
