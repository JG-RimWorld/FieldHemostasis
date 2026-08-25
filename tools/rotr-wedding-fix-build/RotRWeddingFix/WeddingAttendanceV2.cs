using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace JG.RotRWeddingFix
{
    // RotR advances its wedding through custom stage triggers, but those triggers
    // do not count toward vanilla ritual progress. Patching LordJobTick did not
    // affect RotR because its custom LordJob overrides that method. Patch the
    // property used by the UI instead, without changing ritual completion logic.
    [HarmonyPatch]
    internal static class WeddingTicksLeftV4Patch
    {
        private static MethodBase TargetMethod()
        {
            Type ritualType = AccessTools.TypeByName("RimWorld.LordJob_Ritual");
            if (ritualType == null)
                throw new MissingMemberException("RimWorld.LordJob_Ritual was not found.");

            MethodInfo getter = AccessTools.PropertyGetter(ritualType, "TicksLeft");
            if (getter == null)
                throw new MissingMethodException("LordJob_Ritual.TicksLeft getter was not found.");

            return getter;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(object __instance, ref int __result)
        {
            if (!RotRCompat.IsWedding(__instance))
                return;

            try
            {
                FieldInfo elapsedField = RotRCompat.FindField(__instance.GetType(), "ticksPassed");
                FieldInfo durationField = RotRCompat.FindField(__instance.GetType(), "durationTicks");
                if (elapsedField == null || durationField == null)
                    return;

                int elapsed = Convert.ToInt32(elapsedField.GetValue(__instance));
                int duration = Convert.ToInt32(durationField.GetValue(__instance));
                __result = Math.Max(0, duration - elapsed);
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Could not calculate wedding time remaining: " + ex);
            }
        }
    }

    // RotR uses the vanilla RitualOutcomeComp_ParticipantCount in XML, but its
    // wedding outcome worker leaves that comp's own presentForTicks data empty.
    // LordToil_Ritual independently tracks real physical attendance in
    // LordToilData_Gathering.presentForTicks and serializes it correctly.
    [HarmonyPatch]
    internal static class WeddingParticipantCountV4Patch
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
                Log.Message("[RotR Wedding Fix] Wedding attendance v1.4: " + count +
                            " participant(s) counted from " + tracked +
                            " physically tracked pawn(s); elapsed " + elapsed +
                            " ticks, longest attendance " + longest +
                            ", required " + requiredPresence + ".");
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Attendance v1.4 calculation failed: " + ex);
            }
        }
    }
}
