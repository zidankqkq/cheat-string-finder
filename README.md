# Cheat String Finder

Compare a clean Minecraft with an injected one (same profile, both on the title screen) to pull out the cheat client's own strings for a Client Parser list.

**This is one tool of a screenshare forensics set.** Each tool is its own repository; download only the ones you need and keep them in a folder together.

## Download

Get `CheatStringFinder.exe` from the [latest release](../../releases/latest). Nothing installs. It only reads; it never changes, deletes or uploads anything. Run it as administrator for a complete scan.

The exe is built on GitHub's servers from this source and carries a signed build-provenance attestation, so you can verify your download matches the code:

```
gh attestation verify CheatStringFinder.exe --repo <your-user>/cheat-string-finder
```

It is not code-signed, so Windows SmartScreen shows a warning on first run (More info -> Run anyway).

## Build it yourself

Needs the .NET 9 SDK on Windows.

```
dotnet test tests/HistoryParser.Core.Tests -c Release
./publish.ps1 -Version 1.0.0
```

## Releasing

Push a tag like `v1.0.0`; GitHub Actions builds CheatStringFinder.exe, writes its SHA-256, signs the provenance and creates the release.

## Layout

- `src/CheatStringFinder.App` - the program.
- `src/HistoryParser.Core`, `src/RatChecker.Core` - the shared forensics libraries.
- `tests/HistoryParser.Core.Tests` - the test suite.
