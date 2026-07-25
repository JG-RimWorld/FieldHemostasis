# Stop the Bleeding! – Field Hemostasis

Stop the Bleeding! – Field Hemostasis is a RimWorld 1.6 mod that adds an
emergency right-click order for temporarily stopping a pawn's bleeding without
tending their wounds or using medicine.

## Current behavior

- Select a player-controlled humanlike pawn and right-click a bleeding pawn.
- Choose **Apply field hemostasis to ...**.
- The actor walks to the patient and stabilizes bleeding wounds one at a time,
  starting with the wound that currently has the highest bleed rate.
- Each wound has a base application time of 300 game ticks, scaled by the
  actor's Medical Tend Speed.
- A progress bar is displayed above the patient for each wound.
- Stabilized wounds have a bleed rate of zero but remain untended.
- Normal tending still works. Infection, pain, healing, and all other wound
  behavior remain vanilla.
- Hemostasis expires after the configured duration and bleeding resumes if the
  wound has not been tended by then.
- Self-application is supported when the actor is able to act and manipulate.
- Vanilla injuries and bleeding missing-part hediffs are supported.

## Settings

The mod settings contain:

- **Mean hemostasis duration**: 1–72 in-game hours; default 12.
- **Use normally distributed duration**: disabled by default.
- **Standard deviation**: 5–75% of the mean; default 25%.

When random duration is enabled, the duration of each stabilized wound is
sampled independently with a Box-Muller transform. The source distribution is
normal and centered on the configured mean. Samples are restricted to positive
durations and to no more than four standard deviations above the mean.

The normal sample is generated only once when each wound is stabilized. It has
no measurable ongoing computational cost.

Medical Tend Speed is read once at the start of each wound. Progress display
uses the stored work duration and does not recalculate the stat every tick.

## Development setup

The project uses portable reference packages, so it does not need paths to your
local RimWorld DLLs.

1. Install the current .NET SDK. .NET 8 or later is suitable.
2. In VS Code, install Microsoft's **C# Dev Kit** extension.
3. Subscribe to and enable the RimWorld **Harmony** mod:
   <https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077>
4. Open the `FieldHemostasis` folder in VS Code.
5. Build with `Ctrl+Shift+B`, or run:

   ```powershell
   dotnet build .\Source\FieldHemostasis.csproj --configuration Release
   ```

The build restores the RimWorld 1.6 and Harmony reference packages and writes:

```text
Assemblies/FieldHemostasis.dll
```

Do not copy `0Harmony.dll` into this mod. Harmony is a runtime dependency and
should be loaded from its own mod.

## Contributing

Bug reports, compatibility reports, suggestions, and pull requests are welcome
on [GitHub](https://github.com/JG-RimWorld/FieldHemostasis). See
[CONTRIBUTING.md](CONTRIBUTING.md) for the information that is most useful in a
report.

## License

Stop the Bleeding! – Field Hemostasis is available under the
[MIT License](LICENSE).

The project was designed by JG and developed with assistance from
OpenAI's ChatGPT/Codex.

## Installing the local build

Put the complete `FieldHemostasis` directory inside RimWorld's local `Mods`
directory. A typical Steam installation is:

```text
C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\FieldHemostasis
```

Enable Harmony and Field Hemostasis in the mod manager, with Harmony earlier in
the load order.

## Implementation notes

The hemostasis state is stored per bleeding hediff in a `GameComponent` and is
included in saves. An in-memory dictionary provides constant-time lookups.

Harmony postfixes modify the final bleed rate for `Hediff_Injury` and
`Hediff_MissingPart`. No vanilla wound is marked as tended and no vanilla
method is skipped or replaced.

There is no component tick and no global pawn scan. Expiration is checked by a
constant-time dictionary lookup when RimWorld requests the bleed rate it needs
for its own health processing. Detached records are also removed when a hediff
is removed and before saving.
