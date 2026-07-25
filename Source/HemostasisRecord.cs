using Verse;

namespace FieldHemostasis
{
    public sealed class HemostasisRecord : IExposable
    {
        internal Hediff bleedingSource;
        internal int expiresAtTick;

        public HemostasisRecord()
        {
        }

        internal HemostasisRecord(Hediff bleedingSource, int expiresAtTick)
        {
            this.bleedingSource = bleedingSource;
            this.expiresAtTick = expiresAtTick;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref bleedingSource, "bleedingSource");
            Scribe_Values.Look(ref expiresAtTick, "expiresAtTick");
        }
    }
}
