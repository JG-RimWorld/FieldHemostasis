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
            Log.Message("[AASB Skipdoor Compat] Harmony patches installed v0.3.1.");
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
        private static MethodInfo componentOfPawn;
        private static FieldInfo wormholeByMap;
        private static Type doorTeleporterType;

        public static bool Ready => initialized && canUseTeleporters != null && getAllTeleporters != null
            && tryGetTransit != null && bandsBanded != null && bandsBandOf != null
            && componentOfPawn != null && wormholeByMap != null && doorTeleporterType != null;

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
                wormholeByMap = AccessTools.Field(wormhole, "byMap");
            }

            Type components = AccessTools.TypeByName("AsAboveSoBelow.ABBandComponents");
            componentOfPawn = AccessTools.Method(components, "ComponentOf",
                new[] { typeof(Map), typeof(IntVec3), typeof(Pawn) });

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
                Log.Message("[AASB Skipdoor Compat] Integration active v0.3.1.");
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

        public static int ComponentOf(Map map, IntVec3 cell, Pawn pawn)
        {
            if (componentOfPawn == null || map == null || !cell.IsValid)
                return -1;
            try { return (int)componentOfPawn.Invoke(null, new object[] { map, cell, pawn }); }
            catch { return -1; }
        }

        internal sealed class StairPair
        {
            public Thing A;
            public Thing B;
        }

        public static List<StairPair> GetStairPairs(Map map)
        {
            var result = new List<StairPair>();
            if (wormholeByMap == null || map == null)
                return result;

            try
            {
                object table = wormholeByMap.GetValue(null);
                if (table == null)
                    return result;

                MethodInfo tryGet = table.GetType().GetMethod("TryGetValue");
                if (tryGet == null)
                    return result;

                object[] args = { map, null };
                bool found = (bool)tryGet.Invoke(table, args);
                if (!found || !(args[1] is IEnumerable list))
                    return result;

                foreach (object pair in list)
                {
                    if (pair == null)
                        continue;
                    Type pt = pair.GetType();
                    FieldInfo af = AccessTools.Field(pt, "a");
                    FieldInfo bf = AccessTools.Field(pt, "b");
                    Thing a = af?.GetValue(pair) as Thing;
                    Thing b = bf?.GetValue(pair) as Thing;
                    if (a != null && b != null && a.Spawned && b.Spawned
                        && a.Map == map && b.Map == map)
                        result.Add(new StairPair { A = a, B = b });
                }
            }
            catch (Exception e)
            {
                Log.Warning("[AASB Skipdoor Compat] Reading AASB stair graph failed: "
                    + e.GetType().Name);
            }
            return result;
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
        // Same unit AASB uses: one crossing is roughly seven walk cells.
        private const float StairFlightCost = 7f;
        // Redux currently prices a skipdoor jump as one cardinal move.
        private const float SkipFlightCost = 1f;
        private const float VerifyEpsilon = 0.5f;
        private const int MaxVerifyRounds = 4;

        private sealed class Node
        {
            public Thing Thing;
            public IntVec3 Cell;
            public int Component;
            public bool Skipdoor;
        }

        private struct Edge
        {
            public int To;
            public float Cost;
            public Edge(int to, float cost)
            {
                To = to;
                Cost = cost;
            }
        }

        private sealed class Heap
        {
            private readonly List<KeyValuePair<float, int>> data =
                new List<KeyValuePair<float, int>>();

            public int Count => data.Count;

            public void Push(float priority, int node)
            {
                int i = data.Count;
                data.Add(new KeyValuePair<float, int>(priority, node));
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (data[p].Key <= priority)
                        break;
                    data[i] = data[p];
                    i = p;
                }
                data[i] = new KeyValuePair<float, int>(priority, node);
            }

            public KeyValuePair<float, int> Pop()
            {
                KeyValuePair<float, int> root = data[0];
                int lastIndex = data.Count - 1;
                KeyValuePair<float, int> last = data[lastIndex];
                data.RemoveAt(lastIndex);
                if (data.Count == 0)
                    return root;

                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1;
                    if (left >= data.Count)
                        break;
                    int right = left + 1;
                    int child = right < data.Count && data[right].Key < data[left].Key
                        ? right : left;
                    if (data[child].Key >= last.Key)
                        break;
                    data[i] = data[child];
                    i = child;
                }
                data[i] = last;
                return root;
            }
        }

        public static bool TryChooseSkipdoor(Pawn pawn, LocalTargetInfo dest, PathEndMode peMode,
            out Thing source, out Thing destination)
        {
            source = null;
            destination = null;
            if (pawn == null || !pawn.Spawned || pawn.Map == null || !dest.IsValid)
                return false;

            if (Scribe.mode != LoadSaveMode.Inactive
                || LongEventHandler.AnyEventNowOrWaiting
                || Current.ProgramState != ProgramState.Playing)
                return false;

            CompatReflection.Initialize();
            if (!CompatReflection.Ready || !CompatReflection.IsBanded(pawn.Map)
                || !CompatReflection.CanUseSkipdoors(pawn, dest))
                return false;

            Map map = pawn.Map;
            int startComp = CompatReflection.ComponentOf(map, pawn.Position, pawn);
            if (startComp < 0)
                return false;

            HashSet<int> destComps = DestinationComponents(map, dest.Cell, peMode, pawn);
            if (destComps.Count == 0 || destComps.Contains(startComp))
                return false; // ordinary local route; Redux owns it.

            List<CompatReflection.StairPair> stairs = CompatReflection.GetStairPairs(map);
            HashSet<IntVec3> skipCells = CompatReflection.GetSkipdoors(map);
            if (skipCells.Count < 2 || stairs.Count == 0)
                return false;

            // Build one connector node per AASB stair anchor and per skipdoor.
            // There is NO skipdoor<->skipdoor complete graph. All skipdoors meet at one
            // virtual hub below, so n skipdoors contribute O(n) hub edges.
            var nodes = new List<Node>();
            var indexByThing = new Dictionary<int, int>();
            var stairEdges = new List<KeyValuePair<int, int>>();
            var stairIndices = new List<int>();
            var skipIndices = new List<int>();

            Func<Thing, bool, int> addNode = (thing, isSkip) =>
            {
                if (thing == null || !thing.Spawned || thing.Map != map)
                    return -1;
                if (indexByThing.TryGetValue(thing.thingIDNumber, out int existing))
                    return existing;
                int comp = CompatReflection.ComponentOf(map, thing.Position, pawn);
                if (comp < 0)
                    return -1;
                int idx = nodes.Count;
                nodes.Add(new Node
                {
                    Thing = thing,
                    Cell = thing.Position,
                    Component = comp,
                    Skipdoor = isSkip
                });
                indexByThing.Add(thing.thingIDNumber, idx);
                if (isSkip) skipIndices.Add(idx);
                else stairIndices.Add(idx);
                return idx;
            };

            for (int i = 0; i < stairs.Count; i++)
            {
                int a = addNode(stairs[i].A, false);
                int b = addNode(stairs[i].B, false);
                if (a >= 0 && b >= 0 && a != b)
                    stairEdges.Add(new KeyValuePair<int, int>(a, b));
            }

            foreach (IntVec3 c in skipCells)
            {
                Thing t = CompatReflection.SkipdoorAt(map, c);
                addNode(t, true);
            }

            if (skipIndices.Count < 2 || nodes.Count == 0)
                return false;

            int hub = nodes.Count;
            int graphCount = hub + 1;

            // Adjacency only stores sparse special edges. Same-component walking is
            // generated on expansion. Critically, skipdoor-to-skipdoor walking edges are
            // omitted because teleporting through the hub is always <= walking between two
            // skipdoors and costs one cell.
            var special = new List<Edge>[graphCount];
            for (int i = 0; i < graphCount; i++)
                special[i] = new List<Edge>();

            for (int i = 0; i < stairEdges.Count; i++)
            {
                int a = stairEdges[i].Key;
                int b = stairEdges[i].Value;
                special[a].Add(new Edge(b, StairFlightCost));
                special[b].Add(new Edge(a, StairFlightCost));
            }

            float halfSkip = SkipFlightCost * 0.5f;
            for (int i = 0; i < skipIndices.Count; i++)
            {
                int g = skipIndices[i];
                special[g].Add(new Edge(hub, halfSkip));
                special[hub].Add(new Edge(g, halfSkip));
            }

            // Winner-verification only corrects the pawn-side leg. Mid-graph walk terms are
            // straight-line estimates, as in AASB's estimate mode, so building the graph
            // never launches O(n) or O(n^2) pathfinders.
            var verifiedWalkIn = new Dictionary<int, float>();
            for (int round = 0; round <= MaxVerifyRounds; round++)
            {
                float[] dist;
                int[] next;
                RunDijkstra(nodes, hub, special, stairIndices, skipIndices,
                    destComps, dest.Cell, out dist, out next);

                FirstHop stairHop = BestStairFirstHop(nodes, stairEdges, startComp,
                    pawn.Position, dist, verifiedWalkIn);
                FirstHop skipHop = BestSkipFirstHop(nodes, skipIndices, hub, startComp,
                    pawn.Position, dist, verifiedWalkIn);

                if (!skipHop.Valid)
                {
                    TraceDecision(pawn, "hybrid graph found no usable skipdoor first hop; AASB handles stairs.");
                    return false;
                }

                if (stairHop.Valid && stairHop.Total <= skipHop.Total + 0.05f)
                {
                    TraceDecision(pawn, "hybrid graph: stairs "
                        + stairHop.Total.ToString("0.0") + " <= skipdoor "
                        + skipHop.Total.ToString("0.0") + "; AASB handles first hop.");
                    return false;
                }

                // Verify only the candidate that is about to win. At most one real same-band
                // path probe per correction round; independent of the total number of gates.
                if (!verifiedWalkIn.ContainsKey(skipHop.Near))
                {
                    float real;
                    if (!TryLocalPathCost(pawn, pawn.Position,
                            new LocalTargetInfo(nodes[skipHop.Near].Thing),
                            PathEndMode.Touch, out real))
                    {
                        verifiedWalkIn[skipHop.Near] = float.PositiveInfinity;
                        continue;
                    }

                    verifiedWalkIn[skipHop.Near] = real;
                    float estimated = Octile(pawn.Position, nodes[skipHop.Near].Cell);
                    if (Math.Abs(real - estimated) > VerifyEpsilon)
                    {
                        TraceDecision(pawn, "verified skipdoor entry "
                            + nodes[skipHop.Near].Cell + ": "
                            + estimated.ToString("0.0") + " -> " + real.ToString("0.0")
                            + "; re-ranking.");
                        continue;
                    }
                }

                int exit = skipHop.Far;
                if (exit < 0 || exit >= nodes.Count || exit == skipHop.Near)
                    return false;

                source = nodes[skipHop.Near].Thing;
                destination = nodes[exit].Thing;
                if (source == null || destination == null || !source.Spawned || !destination.Spawned)
                {
                    source = null;
                    destination = null;
                    return false;
                }

                TraceDecision(pawn, "hybrid graph -> SKIPDOOR first: "
                    + source.Position + " [comp " + nodes[skipHop.Near].Component + "] -> "
                    + destination.Position + " [comp " + nodes[exit].Component + "]"
                    + "; estimated full route " + skipHop.Total.ToString("0.0")
                    + (stairHop.Valid ? " vs stair-first " + stairHop.Total.ToString("0.0") : "")
                    + "; gates=" + skipIndices.Count + ", stairs=" + stairEdges.Count + ".");
                return true;
            }
            return false;
        }

        private struct FirstHop
        {
            public bool Valid;
            public int Near;
            public int Far;
            public float Total;
        }

        private static FirstHop BestStairFirstHop(List<Node> nodes,
            List<KeyValuePair<int, int>> pairs, int startComp, IntVec3 start,
            float[] dist, Dictionary<int, float> verified)
        {
            FirstHop best = default(FirstHop);
            best.Total = float.PositiveInfinity;
            for (int i = 0; i < pairs.Count; i++)
            {
                RankStair(nodes, pairs[i].Key, pairs[i].Value, startComp, start,
                    dist, verified, ref best);
                RankStair(nodes, pairs[i].Value, pairs[i].Key, startComp, start,
                    dist, verified, ref best);
            }
            return best;
        }

        private static void RankStair(List<Node> nodes, int near, int far,
            int startComp, IntVec3 start, float[] dist, Dictionary<int, float> verified,
            ref FirstHop best)
        {
            if (nodes[near].Component != startComp || nodes[far].Component == startComp
                || float.IsInfinity(dist[far]))
                return;
            float walk = verified.TryGetValue(near, out float v)
                ? v : Octile(start, nodes[near].Cell);
            if (float.IsInfinity(walk))
                return;
            float total = walk + StairFlightCost + dist[far];
            if (total < best.Total)
                best = new FirstHop { Valid = true, Near = near, Far = far, Total = total };
        }

        private static FirstHop BestSkipFirstHop(List<Node> nodes, List<int> gates,
            int hub, int startComp, IntVec3 start, float[] dist,
            Dictionary<int, float> verified)
        {
            // Best and second-best exits. This avoids scanning all exits for every entry:
            // one O(n) pass replaces n*(n-1)/2 pair comparisons.
            int bestExit = -1;
            int secondExit = -1;
            float bestExitCost = float.PositiveInfinity;
            float secondExitCost = float.PositiveInfinity;
            for (int i = 0; i < gates.Count; i++)
            {
                int g = gates[i];
                if (float.IsInfinity(dist[g]))
                    continue;
                float value = SkipFlightCost * 0.5f + dist[g];
                if (value < bestExitCost)
                {
                    secondExitCost = bestExitCost;
                    secondExit = bestExit;
                    bestExitCost = value;
                    bestExit = g;
                }
                else if (value < secondExitCost)
                {
                    secondExitCost = value;
                    secondExit = g;
                }
            }

            FirstHop best = default(FirstHop);
            best.Total = float.PositiveInfinity;
            for (int i = 0; i < gates.Count; i++)
            {
                int entry = gates[i];
                if (nodes[entry].Component != startComp)
                    continue;

                int exit = bestExit != entry ? bestExit : secondExit;
                float exitCost = bestExit != entry ? bestExitCost : secondExitCost;
                if (exit < 0 || float.IsInfinity(exitCost))
                    continue;

                float walk = verified.TryGetValue(entry, out float v)
                    ? v : Octile(start, nodes[entry].Cell);
                if (float.IsInfinity(walk))
                    continue;

                // entry -> hub is half a skip; hub -> exit is included in exitCost.
                float total = walk + SkipFlightCost * 0.5f + exitCost;
                if (total < best.Total)
                    best = new FirstHop { Valid = true, Near = entry, Far = exit, Total = total };
            }
            return best;
        }

        private static void RunDijkstra(List<Node> nodes, int hub,
            List<Edge>[] special, List<int> stairIndices, List<int> skipIndices,
            HashSet<int> destComps, IntVec3 dest, out float[] dist, out int[] next)
        {
            int count = hub + 1;
            dist = new float[count];
            next = new int[count];
            for (int i = 0; i < count; i++)
            {
                dist[i] = float.PositiveInfinity;
                next[i] = -1;
            }

            var heap = new Heap();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!destComps.Contains(nodes[i].Component))
                    continue;
                float seed = Octile(nodes[i].Cell, dest);
                dist[i] = seed;
                heap.Push(seed, i);
            }

            bool[] settled = new bool[count];
            while (heap.Count > 0)
            {
                KeyValuePair<float, int> cur = heap.Pop();
                int u = cur.Value;
                if (settled[u] || cur.Key > dist[u] + 0.0001f)
                    continue;
                settled[u] = true;

                List<Edge> edges = special[u];
                for (int e = 0; e < edges.Count; e++)
                    Relax(u, edges[e].To, edges[e].Cost, dist, next, settled, heap);

                if (u == hub)
                    continue;

                Node nu = nodes[u];

                // Walking to stair anchors in the same component is always relevant.
                for (int i = 0; i < stairIndices.Count; i++)
                {
                    int v = stairIndices[i];
                    if (v == u || nodes[v].Component != nu.Component)
                        continue;
                    Relax(u, v, Octile(nu.Cell, nodes[v].Cell),
                        dist, next, settled, heap);
                }

                // Only a STAIR node needs explicit walking edges to skipdoors. For a
                // skipdoor node, another skipdoor is reached more cheaply through the hub;
                // omitting skip<->skip walk edges is what removes the n^2 gate term.
                if (!nu.Skipdoor)
                {
                    for (int i = 0; i < skipIndices.Count; i++)
                    {
                        int v = skipIndices[i];
                        if (nodes[v].Component != nu.Component)
                            continue;
                        Relax(u, v, Octile(nu.Cell, nodes[v].Cell),
                            dist, next, settled, heap);
                    }
                }
            }
        }

        // Dijkstra is run outward from destination seeds on an undirected graph. Relaxing
        // v from settled u means "from v, go next to u on the way to the destination".
        private static void Relax(int u, int v, float edge, float[] dist, int[] next,
            bool[] settled, Heap heap)
        {
            if (v < 0 || v >= dist.Length || settled[v] || float.IsInfinity(dist[u]))
                return;
            float cand = dist[u] + edge;
            if (cand + 0.0001f < dist[v])
            {
                dist[v] = cand;
                next[v] = u;
                heap.Push(cand, v);
            }
        }

        private static HashSet<int> DestinationComponents(Map map, IntVec3 dest,
            PathEndMode mode, Pawn pawn)
        {
            var result = new HashSet<int>();
            int direct = CompatReflection.ComponentOf(map, dest, pawn);
            if (direct >= 0)
            {
                result.Add(direct);
                return result;
            }

            if (mode != PathEndMode.Touch)
                return result;

            int band = CompatReflection.BandOf(map, dest);
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 c = dest + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map) || CompatReflection.BandOf(map, c) != band)
                    continue;
                int comp = CompatReflection.ComponentOf(map, c, pawn);
                if (comp >= 0)
                    result.Add(comp);
            }
            return result;
        }

        private static float Octile(IntVec3 a, IntVec3 b)
        {
            int dx = Math.Abs(a.x - b.x);
            int dz = Math.Abs(a.z - b.z);
            int diagonal = Math.Min(dx, dz);
            int straight = Math.Max(dx, dz) - diagonal;
            return diagonal * 1.41421356f + straight;
        }

        private static bool TryLocalPathCost(Pawn pawn, IntVec3 start,
            LocalTargetInfo dest, PathEndMode mode, out float cost)
        {
            cost = 0f;
            bool oldDisable = CompatState.DisableSkipdoors;
            CompatState.DisableSkipdoors = true;
            try
            {
                using (PawnPath path = pawn.Map.pathFinder.FindPathNow(start, dest, pawn, null, mode))
                {
                    if (path == null || !path.Found)
                        return false;
                    List<IntVec3> cells = path.NodesReversed;
                    if (cells == null || cells.Count < 2)
                        return true;
                    for (int i = 1; i < cells.Count; i++)
                    {
                        IntVec3 d = cells[i] - cells[i - 1];
                        cost += d.x != 0 && d.z != 0 ? 1.41421356f : 1f;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                CompatState.DisableSkipdoors = oldDisable;
            }
        }

        private static void TraceDecision(Pawn pawn, string message)
        {
            if (pawn != null && pawn.IsColonistPlayerControlled)
                Log.Message("[AASB Skipdoor Compat] " + pawn.LabelShort + ": " + message);
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

            // We are suppressing vanilla PatherArrived because arrival at the source
            // skipdoor is only an intermediate waypoint. PatherArrived normally begins
            // with StopDead(), though, and skipping that cleanup leaves nextCell,
            // nextCellCost and the old async path/request pointing at the entrance.
            // After moving the pawn across the map, StartPath can then inherit that stale
            // movement state: most trips self-heal, but some pawns remain Standing at the
            // exit. Notify_Teleported(false) is vanilla's supported way to reset exactly
            // this state without interrupting the current job; it calls
            // Pawn_PathFollower.Notify_Teleported_Int -> StopDead + ResetToCurrentPosition.
            pawn.Notify_Teleported(endCurrentJob: false, resetTweenedPos: true);

            if (pawn.IsColonistPlayerControlled)
                Log.Message("[AASB Skipdoor Compat] " + pawn.LabelShort
                    + ": explicit skipdoor transit " + transit.Source.Position
                    + " [band " + fromBand + "] -> " + landing + " [band " + toBand
                    + "], pather reset; resuming path to " + realDest.Cell + ".");

            pather.StartPath(realDest, realMode);

            if (pawn.IsColonistPlayerControlled)
                Log.Message("[AASB Skipdoor Compat] " + pawn.LabelShort
                    + ": post-transit resume issued; moving=" + pather.Moving
                    + ", destination=" + pather.Destination.Cell + ".");

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
