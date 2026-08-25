# Romance On The Rim - Wedding Fix

Compatibility patch for Romance On The Rim on RimWorld 1.6.

Current fixes:

- Reconstructs `LordJob_WeddingCeremony` when loading a save made during an active wedding.
- Repairs procession pairings that RotR loses because `Pair<Pawn,Pawn>` is serialized with an incompatible Scribe mode.
- Synchronizes RotR's frozen vanilla ritual clock with real elapsed wedding ticks, so the `Attending wedding (... remaining)` report updates.
- Computes the final participant/guest quality from RimWorld's own serialized `LordToilData_Gathering.presentForTicks`, because RotR's wedding outcome worker leaves `RitualOutcomeComp_ParticipantCount` attendance data empty.

The patch is deliberately restricted to `RomanceOnTheRim.LordJob_WeddingCeremony` and does not alter other rituals.
