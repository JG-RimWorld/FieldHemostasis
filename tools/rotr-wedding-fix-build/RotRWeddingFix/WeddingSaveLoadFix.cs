using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.Serialization;
using Verse;

[assembly: AssemblyVersion("1.0.0.0")]

namespace JG.RotRWeddingFix
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            try
            {
                new Harmony("jg.rotr.weddingfix").PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[RotR Wedding Fix] Save/load compatibility patch active.");
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
        private const string WeddingTypeName = "RomanceOnTheRim.LordJob_WeddingCeremony";
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
            if (type == null || type.FullName != WeddingTypeName)
                return true;

            // Normal gameplay creates weddings through RotR's real five-argument constructor.
            // Only intervene when Scribe is trying to deserialize through a parameterless ctor.
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
                // Fall back to vanilla so the original error remains visible rather than
                // silently replacing the wedding with a partially initialized object.
                Log.Error("[RotR Wedding Fix] Could not reconstruct LordJob_WeddingCeremony: " + ex);
                return true;
            }
        }

        private static object CreateWeddingForDeserialization(Type weddingType)
        {
            Type baseType = weddingType.BaseType;
            if (baseType == null)
                throw new InvalidOperationException("Wedding LordJob has no base type.");

            // RotR omitted a parameterless constructor. Allocate the derived instance without
            // running its five-argument constructor, then reproduce the state that C# would
            // normally establish through base constructors and derived field initializers.
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
}
