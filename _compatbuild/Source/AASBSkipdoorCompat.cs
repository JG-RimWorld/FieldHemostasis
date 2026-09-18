using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AASBSkipdoorCompat
{
    public sealed class CompatMod : Mod
    {
        public CompatMod(ModContentPack content) : base(content)
        {
            var harmony = new Harmony(content.PackageIdPlayerFacing);
            harmony.PatchAll();
            LongEventHandler.ExecuteWhenFinished(CompatReflection.Initialize);
        }
    }

    internal static class CompatState
    {
        [ThreadStatic] public static bool BypassCrossBandGuard;
        [ThreadStatic] public static bool DisableSkipdoors;
    }

    internal sealed class RoutePermit
    {
        public Map Map;
        public IntVec3 Start;
        public IntVec3 Dest;
        public int PawnId;
        public int ExpiresAtTick;
    }

    internal static class RoutePermits
    {
        private static readonly List<RoutePermit> permits = new List<RoutePermit>();
        private const int LifetimeTicks = 180;

        public static void Add(Pawn pawn, LocalTargetInfo dest)
        {
            if (pawn == null || pawn.Map == null || !dest.IsValid)
                return;

            Cleanup();
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            int pawnId = pawn.thingIDNumber;
            IntVec3 start = pawn.Position;
            IntVec3 target = dest.Cell;

            for (int i = 0; i < permits.Count; i++)
            {
                RoutePermit p = permits[i];
                if (p.Map == pawn.Map && p.PawnId == pawnId && p.Start == start && p.Dest == target)
                {
                    p.ExpiresAtTick = now + LifetimeTicks;
                    return;
                }
            }

            permits.Add(new RoutePermit
            {
                Map = pawn.Map,
                Start = start,
                Dest = target,
                PawnId = pawnId,
                ExpiresAtTick = now + LifetimeTicks
            });
        }

        public static bool Matches(Map map, IntVec3 start, LocalTargetInfo target)
        {
            if (map == null || !target.IsValid)
                return false;

            Cleanup();
            IntVec3 dest = target.Cell;
            for (int i = 0; i < permits.Count; i++)
            {
                RoutePermit p = permits[i];
                if (p.Map == map && p.Start == start && p.Dest == dest)
                    return true;
            }
            return false;
        }

        public static bool Matches(PathRequest request)
        {
            if (request == null || request.map == null || !request.Target.IsValid)
                return false;

            Cleanup();
            int pawnId = request.pawn != null ? request.pawn.thingIDNumber : -1;
            IntVec3 start = request.Start;
            IntVec3 dest = request.Target.Cell;

            for (int i = 0; i < permits.Count; i++)
            {
                RoutePermit p = permits[i];
                if (p.Map != request.map || p.Start != start || p.Dest != dest)
                    continue;
                if (pawnId < 0 || p.PawnId == pawnId)
                    return true;
            }
            return false;
        }

        private static void Cleanup()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            for (int i = permits.Count - 1; i >= 0; i--)
            {
                RoutePermit p = permits[i];
                if (p.Map == null || now > p.ExpiresAtTick || now + LifetimeTicks < p.ExpiresAtTick)
                    permits.RemoveAt(i);
            }
        }
    }

    internal static class CompatReflection
    {
        private static bool initialized;
        private static MethodInfo canUseTeleporters;
        private static MethodInfo getAllTeleporters;
        private static MethodInfo tryGetTransit;
        private static MethodInfo bandsBanded;

        public static bool Ready => initialized && canUseTeleporters != null && getAllTeleporters != null
            && tryGetTransit != null && bandsBanded != null;

        public static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            Type pathUtils = AccessTools.TypeByName("VPE_Skipdoor_Pathing.PathfindingUtils");
            canUseTeleporters = AccessTools.Method(pathUtils, "CanUseTeleporters");
            getAllTeleporters = AccessTools.Method(pathUtils, "GetAllTeleporters");

            Type wormhole = AccessTools.TypeByName("AsAboveSoBelow.ABWormhole");
            if (wormhole != null)
            {
                tryGetTransit = wormhole.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "TryGetTransit" && m.GetParameters().Length == 6
                        && m.GetParameters()[3].ParameterType == typeof(Pawn));
            }

            Type bands = AccessTools.TypeByName("AsAboveSoBelow.ABBands");
            bandsBanded = AccessTools.Method(bands, "Banded", new[] { typeof(Map) });

            if (!Ready)
            {
                Log.Warning("[AASB Skipdoor Compat] Could not resolve all integration methods. "
                    + "Patch will stay passive. AASB=" + (wormhole != null)
                    + ", Redux=" + (pathUtils != null) + ".");
            }
            else
            {
                Log.Message("[AASB Skipdoor Compat] Integration active.");
            }
        }

        public static bool IsBanded(Map map)
        {
            if (bandsBanded == null || map == null)
                return false;
            try { return (bool)bandsBanded.Invoke(null, new object[] { map }); }
            catch { return false; }
        }

        public static bool CanUseSkipdoors(Pawn pawn, LocalTargetInfo dest)
        {
            if (canUseTeleporters == null)
                return false;
            try { return (bool)canUseTeleporters.Invoke(null, new object[] { pawn, dest }); }
            catch { return false; }
        }

        public static HashSet<IntVec3> GetSkipdoors(Map map)
        {
            var result = new HashSet<IntVec3>();
            if (getAllTeleporters == null || map == null)
                return result;

            try
            {
                object enumerable = getAllTeleporters.Invoke(null, new object[] { map });
                if (enumerable is IEnumerable values)
                {
                    foreach (object value in values)
                    {
                        if (value is IntVec3 cell)
                            result.Add(cell);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[AASB Skipdoor Compat] Reading skipdoors failed: " + e.GetType().Name);
            }
            return result;
        }

        public static bool TryGetStairTransit(Map map, IntVec3 from, IntVec3 to, Pawn pawn,
            out Thing near, out Thing far)
        {
            near = null;
            far = null;
            if (tryGetTransit == null)
                return false;

            try
            {
                object[] args = { map, from, to, pawn, null, null };
                bool ok = (bool)tryGetTransit.Invoke(null, args);
                if (!ok)
                    return false;
                near = args[4] as Thing;
                far = args[5] as Thing;
                return near != null && far != null;
            }
            catch (TargetInvocationException e)
            {
                Log.Warning("[AASB Skipdoor Compat] Stair planner probe failed: "
                    + (e.InnerException != null ? e.InnerException.GetType().Name : e.GetType().Name));
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class RouteChooser
    {
        private const float StairFlightCost = 7f;
        private const int MaxStairHops = 12;

        public static bool ShouldUseSkipdoor(Pawn pawn, LocalTargetInfo dest, PathEndMode peMode)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || !dest.IsValid)
                return false;

            CompatReflection.Initialize();
            if (!CompatReflection.Ready || !CompatReflection.IsBanded(pawn.Map))
                return false;
            if (!CompatReflection.CanUseSkipdoors(pawn, dest))
                return false;

            HashSet<IntVec3> skipdoors = CompatReflection.GetSkipdoors(pawn.Map);
            if (skipdoors.Count < 2)
                return false;

            float skipCost;
            if (!TryPathCost(pawn, pawn.Position, dest, peMode, skipdoors,
                    allowSkipdoors: true, bypassCrossBand: true, requireTeleport: true, out skipCost))
                return false;

            float stairCost;
            if (!TryStairRouteCost(pawn, dest, peMode, out stairCost))
                return true;

            return skipCost <= stairCost + 0.05f;
        }

        private static bool TryStairRouteCost(Pawn pawn, LocalTargetInfo dest, PathEndMode peMode,
            out float total)
        {
            total = 0f;
            Map map = pawn.Map;
            IntVec3 current = pawn.Position;
            var visited = new HashSet<int>();

            bool oldDisable = CompatState.DisableSkipdoors;
            CompatState.DisableSkipdoors = true;
            try
            {
                for (int hop = 0; hop < MaxStairHops; hop++)
                {
                    Thing near;
                    Thing far;
                    if (!CompatReflection.TryGetStairTransit(map, current, dest.Cell, pawn, out near, out far))
                    {
                        float finalCost;
                        if (!TryPathCost(pawn, current, dest, peMode, null,
                                allowSkipdoors: false, bypassCrossBand: false,
                                requireTeleport: false, out finalCost))
                            return false;
                        total += finalCost;
                        return true;
                    }

                    int edgeKey = near.thingIDNumber * 397 ^ far.thingIDNumber;
                    if (!visited.Add(edgeKey))
                        return false;

                    float walkCost;
                    if (!TryPathCost(pawn, current, new LocalTargetInfo(near), PathEndMode.Touch,
                            null, allowSkipdoors: false, bypassCrossBand: false,
                            requireTeleport: false, out walkCost))
                        return false;

                    total += walkCost + StairFlightCost;
                    current = far.Position;
                }
            }
            finally
            {
                CompatState.DisableSkipdoors = oldDisable;
            }
            return false;
        }

        private static bool TryPathCost(Pawn pawn, IntVec3 start, LocalTargetInfo dest,
            PathEndMode peMode, HashSet<IntVec3> skipdoors, bool allowSkipdoors,
            bool bypassCrossBand, bool requireTeleport, out float cost)
        {
            cost = 0f;
            bool oldBypass = CompatState.BypassCrossBandGuard;
            bool oldDisable = CompatState.DisableSkipdoors;
            CompatState.BypassCrossBandGuard = bypassCrossBand;
            CompatState.DisableSkipdoors = !allowSkipdoors;

            try
            {
                using (PawnPath path = pawn.Map.pathFinder.FindPathNow(start, dest, pawn, null, peMode))
                {
                    if (path == null || !path.Found)
                        return false;

                    List<IntVec3> nodes = path.NodesReversed;
                    if (nodes == null || nodes.Count < 1)
                        return !requireTeleport;

                    bool usedTeleport = false;
                    float sum = 0f;
                    for (int i = 1; i < nodes.Count; i++)
                    {
                        IntVec3 a = nodes[i - 1];
                        IntVec3 b = nodes[i];
                        int dx = Math.Abs(a.x - b.x);
                        int dz = Math.Abs(a.z - b.z);

                        bool jump = dx > 1 || dz > 1;
                        bool skipJump = jump && skipdoors != null
                            && skipdoors.Contains(a) && skipdoors.Contains(b);
                        if (skipJump)
                        {
                            usedTeleport = true;
                            sum += 1f;
                        }
                        else if (dx != 0 && dz != 0)
                        {
                            sum += 1.41421356f;
                        }
                        else if (dx != 0 || dz != 0)
                        {
                            sum += 1f;
                        }
                    }

                    cost = sum;
                    return !requireTeleport || usedTeleport;
                }
            }
            catch (Exception e)
            {
                Log.Warning("[AASB Skipdoor Compat] Path probe failed: " + e.GetType().Name);
                return false;
            }
            finally
            {
                CompatState.BypassCrossBandGuard = oldBypass;
                CompatState.DisableSkipdoors = oldDisable;
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_AASB_TrySegment
    {
        private static MethodBase TargetMethod()
        {
            Type t = AccessTools.TypeByName("AsAboveSoBelow.ABWormholePather");
            return AccessTools.Method(t, "TrySegment");
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn pawn, ref LocalTargetInfo dest, ref PathEndMode peMode,
            ref bool __result)
        {
            try
            {
                if (!RouteChooser.ShouldUseSkipdoor(pawn, dest, peMode))
                    return true;

                RoutePermits.Add(pawn, dest);
                __result = false;
                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[AASB Skipdoor Compat] Route choice failed; falling back to AASB: " + e);
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_AASB_CrossBand
    {
        private static MethodBase TargetMethod()
        {
            Type t = AccessTools.TypeByName("AsAboveSoBelow.ABPathBandScope");
            return AccessTools.Method(t, "CrossBand");
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Map map, IntVec3 start, LocalTargetInfo target,
            ref string why, ref bool __result)
        {
            if (!CompatState.BypassCrossBandGuard && !RoutePermits.Matches(map, start, target))
                return true;

            why = null;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class Patch_AASB_ScopeDoorJob
    {
        private static MethodBase TargetMethod()
        {
            Type t = AccessTools.TypeByName("AsAboveSoBelow.ABPathBandScope");
            return AccessTools.Method(t, "ScopeDoorJob");
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PathRequest request)
        {
            return !CompatState.BypassCrossBandGuard && !RoutePermits.Matches(request);
        }
    }

    [HarmonyPatch]
    internal static class Patch_Redux_CanUseTeleporters
    {
        private static MethodBase TargetMethod()
        {
            Type t = AccessTools.TypeByName("VPE_Skipdoor_Pathing.PathfindingUtils");
            return AccessTools.Method(t, "CanUseTeleporters");
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            if (!CompatState.DisableSkipdoors)
                return true;
            __result = false;
            return false;
        }
    }
}
