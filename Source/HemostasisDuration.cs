using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace FieldHemostasis
{
    internal static class HemostasisDuration
    {
        private const float DefaultMeanHours = 12f;
        private const float MinimumDurationHours = 0.25f;
        private const int MaximumSamplingAttempts = 16;

        internal static int RollTicks()
        {
            FieldHemostasisSettings settings = FieldHemostasisMod.Settings;
            float meanHours = settings?.meanDurationHours ?? DefaultMeanHours;
            float sampledHours = meanHours;

            if (settings != null && settings.useNormalDistribution)
            {
                float standardDeviation =
                    meanHours * settings.standardDeviationFraction;
                float upperBound = meanHours + 4f * standardDeviation;

                for (int attempt = 0; attempt < MaximumSamplingAttempts; attempt++)
                {
                    float candidate =
                        meanHours + standardDeviation * SampleStandardNormal();

                    if (candidate >= MinimumDurationHours && candidate <= upperBound)
                    {
                        sampledHours = candidate;
                        break;
                    }
                }
            }

            return Math.Max(
                1,
                Mathf.RoundToInt(sampledHours * GenDate.TicksPerHour));
        }

        private static float SampleStandardNormal()
        {
            // Box-Muller transform using Verse.Rand keeps the result tied to
            // RimWorld's deterministic random-number state.
            double firstUniform = Math.Max(1e-12, 1.0 - Rand.Value);
            double secondUniform = 1.0 - Rand.Value;
            double magnitude = Math.Sqrt(-2.0 * Math.Log(firstUniform));
            double angle = 2.0 * Math.PI * secondUniform;
            return (float)(magnitude * Math.Cos(angle));
        }
    }
}
