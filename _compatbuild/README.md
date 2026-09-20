# AASB II - VPE Skipdoor Pathing Redux Compat

Compatibility patch for RimWorld 1.6.

It lets `VPE Skipdoor Pathing Redux` compete with the segmented stair/elevator routing of `As above, So below II` on banded maps.

For cross-island or cross-level travel, the patch builds a sparse hybrid graph from AASB's walkable components and stair/elevator links plus one virtual SkipHub for the VPE skipdoor network. Skipdoors never form a pairwise complete graph: n gates contribute O(n) hub links. Same-component walking to connectors is estimated cheaply, and only a candidate skipdoor entry that is about to win is verified with a real local pathfinding probe. If the best first special hop is an AASB stair/elevator, AASB handles it normally; if it is a skipdoor, the trip is segmented at that gate, teleported explicitly, and the original destination is resumed.


## v0.1.1
Fixes Harmony target resolution against AASB's overloaded CrossBand method and pins all compatibility hooks to exact signatures. Adds route-choice diagnostics for player-controlled colonists.


## v0.1.2
Keeps skipdoor route permits across normal 1.6 path retries, keys them by pawn + destination instead of exact start cell, and explicitly carries the bypass through PathFinder.PushRequest/FindPathNow so AASB cannot re-reject a route already chosen for Redux.


## v0.2.0
Cross-band skipdoor travel no longer relies on a discontinuous PawnPath surviving AASB. The compat patch now verifies that the chosen Redux jump really crosses AASB bands, segments the trip at the source skipdoor, performs the same-map teleport explicitly, and resumes the original destination without replacing the pawn's job. Same-band skipdoor use remains Redux's responsibility.


## v0.3.0
Replaces the cross-band Redux path probe with a sparse hybrid graph. AASB stair/elevator anchors and skipdoors are planned together, while all skipdoors connect through one virtual hub instead of n(n-1)/2 gate pairs. The planner can therefore choose mixed routes such as skipdoor -> stairs or stairs -> skipdoor without requiring Redux to find an otherwise-unreachable cross-level destination. Only the winning skipdoor entry is verified with real same-band pathfinding before execution.


## v0.3.1
Fixes intermittent pawns remaining Standing immediately after an explicit skipdoor transit. The compat patch now uses RimWorld's own Notify_Teleported(endCurrentJob: false) after moving the pawn, which resets stale Pawn_PathFollower state (old path request, nextCell and movement costs) without interrupting the current job, then resumes the original destination.


## v0.4.0
Cross-band skipdoor segments now play the native VPE skipdoor sequence instead of jumping instantly. On reaching the source gate the pawn holds for the same 16-tick transit window used by VEF, calls the Skipdoor override of DoTeleportEffects each tick (native entry/exit flecks, sound, exit effecter and VPE-selected landing cell), then performs a safe same-map teleport reset and resumes the original job. The compat intentionally does not call VEF DoorTeleporter.Teleport on same-map travel because that method clears reservations, uses ExitMap/Spawn and drops carried things.


## v0.4.1
Executes Redux same-band skipdoor edges as real skipdoor transits. Redux still owns path selection; the compat only detects a non-adjacent consecutive skipdoor node in Pawn_PathFollower, cancels vanilla's collisionless Position jump, runs the native 16-tick VPE effects/landing sequence, resets the pather safely, and resumes the original destination.
