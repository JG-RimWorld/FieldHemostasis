using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using Verse;

namespace JG.RotRWeddingFix
{
    [HarmonyPatch]
    internal static class WeddingParticipantTickV2Patch
    {
        private static MethodBase TargetMethod()
        {
            Type participantCount = AccessTools.TypeByName("RimWorld.RitualOutcomeComp_ParticipantCount");
            if (participantCount == null)
                throw new MissingMemberException("RimWorld.RitualOutcomeComp_ParticipantCount was not found.");

            foreach (MethodInfo method in participantCount.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name == "Tick" && method.GetParameters().Length == 3)
                    return method;
            }

            throw new MissingMethodException("RitualOutcomeComp_ParticipantCount.Tick was not found.");
        }

        private static void Prefix(object[] __args)
        {
            if (__args == null || __args.Length < 3 || !RotRCompat.IsWedding(__args[0]))
                return;

            // RotR's custom wedding stages report ProgressPerTick == 0, so the vanilla
            // attendance component otherwise records every physically present pawn as
            // present for zero ticks. Use one real game tick for attendance accounting only.
            __args[2] = 1f;
        }
    }

    [HarmonyPatch]
    internal static class WeddingParticipantCountV2Patch
    {
        private static MethodBase TargetMethod()
        {
            Type participantCount = AccessTools.TypeByName("RimWorld.RitualOutcomeComp_ParticipantCount");
            if (participantCount == null)
                throw new MissingMemberException("RimWorld.RitualOutcomeComp_ParticipantCount was not found.");

            MethodInfo method = AccessTools.Method(participantCount, "Count");
            if (method == null)
                throw new MissingMethodException("RitualOutcomeComp_ParticipantCount.Count was not found.");

            return method;
        }

        private static void Postfix(object __instance, object[] __args, ref float __result)
        {
            if (__args == null || __args.Length < 2 || !RotRCompat.IsWedding(__args[0]))
                return;

            try
            {
                object wedding = __args[0];
                object data = __args[1];
                FieldInfo presentField = RotRCompat.FindField(data.GetType(), "presentForTicks");
                FieldInfo assignmentsField = RotRCompat.FindField(wedding.GetType(), "assignments");
                if (presentField == null || assignmentsField == null)
                    return;

                IEnumerable entries = presentField.GetValue(data) as IEnumerable;
                object assignments = assignmentsField.GetValue(wedding);
                if (entries == null || assignments == null)
                    return;

                MethodInfo countsMethod = AccessTools.Method(__instance.GetType(), "Counts");
                if (countsMethod == null)
                    return;

                float maxTracked = 0f;
                foreach (object entry in entries)
                {
                    if (entry == null) continue;
                    PropertyInfo valueProperty = entry.GetType().GetProperty("Value");
                    if (valueProperty == null) continue;
                    float value = Convert.ToSingle(valueProperty.GetValue(entry, null));
                    if (value > maxTracked) maxTracked = value;
                }

                if (maxTracked <= 0f)
                {
                    __result = 0f;
                    Log.Message("[RotR Wedding Fix] Wedding attendance v1.2: no real attendance ticks were recorded.");
                    return;
                }

                float requiredPresence = maxTracked / 2f;
                int count = 0;
                int tracked = 0;

                foreach (object entry in entries)
                {
                    if (entry == null) continue;
                    Type entryType = entry.GetType();
                    PropertyInfo keyProperty = entryType.GetProperty("Key");
                    PropertyInfo valueProperty = entryType.GetProperty("Value");
                    if (keyProperty == null || valueProperty == null) continue;

                    object pawn = keyProperty.GetValue(entry, null);
                    float presentTicks = Convert.ToSingle(valueProperty.GetValue(entry, null));
                    tracked++;
                    if (pawn == null || presentTicks < requiredPresence) continue;

                    if (Convert.ToBoolean(countsMethod.Invoke(__instance, new[] { assignments, pawn })))
                        count++;
                }

                if (count > 30) count = 30;
                __result = count;

                Log.Message("[RotR Wedding Fix] Wedding attendance v1.2: " + count +
                            " participant(s) counted from " + tracked +
                            " tracked pawn(s); longest attendance " + maxTracked +
                            " ticks, required " + requiredPresence + ".");
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Attendance v1.2 postfix failed: " + ex);
            }
        }
    }
}
