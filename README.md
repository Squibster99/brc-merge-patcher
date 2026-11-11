# BRC Merge Patcher

For Divinity: Original Sin Enhanced Edition (tested on Steam 2.0.119.430).

Some BRC mods add items to vendors. The game can't load vendor stock from a mod's `.pak`, so those additions have to go into a loose `TreasureTable.txt`. This small program adds them to the file you already have, without removing anything another mod put there.

## What this version does
- Adds the BRC vendor additions listed in `additions.txt` to an existing loose `TreasureTable.txt`. Today that's the Alpha Pyramids manual, sold by Cylia, Nadia and the Teller of Secrets.
- Running it again changes nothing. `remove` takes the additions out again.
- It backs up your file every time it changes it (in a `Backups` folder next to the program).

It doesn't merge two other mods' files together. If you later install a mod that replaces `TreasureTable.txt`, run the patcher again afterwards.

## Do I need it?
- **You have another mod that changes vendor stock**, so you have a file at `Data\Public\Shared\Stats\Generated\TreasureTable.txt` in your game folder: use this patcher.
- **You don't have that file** (most players): you don't need the patcher. Use the mod's ready-made TreasureTable download instead. The patcher tells you this if you run it.

## How to use
1. Close the game.
2. Extract this archive anywhere and double-click `BrcMergePatcher.exe`. It finds Steam installs, including other Steam library folders.
3. Read the message, then press Enter to close.

From a command prompt: `BrcMergePatcher.exe check`, `patch` or `remove`. If the game isn't found, add `--game "<your game folder>"`.

## Notes
- Windows may warn about an unknown program, because it isn't signed. The source code is in this repository, and you can build it yourself with the .NET Framework compiler that comes with Windows (`csc.exe`).
- Future versions may widen what it can merge, one step at a time, as players need it.

## Licence
MIT, see `LICENSE`. This covers the program and its text only. The game's own files are Larian Studios'.
