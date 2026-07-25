using RimWorld;
using Verse;

namespace FieldHemostasis
{
    [DefOf]
    public static class FieldHemostasisDefOf
    {
        public static JobDef FH_ApplyHemostasis;

        static FieldHemostasisDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(
                typeof(FieldHemostasisDefOf));
        }
    }
}
