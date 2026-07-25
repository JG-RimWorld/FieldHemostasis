using System.Collections.Generic;
using Verse;

namespace FieldHemostasis
{
    internal static class HemostasisUtility
    {
        internal static bool HasUnstabilizedBleeding(Pawn patient)
        {
            return TryGetWorstUnstabilizedBleedingSource(patient, out _);
        }

        internal static bool TryGetWorstUnstabilizedBleedingSource(
            Pawn patient,
            out Hediff worstSource)
        {
            worstSource = null;
            if (patient?.health?.hediffSet?.hediffs == null)
            {
                return false;
            }

            HemostasisGameComponent component =
                HemostasisGameComponent.Instance;
            float highestBleedRate = 0f;
            List<Hediff> hediffs = patient.health.hediffSet.hediffs;

            for (int index = 0; index < hediffs.Count; index++)
            {
                Hediff candidate = hediffs[index];
                if (!(candidate is Hediff_Injury)
                    && !(candidate is Hediff_MissingPart))
                {
                    continue;
                }

                if (component != null && component.IsActive(candidate))
                {
                    continue;
                }

                float bleedRate = candidate.BleedRate;
                if (bleedRate > highestBleedRate)
                {
                    highestBleedRate = bleedRate;
                    worstSource = candidate;
                }
            }

            return worstSource != null;
        }
    }
}
