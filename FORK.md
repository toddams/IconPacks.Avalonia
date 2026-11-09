# Runa's IconPacks.Avalonia fork

This fork of [MahApps/IconPacks.Avalonia](https://github.com/MahApps/IconPacks.Avalonia) lives at
`toddams/IconPacks.Avalonia` on the branch `runa/used-icons-only`, and is Runa's submodule `libs/IconPacks.Avalonia`. It branches from upstream `44ee23a` (Merge pull
request #42), whose code is the same as `0da930b`, the build Runa used before as DLLs in `libs/`. Runa now references
`src/IconPacks.Avalonia/IconPacks.Avalonia.csproj` as a project reference (its 44 projects are in `Runa.sln`, under
`libs/IconPacks.Avalonia`) and imports `src/IconPacks.Avalonia.UsedIcons/IconPacks.Avalonia.UsedIcons.targets`.

The entries run oldest first. Anything marked *uncommitted* hasn't been committed to the fork yet.

## At a glance

| Date | Commit | Change |
|---|---|---|
| 2026-11-08 | `19cfc842` | An app holds only the icons it names |
| 2026-11-09 | `7f8902cc` | Builds as a project reference: `net10.0` only, patched SourceLink |

---

## An app holds only the icons it names

- **Commit:** `19cfc842` (2026-11-08), "Embed only the icons an app names, found by a build-time scan"
- **Used by:** every Runa project that names an icon: Runa, Runa.UI.Controls, Runa.UI.Controls.Graph, Runa.Tests and
  Runa.UI.Controls.Tests
- **Tests:** `UsedIconsTests` in Runa's `tests/Runa.Tests`

**Why.** A pack keeps all its icons as one JSON table of SVG paths. The first time any icon of a pack is shown,
`PackIconDataIndex` parses the whole table and keeps every path as a string for the life of the app. Runa shows 167
icons from 12 packs, but those packs' tables are 51 MB of JSON, about 100 MB of strings; BootstrapIcons alone is
31 MB for the 8 icons Runa uses. A heap walk of Runa's Native AOT build found 95 MB of them, and the tables made up
most of its 230 MB `Runa.exe` (96 MB without them).

**What.**

- A pack has no `Resources/Icons.json` unless built with `-p:IconPacksEmbedIconData=true` (`src/Directory.Build.targets`).
  `PackIconDataFactory` returns an empty table for such a pack instead of throwing.
- `IconPacks.Avalonia.UsedIcons.targets`, imported by a project, scans that project's C# and XAML when it builds and
  generates `IconPacksUsedIcons.g.cs`: a module initializer that passes each named icon's path data to the new
  `PackIconDataIndex.Add`. The data comes from the packs' `Icons.json` in this repo, read by the inline task
  `FindUsedIcons.cs`. The scan finds `PackIcon<Pack>Kind.<Name>` (in C#, and in XAML inside `{x:Static}`),
  `<iconPacks:PackIcon<Pack> Kind="<Name>">` and `{iconPacks:<Pack> <Name>}`. It warns (`ICONPACKS002`) on a
  `using static` or an alias of a kind enum, which it can't follow, and (`ICONPACKS001`) on a name with no path data.
- Each assembly carries the icons its own source names, so a library's icons are there before any of its code runs,
  in the app, in tests and in the Native AOT build alike.
- `PackIconDataIndex.TryGetPath` returns an added icon first. Only a miss loads the pack's own table, as upstream does,
  and for a pack built without its data that table is empty. A miss logs an Avalonia warning in the `IconPacks` area,
  once per icon.
- The scan runs only when a project's C# or XAML changed, and not in design-time builds. Measured on Runa
  (2026-11-08): about 1 s when its XAML changed (the scan itself 0.3 s), 3 ms when nothing changed.

**Limits.** An icon picked at run time from a name or a number (`Enum.Parse`, a cast) isn't embedded and renders
blank. A project that names icons needs `IconPacks.Avalonia.Core` among its references, which the generated code
calls; a project reference to `IconPacks.Avalonia` brings it.

## Builds as a project reference: `net10.0` only, patched SourceLink

- **Commit:** `7f8902cc` (2026-11-09), "Build net10.0 only with a patched SourceLink, for use as a project reference"

**Why.** Runa builds these projects itself instead of keeping prebuilt DLLs. Upstream multi-targets `net8.0` and
`net10.0`, which doubles a solution build, and its `Microsoft.SourceLink.GitHub` 10.0.202 brings
`Microsoft.Build.Tasks.Git` with GHSA-23fw-v26w-5fgq, an NU1902 warning in every pack's restore.

**What.** `src/Directory.Build.props` targets `net10.0` alone; `src/Directory.Packages.props` takes SourceLink
10.0.114, patched and in the band of the installed 10.0.1xx SDK. Core drops its `System.Text.Json` package, which
`net10.0` has built in (NU1510).
