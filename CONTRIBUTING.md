# Contributing

Bug reports, compatibility reports, suggestions, and pull requests are welcome.

## Reporting a problem

Please include:

- RimWorld version.
- Stop the Bleeding! – Field Hemostasis version or commit.
- Mod list and load order when compatibility may be involved.
- Steps that reproduce the problem.
- The first relevant red error and its complete stack trace.

## Building

Install a current .NET SDK and run:

```powershell
dotnet build .\Source\FieldHemostasis.csproj --configuration Release
```

The compiled assembly is written to:

```text
Assemblies/FieldHemostasis.dll
```

## Pull requests

Keep changes focused and preserve the mod's main design goals:

- Hemostasis stops bleeding temporarily without tending the wound.
- No periodic map-wide scans.
- No unnecessary per-tick work.
- Existing saves should remain compatible where practical.
