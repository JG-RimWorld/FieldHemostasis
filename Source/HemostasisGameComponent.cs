using System.Collections.Generic;
using Verse;

namespace FieldHemostasis
{
    public sealed class HemostasisGameComponent : GameComponent
    {
        private List<HemostasisRecord> records = new List<HemostasisRecord>();

        private Dictionary<Hediff, HemostasisRecord> recordsBySource =
            new Dictionary<Hediff, HemostasisRecord>();

        internal static HemostasisGameComponent Instance { get; private set; }

        internal int ActiveCount => recordsBySource.Count;

        public HemostasisGameComponent(Game game)
        {
            Instance = this;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Cleanup(Find.TickManager.TicksGame);
            }

            Scribe_Collections.Look(ref records, "hemostasisRecords", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null)
                {
                    records = new List<HemostasisRecord>();
                }

                RebuildIndex();
                Cleanup(Find.TickManager.TicksGame);
            }
        }

        internal bool AddOrRefresh(Hediff bleedingSource, int durationTicks)
        {
            if (!IsStillAttached(bleedingSource))
            {
                return false;
            }

            int expiration = Find.TickManager.TicksGame + durationTicks;

            if (recordsBySource.TryGetValue(
                    bleedingSource,
                    out HemostasisRecord existingRecord))
            {
                if (expiration > existingRecord.expiresAtTick)
                {
                    existingRecord.expiresAtTick = expiration;
                }

                return true;
            }

            HemostasisRecord record =
                new HemostasisRecord(bleedingSource, expiration);
            records.Add(record);
            recordsBySource.Add(bleedingSource, record);
            return true;
        }

        internal bool IsActive(Hediff bleedingSource)
        {
            if (!recordsBySource.TryGetValue(
                    bleedingSource,
                    out HemostasisRecord record))
            {
                return false;
            }

            if (record.expiresAtTick <= Find.TickManager.TicksGame
                || !IsStillAttached(bleedingSource))
            {
                Remove(bleedingSource);
                return false;
            }

            return true;
        }

        internal void Remove(Hediff bleedingSource)
        {
            if (bleedingSource == null
                || !recordsBySource.TryGetValue(
                    bleedingSource,
                    out HemostasisRecord record))
            {
                return;
            }

            recordsBySource.Remove(bleedingSource);
            records.Remove(record);
        }

        private void RebuildIndex()
        {
            recordsBySource =
                new Dictionary<Hediff, HemostasisRecord>();

            for (int index = records.Count - 1; index >= 0; index--)
            {
                HemostasisRecord record = records[index];
                if (record?.bleedingSource == null)
                {
                    records.RemoveAt(index);
                    continue;
                }

                if (recordsBySource.TryGetValue(
                        record.bleedingSource,
                        out HemostasisRecord existingRecord))
                {
                    if (record.expiresAtTick > existingRecord.expiresAtTick)
                    {
                        existingRecord.expiresAtTick = record.expiresAtTick;
                    }

                    records.RemoveAt(index);
                    continue;
                }

                recordsBySource.Add(record.bleedingSource, record);
            }
        }

        private void Cleanup(int currentTick)
        {
            for (int index = records.Count - 1; index >= 0; index--)
            {
                HemostasisRecord record = records[index];
                if (record?.bleedingSource == null
                    || record.expiresAtTick <= currentTick
                    || !IsStillAttached(record.bleedingSource))
                {
                    if (record?.bleedingSource != null)
                    {
                        recordsBySource.Remove(record.bleedingSource);
                    }

                    records.RemoveAt(index);
                }
            }
        }

        private static bool IsStillAttached(Hediff bleedingSource)
        {
            Pawn pawn = bleedingSource?.pawn;
            return pawn?.health?.hediffSet?.hediffs != null
                && pawn.health.hediffSet.hediffs.Contains(bleedingSource);
        }
    }
}
