using HarmonyLib;
using Verse;

namespace FieldHemostasis
{
    internal static class BleedRatePatch
    {
        internal static void Apply(Hediff bleedingSource, ref float result)
        {
            HemostasisGameComponent component =
                HemostasisGameComponent.Instance;

            if (component == null || component.ActiveCount == 0)
            {
                return;
            }

            // A vanilla bleed rate of zero means the wound has been tended or
            // no longer bleeds for another reason. Its hemostasis is redundant.
            if (result <= 0f)
            {
                component.Remove(bleedingSource);
                return;
            }

            if (component.IsActive(bleedingSource))
            {
                result = 0f;
            }
        }
    }

    [HarmonyPatch(
        typeof(Hediff_Injury),
        nameof(Hediff_Injury.BleedRate),
        MethodType.Getter)]
    internal static class HediffInjuryBleedRatePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Hediff_Injury __instance,
            ref float __result)
        {
            BleedRatePatch.Apply(__instance, ref __result);
        }
    }

    [HarmonyPatch(
        typeof(Hediff_MissingPart),
        nameof(Hediff_MissingPart.BleedRate),
        MethodType.Getter)]
    internal static class HediffMissingPartBleedRatePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Hediff_MissingPart __instance,
            ref float __result)
        {
            BleedRatePatch.Apply(__instance, ref __result);
        }
    }

    [HarmonyPatch(
        typeof(Pawn_HealthTracker),
        nameof(Pawn_HealthTracker.RemoveHediff))]
    internal static class RemoveHediffPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Hediff hediff)
        {
            HemostasisGameComponent.Instance?.Remove(hediff);
        }
    }
}
