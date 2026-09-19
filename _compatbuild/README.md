# AASB II - VPE Skipdoor Pathing Redux Compat

Compatibility patch for RimWorld 1.6.

It lets `VPE Skipdoor Pathing Redux` compete with the segmented stair/elevator routing of `As above, So below II` on banded maps.

For a route that AASB would otherwise segment, the patch probes the real Redux path. If it contains a usable skipdoor jump, it compares that path length with AASB's stair route. The shorter route wins; ties prefer the skipdoor. When Redux wins, the patch temporarily exempts that request from AASB's cross-band rejection and its start-band-only door/provider filter. If Redux cannot produce a valid route, AASB is left untouched and uses its stairs/elevators normally.


## v0.1.1
Fixes Harmony target resolution against AASB's overloaded CrossBand method and pins all compatibility hooks to exact signatures. Adds route-choice diagnostics for player-controlled colonists.


## v0.1.2
Keeps skipdoor route permits across normal 1.6 path retries, keys them by pawn + destination instead of exact start cell, and explicitly carries the bypass through PathFinder.PushRequest/FindPathNow so AASB cannot re-reject a route already chosen for Redux.


## v0.2.0
Cross-band skipdoor travel no longer relies on a discontinuous PawnPath surviving AASB. The compat patch now verifies that the chosen Redux jump really crosses AASB bands, segments the trip at the source skipdoor, performs the same-map teleport explicitly, and resumes the original destination without replacing the pawn's job. Same-band skipdoor use remains Redux's responsibility.
