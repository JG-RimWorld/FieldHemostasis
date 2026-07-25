using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace FieldHemostasis
{
    public sealed class FieldHemostasisMod : Mod
    {
        internal static FieldHemostasisSettings Settings { get; private set; }

        public FieldHemostasisMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<FieldHemostasisSettings>();
            new Harmony("jg.fieldhemostasis").PatchAll();
        }

        public override string SettingsCategory()
        {
            return "Stop the Bleeding! – Field Hemostasis";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label($"Mean hemostasis duration: {Settings.meanDurationHours:0} hour(s)");
            Settings.meanDurationHours = (float)Math.Round(
                listing.Slider(Settings.meanDurationHours, 1f, 72f));

            listing.Gap(8f);
            listing.CheckboxLabeled(
                "Use normally distributed duration",
                ref Settings.useNormalDistribution,
                "If enabled, each bleeding wound receives an independently sampled duration centered on the mean above.");

            if (Settings.useNormalDistribution)
            {
                listing.Gap(4f);
                listing.Label(
                    $"Standard deviation: {Settings.standardDeviationFraction * 100f:0}% of the mean");
                Settings.standardDeviationFraction = listing.Slider(
                    Settings.standardDeviationFraction,
                    0.05f,
                    0.75f);
                listing.Label(
                    "Samples are truncated to positive values and four standard deviations above the mean.");
            }

            listing.GapLine();
            listing.Label(
                "Duration is sampled when a wound is stabilized. Changing these settings does not alter existing hemostasis.");

            listing.End();
        }
    }

    public sealed class FieldHemostasisSettings : ModSettings
    {
        public float meanDurationHours = 12f;
        public bool useNormalDistribution;
        public float standardDeviationFraction = 0.25f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref meanDurationHours, "meanDurationHours", 12f);
            Scribe_Values.Look(ref useNormalDistribution, "useNormalDistribution", false);
            Scribe_Values.Look(
                ref standardDeviationFraction,
                "standardDeviationFraction",
                0.25f);
            base.ExposeData();
        }
    }
}
