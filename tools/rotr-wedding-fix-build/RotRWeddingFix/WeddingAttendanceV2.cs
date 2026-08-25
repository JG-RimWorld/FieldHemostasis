using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace JG.RotRWeddingFix
{
    // RotR's four custom StageEndTrigger classes do not opt into vanilla ritual
    // progress. The ceremony itself advances through those custom triggers, but
    // LordJob_Ritual.ticksPassedWithProgress therefore remains zero forever.
    // Keep the vanilla progress clock in sync with the real elapsed wedding time.
    // This fixes the permanently frozen "4 hours remaining" report and also
    // repairs old in-progress saves on their first tick after loading.
    [HarmonyPatch]
    internal static class WeddingProgressV3Patch
    {
        private static MethodBase TargetMethod()
        {
            Type ritualType = AccessTools.TypeByName("RimWorld.LordJob_Ritual");
            if (ritualType == null)
                throw new MissingMemberException("RimWorld.LordJob_Ritual was not found.");

            MethodInfo method = AccessTools.Method(ritualType, "LordJobTick");
            if (method == null)
                throw new MissingMethodException("LordJob_Ritual.LordJobTick was not found.");

            return method;
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance)
        {
            if (!RotRCompat.IsWedding(__instance))
                return;

            try
            {
                FieldInfo elapsedField = RotRCompat.FindField(__instance.GetType(), "ticksPassed");
                FieldInfo progressField = RotRCompat.FindField(__instance.GetType(), "ticksPassedWithProgress");
                FieldInfo durationField = RotRCompat.FindField(__instance.GetType(), "durationTicks");
                if (elapsedField == null || progressField == null || durationField == null)
                    return;

                int elapsed = Convert.ToInt32(elapsedField.GetValue(__instance));
                int duration = Convert.ToInt32(durationField.GetValue(__instance));
                float current = Convert.ToSingle(progressField.GetValue(__instance));
                float corrected = duration > 0 ? Math.Min(elapsed, duration) : elapsed;

                if (current < corrected)
                    progressField.SetValue(__instance, corrected);
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Could not synchronize wedding progress: " + ex);
            }
        }
    }

    // RotR uses the vanilla RitualOutcomeComp_ParticipantCount in XML, but its
    // wedding outcome worker leaves that comp's own presentForTicks data empty.
    // LordToil_Ritual, however, independently tracks real physical attendance in
    // LordToilData_Gathering.presentForTicks every tick and serializes it correctly.
    // Use that canonical ritual presence data for the final wedding quality.
    [HarmonyPatch]
    internal static class WeddingParticipantCountV3Patch
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

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(object __instance, object[] __args, ref float __result)
        {
            if (__args == null || __args.Length < 1 || !RotRCompat.IsWedding(__args[0]))
                return;

            try
            {
                object wedding = __args[0];
                Type weddingType = wedding.GetType();

                FieldInfo toilsField = RotRCompat.FindField(weddingType, "toils");
                FieldInfo assignmentsField = RotRCompat.FindField(weddingType, "assignments");
                FieldInfo elapsedField = RotRCompat.FindField(weddingType, "ticksPassed");
                if (toilsField == null || assignmentsField == null || elapsedField == null)
                    throw new MissingMemberException("Could not find ritual toil/assignment state.");

                IList toils = toilsField.GetValue(wedding) as IList;
                object assignments = assignmentsField.GetValue(wedding);
                int elapsed = Convert.ToInt32(elapsedField.GetValue(wedding));
                if (toils == null || assignments == null)
                {
                    __result = 0f;
                    return;
                }

                Dictionary<object, float> totalPresence = new Dictionary<object, float>();

                foreach (object toil in toils)
                {
                    if (toil == null)
                        continue;

                    PropertyInfo dataProperty = RotRCompat.FindProperty(toil.GetType(), "Data");
                    object data = dataProperty != null ? dataProperty.GetValue(toil, null) : null;
                    if (data == null)
                        continue;

                    FieldInfo presentField = RotRCompat.FindField(data.GetType(), "presentForTicks");
                    IEnumerable entries = presentField != null ? presentField.GetValue(data) as IEnumerable : null;
                    if (entries == null)
                        continue;

                    foreach (object entry in entries)
                    {
                        if (entry == null)
                            continue;

                        PropertyInfo keyProperty = entry.GetType().GetProperty("Key");
                        PropertyInfo valueProperty = entry.GetType().GetProperty("Value");
                        if (keyProperty == null || valueProperty == null)
                            continue;

                        object pawn = keyProperty.GetValue(entry, null);
                        if (pawn == null)
                            continue;

                        float ticks = Convert.ToSingle(valueProperty.GetValue(entry, null));
                        float old;
                        totalPresence.TryGetValue(pawn, out old);
                        totalPresence[pawn] = old + ticks;
                    }
                }

                // Match vanilla semantics: a participant counts if physically present for
                // at least half of the ritual. Use actual elapsed wedding time rather than
                // RotR's nominal 10,000-tick duration, since its custom stages can finish
                // independently of that nominal clock.
                float requiredPresence = Math.Max(1f, elapsed / 2f);
                MethodInfo countsMethod = AccessTools.Method(__instance.GetType(), "Counts");
                if (countsMethod == null)
                    throw new MissingMethodException("RitualOutcomeComp_ParticipantCount.Counts was not found.");

                int count = 0;
                int tracked = 0;
                float longest = 0f;

                foreach (KeyValuePair<object, float> pair in totalPresence)
                {
                    tracked++;
                    if (pair.Value > longest)
                        longest = pair.Value;

                    if (pair.Value < requiredPresence)
                        continue;

                    if (Convert.ToBoolean(countsMethod.Invoke(__instance, new[] { assignments, pair.Key })))
                        count++;
                }

                if (count > 30)
                    count = 30;

                __result = count;
                Log.Message("[RotR Wedding Fix] Wedding attendance v1.3: " + count +
                            " participant(s) counted from " + tracked +
                            " physically tracked pawn(s); elapsed " + elapsed +
                            " ticks, longest attendance " + longest +
                            ", required " + requiredPresence + ".");
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Attendance v1.3 calculation failed: " + ex);
            }
        }
    }
}
