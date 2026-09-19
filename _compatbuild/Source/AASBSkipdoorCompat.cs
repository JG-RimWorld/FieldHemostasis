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
            Log.Message("[AASB Skipdoor Compat] Harmony patches installed v0.2.0.");
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
        public IntVec3 Dest;
        public int PawnId;
        public int ExpiresAtTick;
    }

    internal static class RoutePermits
    {
        private static readonly List<RoutePermit> permits = new List<RoutePermit>();
        private const int LifetimeTicks = 600;

        public static void Add(Pawn pawn, LocalTargetInfo dest)
        {
            if (pawn == null || pawn.Map == null || !dest.IsValid)
                return;

            Cleanup();
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            int pawnId = pawn.thingIDNumber;
            IntVec3 target = dest.Cell;

            for (int i = 0; i < permits.Count; i++)
            {
                RoutePermit p = permits[i];
                if (p.Map == pawn.Map && p.PawnId == pawnId && p.Dest == target)
                {
                    p.ExpiresAtTick = now + LifetimeTicks;
                    return;
                }
            }

            permits.Add(new RoutePermit
            {
                Map = pawn.Map,
                Dest = target,
                PawnId = pawnId,
                ExpiresAtTick = now + LifetimeTicks
            });
        }

        public static bool Matches(Map map, LocalTargetInfo target)
        {
            if (map == null || !target.IsValid)
                return false;

            Cleanup();
            IntVec3 dest = target.Cell;
            for (int i = 0; i < permits.Count; i++)
            {
                RoutePermit p = permits[i];
                if (p.Map == map && p.Dest == dest)
                    return true;
            }
            return false;
        }

        public static bool Matches(Pawn pawn, LocalTargetInfo target)
        {
            if (pawn == null || pawn.Map == null || !target.IsValid)
                return false;

            Cleanup();
            int pawnId = pawn.thingIDNumber;
            IntVec3 dest = target.Cell;
            for (int i = 0; i < permits.Count; i++)
            {
                RoutePermit p = permits[i];
                if (p.Map == pawn.Map && p.PawnId == pawnId && p.Dest == dest)
                    return true;
            }
            return false;
        }

        public static bool Matches(PathRequest request)
        {
            if (request == null || request.map == null || !request.Target.IsValid || request.pawn == null)
                return false;

            Cleanup();
            int pawnId = request.pawn.thingIDNumber;
            IntVec3 dest = request.Target.Cell;

            for (int i = 0; i < permits.Count; i++)
            {
                RoutePermit p = permits[i];
                if (p.Map == request.map && p.PawnId == pawnId && p.Dest == dest)
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


    internal sealed class SkipTransit
    {
        public Map Map;
        public Thing Source;
        public Thing Destination;
        public LocalTargetInfo RealDestination;
        public PathEndMode RealEndMode;
        public Job Job;
        public int ExpiresAtTick;
    }

    internal static class SkipTransits
    {
        private static readonly Dictionary<int, SkipTransit> pending =
            new Dictionary<int, SkipTransit>();
        private const int LifetimeTicks = 4000;

        public static bool TryGet(Pawn pawn, out SkipTransit transit)
        {
            transit = null;
            if (pawn == null)
                return false;
            Cleanup();
            return pending.TryGetValue(pawn.thingIDNumber, out transit);
        }

        public static void Set(Pawn pawn, Thing source, Thing destination,
            LocalTargetInfo realDestination, PathEndMode realEndMode)
        {
            if (pawn == null || pawn.Map == null || source == null || destination == null)
                return;

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            pending[pawn.thingIDNumber] = new SkipTransit
            {
                Map = pawn.Map,
                Source = source,
                Destination = destination,
                RealDestination = realDestination,
                RealEndMode = realEndMode,
                Job = pawn.CurJob,
                ExpiresAtTick = now + LifetimeTicks
            };
        }

        public static void Clear(Pawn pawn)
        {
            if (pawn != null)
                pending.Remove(pawn.thingIDNumber);
        }

        private static void Cleanup()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            var remove = new List<int>();
            foreach (KeyValuePair<int, SkipTransit> kv in pending)
            {
                SkipTransit t = kv.Value;
                if (t == null || t.Map == null || now > t.ExpiresAtTick
                    || now + LifetimeTicks < t.ExpiresAtTick)
                    remove.Add(kv.Key);
            }
            for (int i = 0; i < remove.Count; i++)
                pending.Remove(remove[i]);
        }
    }

    internal static class CompatReflection
    {
        private static bool initialized;
        private static MethodInfo canUseTeleporters;
        private static MethodInfo getAllTeleporters;
        private static MethodInfo tryGetTransit;
        private static MethodInfo bandsBanded;
        private static MethodInfo bandsBandOf;
        private static Type doorTeleporterType;

        public static bool Ready => initialized && canUseTeleporters != null && getAllTeleporters != null
            && tryGetTransit != null && bandsBanded != null && bandsBandOf != null
            && doorTeleporterType != null;

        public static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            Type pathUtils = AccessTools.TypeByName("VPE_Skipdoor_Pathing.PathfindingUtils");
            canUseTeleporters = AccessTools.Method(pathUtils, "CanUseTeleporters",
                new[] { typeof(Pawn), typeof(LocalTargetInfo) });
            getAllTeleporters = AccessTools.Method(pathUtils, "GetAllTeleporters",
                new[] { typeof(Map) });

            doorTeleporterType = AccessTools.TypeByName("VEF.Buildings.DoorTeleporter");

            Type wormhole = AccessTools.TypeByName("AsAboveSoBelow.ABWormhole");
            if (wormhole != null)
            {
                tryGetTransit = wormhole.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "TryGetTransit" && m.GetParameters().Length == 6
                        && m.GetParameters()[3].ParameterType == typeof(Pawn));
            }

            Type bands = AccessTools.TypeByName("AsAboveSoBelow.ABBands");
            bandsBanded = AccessTools.Method(bands, "Banded", new[] { typeof(Map) });
            bandsBandOf = AccessTools.Method(bands, "BandOf", new[] { typeof(Map), typeof(IntVec3) });

            if (!Ready)
            {
                Log.Warning("[AASB Skipdoor Compat] Could not resolve all integration methods. "
                    + "Patch will stay passive. AASB=" + (wormhole != null)
                    + ", Redux=" + (pathUtils != null)
                    + ", VEF=" + (doorTeleporterType != null) + ".");
            }
            else
            {
                Log.Message("[AASB Skipdoor Compat] Integration active v0.2.0.");
            }
        }

        public static bool IsBanded(Map map)
        {
            if (bandsBanded == null || map == null)
                return false;
            try { return (bool)bandsBanded.Invoke(null, new object[] { map }); }
            catch { return false; }
        }


        public static int BandOf(Map map, IntVec3 cell)
        {
            if (bandsBandOf == null || map == null || !cell.IsValid)
                return -1;
            try { return (int)bandsBandOf.Invoke(null, new object[] { map, cell }); }
            catch { return -1; }
        }

        public static Thing SkipdoorAt(Map map, IntVec3 cell)
        {
            if (map == null || doorTeleporterType == null || !cell.InBounds(map))
                return null;

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && thing.Spawned
                    && doorTeleporterType.IsAssignableFrom(thing.GetType()))
                    return thing;
            }
            return null;
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
        [ThreadStatic] private static IntVec3 chosenJumpA;
        [ThreadStatic] private static IntVec3 chosenJumpB;

        public static bool TryChooseSkipdoor(Pawn pawn, LocalTargetInfo dest, PathEndMode peMode,
            out Thing source, out Thing destination)
        {
            source = null;
            destination = null;
            if (pawn == null || !pawn.Spawned || pawn.Map == null || !dest.IsValid)
                return false;

            // FindPathNow may be reached while a save/map is still loading on the long-event
            // thread. Redux allocates Native Temp containers there, which Unity rejects.
            // Routing decisions are safe to defer until normal play resumes.
            if (Scribe.mode != LoadSaveMode.Inactive
                || LongEventHandler.AnyEventNowOrWaiting
                || Current.ProgramState != ProgramState.Playing)
                return false;

            CompatReflection.Initialize();
            if (!CompatReflection.Ready || !CompatReflection.IsBanded(pawn.Map))
                return false;

            // Only compete on journeys AASB can actually segment through a vertical link.
            // Same-band travel remains entirely Redux's responsibility.
            float stairCost;
            if (!TryStairRouteCost(pawn, dest, peMode, out stairCost))
                return false;

            if (!CompatReflection.CanUseSkipdoors(pawn, dest))
            {
                TraceDecision(pawn, "AASB stair route cost " + stairCost.ToString("0.0")
                    + "; Redux says this pawn/job cannot use skipdoors.");
                return false;
            }

            HashSet<IntVec3> skipdoors = CompatReflection.GetSkipdoors(pawn.Map);
            if (skipdoors.Count < 2)
            {
                TraceDecision(pawn, "AASB stair route cost " + stairCost.ToString("0.0")
                    + "; fewer than two usable skipdoors found.");
                return false;
            }

            float skipCost;
            if (!TryPathCost(pawn, pawn.Position, dest, peMode, skipdoors,
                    allowSkipdoors: true, bypassCrossBand: true, requireTeleport: true, out skipCost))
            {
                TraceDecision(pawn, "AASB stair route cost " + stairCost.ToString("0.0")
                    + "; Redux cross-band probe found no valid skipdoor route.");
                return false;
            }

            int pawnBand = CompatReflection.BandOf(pawn.Map, pawn.Position);
            int bandA = CompatReflection.BandOf(pawn.Map, chosenJumpA);
            int bandB = CompatReflection.BandOf(pawn.Map, chosenJumpB);

            if (!chosenJumpA.IsValid || !chosenJumpB.IsValid || bandA < 0 || bandB < 0
                || bandA == bandB)
            {
                TraceDecision(pawn, "Redux route used only same-band skipdoors; leaving cross-band travel to AASB.");
                return false;
            }

            IntVec3 sourceCell;
            IntVec3 destinationCell;
            if (bandA == pawnBand)
            {
                sourceCell = chosenJumpA;
                destinationCell = chosenJumpB;
            }
            else if (bandB == pawnBand)
            {
                sourceCell = chosenJumpB;
                destinationCell = chosenJumpA;
            }
            else
            {
                TraceDecision(pawn, "Redux cross-band jump does not start in pawn's current band; leaving it to AASB.");
                return false;
            }

            source = CompatReflection.SkipdoorAt(pawn.Map, sourceCell);
            destination = CompatReflection.SkipdoorAt(pawn.Map, destinationCell);
            if (source == null || destination == null)
            {
                TraceDecision(pawn, "Redux jump endpoints vanished before segmentation.");
                source = null;
                destination = null;
                return false;
            }

            bool useSkipdoor = skipCost <= stairCost + 0.05f;
            TraceDecision(pawn, "route to " + dest.Cell + ": cross-band skipdoor "
                + sourceCell + " [band " + pawnBand + "] -> " + destinationCell
                + " [band " + CompatReflection.BandOf(pawn.Map, destinationCell) + "] cost "
                + skipCost.ToString("0.0") + " vs stairs " + stairCost.ToString("0.0")
                + " -> " + (useSkipdoor ? "SKIPDOOR" : "STAIRS"));
            if (!useSkipdoor)
            {
                source = null;
                destination = null;
            }
            return useSkipdoor;
        }

        private static void TraceDecision(Pawn pawn, string message)
        {
            if (pawn != null && pawn.IsColonistPlayerControlled)
                Log.Message("[AASB Skipdoor Compat] " + pawn.LabelShort + ": " + message);
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
            chosenJumpA = IntVec3.Invalid;
            chosenJumpB = IntVec3.Invalid;
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
                            int bandA = CompatReflection.BandOf(pawn.Map, a);
                            int bandB = CompatReflection.BandOf(pawn.Map, b);
                            if (bandA >= 0 && bandB >= 0 && bandA != bandB && !usedTeleport)
                            {
                                usedTeleport = true;
                                chosenJumpA = a;
                                chosenJumpB = b;
                            }
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
            return AccessTools.Method(t, "TrySegment", new[]
            {
                typeof(Pawn),
                typeof(LocalTargetInfo).MakeByRefType(),
                typeof(PathEndMode).MakeByRefType()
            });
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn pawn, ref LocalTargetInfo dest, ref PathEndMode peMode,
            ref bool __result)
        {
            try
            {
                SkipTransit existing;
                if (SkipTransits.TryGet(pawn, out existing))
                {
                    bool sameJob = existing.Job == null || pawn.CurJob == existing.Job;
                    bool ourLeg = sameJob && existing.Source != null && existing.Source.Spawned
                        && dest.IsValid && dest.Cell.InHorDistOf(existing.Source.Position, 2f);
                    if (ourLeg)
                        return true;
                    SkipTransits.Clear(pawn);
                }

                Thing source;
                Thing destination;
                if (!RouteChooser.TryChooseSkipdoor(pawn, dest, peMode, out source, out destination))
                    return true;

                LocalTargetInfo realDest = dest;
                PathEndMode realMode = peMode;
                SkipTransits.Set(pawn, source, destination, realDest, realMode);

                dest = new LocalTargetInfo(source);
                peMode = PathEndMode.Touch;

                if (pawn != null && pawn.IsColonistPlayerControlled)
                    Log.Message("[AASB Skipdoor Compat] " + pawn.LabelShort
                        + ": segmented cross-band trip at skipdoor " + source.Position
                        + " -> " + destination.Position + "; walking to source first.");

                // AASB now sees only the local leg to the source skipdoor.
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[AASB Skipdoor Compat] Route choice failed; falling back to AASB: " + e);
                SkipTransits.Clear(pawn);
                return true;
            }
        }
    }



    [HarmonyPatch(typeof(Pawn_PathFollower), "PatherArrived")]
    internal static class Patch_PathFollower_ConsumeSkipTransit
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            try
            {
                return !TryConsume(__instance, ___pawn);
            }
            catch (Exception e)
            {
                Log.Warning("[AASB Skipdoor Compat] Explicit skipdoor transit failed: " + e);
                SkipTransits.Clear(___pawn);
                return true;
            }
        }

        private static bool TryConsume(Pawn_PathFollower pather, Pawn pawn)
        {
            SkipTransit transit;
            if (!SkipTransits.TryGet(pawn, out transit))
                return false;

            if (transit.Job != null && pawn.CurJob != transit.Job)
            {
                SkipTransits.Clear(pawn);
                return false;
            }

            LocalTargetInfo realDest = transit.RealDestination;
            PathEndMode realMode = transit.RealEndMode;

            if (transit.Map != pawn.Map || transit.Source == null || transit.Destination == null
                || !transit.Source.Spawned || !transit.Destination.Spawned)
            {
                SkipTransits.Clear(pawn);
                if (realDest.IsValid)
                {
                    pather.StartPath(realDest, realMode);
                    return true;
                }
                return false;
            }

            if (!pawn.Position.InHorDistOf(transit.Source.Position, 2f))
                return false;

            IntVec3 landing = FindLanding(pawn.Map, transit.Destination.Position);
            if (!landing.IsValid)
            {
                SkipTransits.Clear(pawn);
                pather.StartPath(realDest, realMode);
                return true;
            }

            int fromBand = CompatReflection.BandOf(pawn.Map, pawn.Position);
            int toBand = CompatReflection.BandOf(pawn.Map, landing);
            SkipTransits.Clear(pawn);

            pawn.teleporting = true;
            try
            {
                pawn.Position = landing;
            }
            finally
            {
                pawn.teleporting = false;
            }

            if (pawn.IsColonistPlayerControlled)
                Log.Message("[AASB Skipdoor Compat] " + pawn.LabelShort
                    + ": explicit skipdoor transit " + transit.Source.Position
                    + " [band " + fromBand + "] -> " + landing + " [band " + toBand
                    + "], resuming path to " + realDest.Cell + ".");

            pather.StartPath(realDest, realMode);
            return true;
        }

        private static IntVec3 FindLanding(Map map, IntVec3 center)
        {
            int band = CompatReflection.BandOf(map, center);
            IntVec3 occupiedFallback = IntVec3.Invalid;
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 c = center + GenAdj.CardinalDirections[i];
                if (!c.InBounds(map) || !c.Standable(map)
                    || CompatReflection.BandOf(map, c) != band)
                    continue;
                if (c.GetFirstPawn(map) == null)
                    return c;
                if (!occupiedFallback.IsValid)
                    occupiedFallback = c;
            }
            return occupiedFallback;
        }
    }

    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.PushRequest))]
    internal static class Patch_PathFinder_PermitAsync
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(PathRequest request, out bool __state)
        {
            __state = CompatState.BypassCrossBandGuard;
            if (!RoutePermits.Matches(request))
                return;

            CompatState.BypassCrossBandGuard = true;
            if (request.pawn != null && request.pawn.IsColonistPlayerControlled)
                Log.Message("[AASB Skipdoor Compat] " + request.pawn.LabelShort
                    + ": permitting async Redux cross-band request to " + request.Target.Cell);
        }

        private static void Postfix(bool __state)
        {
            CompatState.BypassCrossBandGuard = __state;
        }
    }

    [HarmonyPatch]
    internal static class Patch_PathFinder_PermitSync
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PathFinder), nameof(PathFinder.FindPathNow), new[]
            {
                typeof(IntVec3),
                typeof(LocalTargetInfo),
                typeof(TraverseParms),
                typeof(PathFinderCostTuning?),
                typeof(PathEndMode),
                typeof(PathRequest.IPathGridCustomizer)
            });
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(LocalTargetInfo target, TraverseParms traverseParms, out bool __state)
        {
            __state = CompatState.BypassCrossBandGuard;
            Pawn pawn = traverseParms.pawn;
            if (!RoutePermits.Matches(pawn, target))
                return;

            CompatState.BypassCrossBandGuard = true;
            if (pawn != null && pawn.IsColonistPlayerControlled)
                Log.Message("[AASB Skipdoor Compat] " + pawn.LabelShort
                    + ": permitting sync Redux cross-band request to " + target.Cell);
        }

        private static void Postfix(bool __state)
        {
            CompatState.BypassCrossBandGuard = __state;
        }
    }

    [HarmonyPatch]
    internal static class Patch_AASB_CrossBand
    {
        private static MethodBase TargetMethod()
        {
            Type t = AccessTools.TypeByName("AsAboveSoBelow.ABPathBandScope");
            return AccessTools.Method(t, "CrossBand", new[]
            {
                typeof(Map),
                typeof(IntVec3),
                typeof(LocalTargetInfo),
                typeof(string).MakeByRefType()
            });
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Map map, IntVec3 start, LocalTargetInfo target,
            ref string why, ref bool __result)
        {
            if (!CompatState.BypassCrossBandGuard && !RoutePermits.Matches(map, target))
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
            return AccessTools.Method(t, "ScopeDoorJob", new[]
            {
                typeof(PathGridDoorsBlockedJob),
                typeof(PathRequest)
            });
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
            return AccessTools.Method(t, "CanUseTeleporters",
                new[] { typeof(Pawn), typeof(LocalTargetInfo) });
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
