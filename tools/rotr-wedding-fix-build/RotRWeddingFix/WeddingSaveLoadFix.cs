using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Verse;

[assembly: AssemblyVersion("1.1.0.0")]

namespace JG.RotRWeddingFix
{
    internal static class RotRCompat
    {
        internal const string WeddingTypeName = "RomanceOnTheRim.LordJob_WeddingCeremony";

        internal static FieldInfo FindField(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags);
                if (field != null)
                    return field;
            }
            return null;
        }

        internal static PropertyInfo FindProperty(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, flags);
                if (property != null)
                    return property;
            }
            return null;
        }

        internal static bool IsWedding(object obj)
        {
            return obj != null && obj.GetType().FullName == WeddingTypeName;
        }

        internal static object FindWeddingArg(object[] args)
        {
            if (args == null)
                return null;

            for (int i = 0; i < args.Length; i++)
            {
                if (IsWedding(args[i]))
                    return args[i];
            }
            return null;
        }

        internal static int RepairMissingProcessionPairs(object wedding)
        {
            if (!IsWedding(wedding))
                return 0;

            Type type = wedding.GetType();
            FieldInfo pairsField = FindField(type, "ProcessionPairs");
            FieldInfo pawnsField = FindField(type, "ProcessionPawns");
            FieldInfo escortsField = FindField(type, "escortPawns");

            if (pairsField == null || pawnsField == null || escortsField == null)
                return 0;

            IList pairs = pairsField.GetValue(wedding) as IList;
            if (pairs == null)
            {
                pairs = Activator.CreateInstance(pairsField.FieldType) as IList;
                if (pairs == null)
                    return 0;
                pairsField.SetValue(wedding, pairs);
            }

            if (pairs.Count != 0)
                return 0;

            IList processionPawns = pawnsField.GetValue(wedding) as IList;
            IList escorts = escortsField.GetValue(wedding) as IList;
            if (processionPawns == null || processionPawns.Count < 2 ||
                escorts == null || escorts.Count == 0)
                return 0;

            Type[] listArgs = pairsField.FieldType.GetGenericArguments();
            if (listArgs.Length != 1)
                return 0;

            Type pairType = listArgs[0];
            List<object> usedEscorts = new List<object>();
            int repaired = 0;

            // RotR saves ProcessionPawns and escortPawns correctly, but saves
            // List<Pair<Pawn,Pawn>> with LookMode.Deep. Pair<> is a value type and
            // is not IExposable, so Scribe drops the pairs. Rebuild them from the
            // two lists that do survive the save.
            for (int i = 0; i < processionPawns.Count; i++)
            {
                object principal = processionPawns[i];
                if (principal == null || escorts.Contains(principal))
                    continue;

                object escort = null;

                if (i + 1 < processionPawns.Count)
                {
                    object next = processionPawns[i + 1];
                    if (next != null && escorts.Contains(next) && !usedEscorts.Contains(next))
                        escort = next;
                }

                if (escort == null)
                {
                    for (int j = 0; j < escorts.Count; j++)
                    {
                        object candidate = escorts[j];
                        if (candidate != null && !usedEscorts.Contains(candidate))
                        {
                            escort = candidate;
                            break;
                        }
                    }
                }

                if (escort == null)
                    continue;

                object pair = Activator.CreateInstance(pairType, new[] { principal, escort });
                pairs.Add(pair);
                usedEscorts.Add(escort);
                repaired++;
            }

            if (repaired > 0)
                Log.Message("[RotR Wedding Fix] Repaired " + repaired +
                            " missing procession pairing(s) after save load.");

            return repaired;
        }

        internal static float CountWeddingAttendance(object participantComp, object wedding, object data)
        {
            FieldInfo presentField = FindField(data.GetType(), "presentForTicks");
            FieldInfo assignmentsField = FindField(wedding.GetType(), "assignments");
            PropertyInfo ticksProperty = FindProperty(wedding.GetType(), "TicksPassedWithProgress");

            if (presentField == null || assignmentsField == null || ticksProperty == null)
                throw new MissingMemberException("Could not find vanilla ritual attendance state.");

            object presentObject = presentField.GetValue(data);
            IEnumerable presentEntries = presentObject as IEnumerable;
            object assignments = assignmentsField.GetValue(wedding);

            if (presentEntries == null || assignments == null)
                return 0f;

            MethodInfo countsMethod = AccessTools.Method(participantComp.GetType(), "Counts");
            if (countsMethod == null)
                throw new MissingMethodException("RitualOutcomeComp_ParticipantCount.Counts was not found.");

            float actualProgress = Convert.ToSingle(ticksProperty.GetValue(wedding, null));
            float requiredPresence = actualProgress / 2f;
            int count = 0;

            foreach (object entry in presentEntries)
            {
                if (entry == null)
                    continue;

                Type entryType = entry.GetType();
                PropertyInfo keyProperty = entryType.GetProperty("Key");
                PropertyInfo valueProperty = entryType.GetProperty("Value");
                if (keyProperty == null || valueProperty == null)
                    continue;

                object pawn = keyProperty.GetValue(entry, null);
                float presentTicks = Convert.ToSingle(valueProperty.GetValue(entry, null));
                if (pawn == null || presentTicks < requiredPresence)
                    continue;

                bool counts = Convert.ToBoolean(countsMethod.Invoke(participantComp, new[] { assignments, pawn }));
                if (counts)
                    count++;
            }

            // RotR's guest quality curve tops out at 30, matching vanilla's
            // normal cap to the last x value in the curve.
            if (count > 30)
                count = 30;

            Log.Message("[RotR Wedding Fix] Wedding attendance: " + count +
                        " participant(s) counted after " + actualProgress +
                        " actual progress ticks (required presence: " + requiredPresence + ").");

            return count;
        }
    }

    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            try
            {
                new Harmony("jg.rotr.weddingfix").PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[RotR Wedding Fix] Save/load and attendance compatibility patch active.");
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Failed to apply patches: " + ex);
            }
        }
    }

    [HarmonyPatch]
    internal static class ScribeExtractorCreateInstancePatch
    {
        private static readonly string[] DerivedInitializers =
        {
            "ProcessionPawns",
            "ProcessionPairs",
            "escortPawns",
            "reservedLocs",
            "chairCells",
            "toLeaveLordPawns",
            "rand"
        };

        private static MethodBase TargetMethod()
        {
            Type scribeExtractor = AccessTools.TypeByName("Verse.ScribeExtractor");
            if (scribeExtractor == null)
                throw new MissingMemberException("Verse.ScribeExtractor was not found.");

            MethodInfo method = AccessTools.Method(
                scribeExtractor,
                "CreateInstance",
                new[] { typeof(Type), typeof(object[]) });

            if (method == null)
                throw new MissingMethodException("Verse.ScribeExtractor.CreateInstance(Type, object[]) was not found.");

            return method;
        }

        private static bool Prefix(Type type, object[] ctorArgs, ref object __result)
        {
            if (type == null || type.FullName != RotRCompat.WeddingTypeName)
                return true;

            if (ctorArgs != null && ctorArgs.Length != 0)
                return true;

            try
            {
                __result = CreateWeddingForDeserialization(type);
                Log.Message("[RotR Wedding Fix] Reconstructed LordJob_WeddingCeremony for save loading.");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Could not reconstruct LordJob_WeddingCeremony: " + ex);
                return true;
            }
        }

        private static object CreateWeddingForDeserialization(Type weddingType)
        {
            Type baseType = weddingType.BaseType;
            if (baseType == null)
                throw new InvalidOperationException("Wedding LordJob has no base type.");

            object wedding = FormatterServices.GetUninitializedObject(weddingType);
            object initializedBase = Activator.CreateInstance(
                baseType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[0],
                culture: null);

            if (initializedBase == null)
                throw new InvalidOperationException("Could not invoke the base LordJob_Ritual constructor.");

            CopyBaseState(initializedBase, wedding, baseType);
            InitializeDerivedFields(wedding, weddingType);
            return wedding;
        }

        private static void CopyBaseState(object source, object destination, Type baseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type current = baseType; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(flags))
                {
                    if (!field.IsStatic)
                        field.SetValue(destination, field.GetValue(source));
                }
            }
        }

        private static void InitializeDerivedFields(object wedding, Type weddingType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (string fieldName in DerivedInitializers)
            {
                FieldInfo field = weddingType.GetField(fieldName, flags);
                if (field == null)
                    throw new MissingFieldException(weddingType.FullName, fieldName);

                object value = field.FieldType == typeof(Random)
                    ? new Random()
                    : Activator.CreateInstance(field.FieldType);

                field.SetValue(wedding, value);
            }
        }
    }

    [HarmonyPatch]
    internal static class ProcessionRepairPatch
    {
        private static MethodBase TargetMethod()
        {
            Type jobGiver = AccessTools.TypeByName("RomanceOnTheRim.JobGiver_Procession");
            if (jobGiver == null)
                throw new MissingMemberException("RomanceOnTheRim.JobGiver_Procession was not found.");

            MethodInfo method = AccessTools.Method(jobGiver, "IsMyTurn");
            if (method == null)
                throw new MissingMethodException("RomanceOnTheRim.JobGiver_Procession.IsMyTurn was not found.");

            return method;
        }

        private static void Prefix(object[] __args)
        {
            try
            {
                object wedding = RotRCompat.FindWeddingArg(__args);
                if (wedding != null)
                    RotRCompat.RepairMissingProcessionPairs(wedding);
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Could not repair procession state: " + ex);
            }
        }
    }

    [HarmonyPatch]
    internal static class WeddingParticipantCountPatch
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

        private static bool Prefix(object __instance, object[] __args, ref float __result)
        {
            if (__args == null || __args.Length < 2 || !RotRCompat.IsWedding(__args[0]))
                return true;

            try
            {
                __result = RotRCompat.CountWeddingAttendance(__instance, __args[0], __args[1]);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Could not apply wedding attendance fix; using vanilla count: " + ex);
                return true;
            }
        }
    }
}
