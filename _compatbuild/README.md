# AASB II - VPE Skipdoor Pathing Redux Compat

Compatibility patch for RimWorld 1.6.

It lets `VPE Skipdoor Pathing Redux` compete with the segmented stair/elevator routing of `As above, So below II` on banded maps.

For a route that AASB would otherwise segment, the patch probes the real Redux path. If it contains a usable skipdoor jump, it compares that path length with AASB's stair route. The shorter route wins; ties prefer the skipdoor. When Redux wins, the patch temporarily exempts that request from AASB's cross-band rejection and its start-band-only door/provider filter. If Redux cannot produce a valid route, AASB is left untouched and uses its stairs/elevators normally.


## v0.1.1
Fixes Harmony target resolution against AASB's overloaded CrossBand method and pins all compatibility hooks to exact signatures. Adds route-choice diagnostics for player-controlled colonists.
