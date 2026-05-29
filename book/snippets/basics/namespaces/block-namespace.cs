using System;

namespace Namespaces.Helpers
{
    public class MathHelper
    {
        public static int Add(int a, int b)
        {
            return a + b;
        }

        public static MathHelper __CreateDefault()
        {
            var inst = new MathHelper();
            return inst;
        }
    }
}
