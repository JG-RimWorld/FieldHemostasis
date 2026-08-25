using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace JG.RotRWeddingFix
{
    [StaticConstructorOnStartup]
    internal static class WeddingCancellationDiagnosticsV4
    {
        static WeddingCancellationDiagnosticsV4()
        {
            try
            {
                Type weddingType = AccessTools.TypeByName(RotRCompat.WeddingTypeName);
                if (weddingType == null)
                {
                    Log.Warning("[RotR Wedding Fix] Cancellation diagnostics: wedding type not found.");
                    return;
                }

                Harmony harmony = new Harmony("jg.rotr.weddingfix.cancellationdiagnostics");
                int patchedNoSpace = 0;

                Type[] types = weddingType.Assembly.GetTypes();
                for (int i = 0; i < types.Length; i++)
                {
                    MethodInfo[] methods = types[i].GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                               BindingFlags.Public | BindingFlags.NonPublic);
                    for (int j = 0; j < methods.Length; j++)
                    {
                        if (methods[j].Name == "LeaveLordAsNoSpaceAvailable")
                        {
                            harmony.Patch(methods[j], prefix: new HarmonyMethod(typeof(WeddingCancellationDiagnosticsV4), nameof(BeforeNoSpaceLeave)));
                            patchedNoSpace++;
                        }
                    }
                }

                MethodInfo pawnLost = AccessTools.Method(weddingType, "Notify_PawnLost");
                if (pawnLost != null)
                    harmony.Patch(pawnLost, prefix: new HarmonyMethod(typeof(WeddingCancellationDiagnosticsV4), nameof(BeforePawnLost)));

                Log.Message("[RotR Wedding Fix] Wedding cancellation diagnostics v1.4 active; no-space methods patched: " + patchedNoSpace + ".");
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Could not install cancellation diagnostics: " + ex);
            }
        }

        private static void BeforeNoSpaceLeave(MethodBase __originalMethod, object __instance, object[] __args)
        {
            try
            {
                object pawn = FindPawn(__args);
                object wedding = FindWedding(__instance, __args, pawn);
                Log.Warning("[RotR Wedding Fix] RotR is ejecting a wedding pawn because no usable wedding/spectate position was found. " +
                            Describe(pawn, wedding) + " source=" + __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name + ".");
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Failed to log no-space wedding leave: " + ex);
            }
        }

        private static void BeforePawnLost(object __instance, object[] __args)
        {
            try
            {
                object pawn = FindPawn(__args);
                object condition = null;
                if (__args != null)
                {
                    for (int i = 0; i < __args.Length; i++)
                    {
                        if (__args[i] != null && (__args[i].GetType().IsEnum || __args[i].GetType().Name.Contains("PawnLostCondition")))
                        {
                            condition = __args[i];
                            break;
                        }
                    }
                }

                Log.Warning("[RotR Wedding Fix] Wedding Notify_PawnLost: " + Describe(pawn, __instance) +
                            " lostCondition=" + (condition != null ? condition.ToString() : "<unknown>") + ".");
            }
            catch (Exception ex)
            {
                Log.Error("[RotR Wedding Fix] Failed to log wedding pawn loss: " + ex);
            }
        }

        private static object FindPawn(object[] args)
        {
            if (args == null)
                return null;

            for (int i = 0; i < args.Length; i++)
            {
                object arg = args[i];
                if (arg != null && arg.GetType().FullName == "Verse.Pawn")
                    return arg;
            }
            return null;
        }

        private static object FindWedding(object instance, object[] args, object pawn)
        {
            if (RotRCompat.IsWedding(instance))
                return instance;

            object wedding = RotRCompat.FindWeddingArg(args);
            if (wedding != null)
                return wedding;

            if (pawn != null)
            {
                object mindState = GetMemberValue(pawn, "mindState");
                object duty = GetMemberValue(mindState, "duty");
                object lord = GetMemberValue(duty, "lord");
                object lordJob = GetMemberValue(lord, "LordJob") ?? GetMemberValue(lord, "lordJob");
                if (RotRCompat.IsWedding(lordJob))
                    return lordJob;
            }

            return null;
        }

        private static string Describe(object pawn, object wedding)
        {
            string pawnLabel = pawn != null ? GetPawnLabel(pawn) : "<unknown pawn>";
            string position = GetMemberValue(pawn, "Position")?.ToString() ?? "<unknown>";
            string duty = GetDefName(GetMemberValue(GetMemberValue(pawn, "mindState"), "duty"));
            object jobs = GetMemberValue(pawn, "jobs");
            object curJob = GetMemberValue(jobs, "curJob") ?? GetMemberValue(jobs, "CurJob");
            string job = GetDefName(curJob);

            string stage = "<unknown>";
            string role = "<none>";
            string required = "<unknown>";
            string substitutable = "<unknown>";

            if (wedding != null)
            {
                FieldInfo stageField = RotRCompat.FindField(wedding.GetType(), "stageIndex");
                if (stageField != null)
                    stage = Convert.ToString(stageField.GetValue(wedding));

                object assignments = RotRCompat.FindField(wedding.GetType(), "assignments")?.GetValue(wedding);
                object ritualRole = GetRoleForPawn(assignments, pawn);
                if (ritualRole != null)
                {
                    role = Convert.ToString(GetMemberValue(ritualRole, "id") ?? GetMemberValue(ritualRole, "label") ?? ritualRole.GetType().Name);
                    required = Convert.ToString(GetMemberValue(ritualRole, "required") ?? "<unknown>");
                    substitutable = Convert.ToString(GetMemberValue(ritualRole, "substitutable") ?? "<unknown>");
                }
            }

            return "pawn=" + pawnLabel +
                   ", stage=" + stage +
                   ", role=" + role +
                   ", required=" + required +
                   ", substitutable=" + substitutable +
                   ", pos=" + position +
                   ", duty=" + duty +
                   ", job=" + job;
        }

        private static object GetRoleForPawn(object assignments, object pawn)
        {
            if (assignments == null || pawn == null)
                return null;

            MethodInfo[] methods = assignments.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "RoleForPawn")
                    continue;

                ParameterInfo[] pars = methods[i].GetParameters();
                try
                {
                    if (pars.Length == 1)
                        return methods[i].Invoke(assignments, new[] { pawn });
                    if (pars.Length == 2 && pars[1].ParameterType == typeof(bool))
                        return methods[i].Invoke(assignments, new object[] { pawn, true });
                }
                catch
                {
                }
            }
            return null;
        }

        private static object GetMemberValue(object obj, string name)
        {
            if (obj == null)
                return null;

            PropertyInfo property = RotRCompat.FindProperty(obj.GetType(), name);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try { return property.GetValue(obj, null); } catch { }
            }

            FieldInfo field = RotRCompat.FindField(obj.GetType(), name);
            if (field != null)
            {
                try { return field.GetValue(obj); } catch { }
            }

            return null;
        }

        private static string GetPawnLabel(object pawn)
        {
            object label = GetMemberValue(pawn, "LabelShort") ?? GetMemberValue(pawn, "LabelCap") ?? GetMemberValue(pawn, "Name");
            return label != null ? label.ToString() : pawn.ToString();
        }

        private static string GetDefName(object obj)
        {
            if (obj == null)
                return "<none>";

            object def = GetMemberValue(obj, "def");
            object defName = GetMemberValue(def, "defName");
            return defName != null ? defName.ToString() : obj.GetType().Name;
        }
    }
}
