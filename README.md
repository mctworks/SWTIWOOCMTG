# Steven Wright Thinks It's Wrong Only One Company Makes This Game (A.K.A SWTIWOOCMTG, or "Wright Thinks It's Wrong")

*Steven Wright Thinks It's Wrong Only One Company Makes This Game* (repo name: `SWTIWOOCMTG`) or **Wright Thinks It's Wrong** is a from-scratch reimagining of the classic property-trading board game, built as a native desktop app in C# using Blazor and Photino.

> **Status: early development.** This is not a finished game. Core mechanics are playable, but the UI is still placeholder/debug-quality, several rules are unfinished, and things will break. Expect frequent, large changes.

## What this is

The name is the point. The game is over 100 years old, and it was originally stolen from its creator, Elizabeth Magie, who designed it as "The Landlord's Game" to teach the dangers of land monopolies and the virtues of Georgist economics. It was meant as a critique of unchecked capitalism. That messaging was stripped out, repackaged, and sold to Parker Bros, who turned it into the celebration of wealth-hoarding we know today.

This project puts that good ol' firey anti-capitalist juice back in. It keeps the core loop (roll dice, move around a board, buy and upgrade properties, pay rent, avoid going broke) but reworks the specifics: renamed spaces (expect references to all the music I listen to while coding, including Electric Six and Ween), a reworked prison system with an appeal mechanic instead of just a flat "get out of jail" (those are still here too!), a Risk/Tactics card system instead of Chance/Community Chest, an HP mechanic, togglable House Rules, and a supply-limited property upgrade system (based on the real, official, if often-ignored, Parker Bros rule that houses/hotels are a limited shared resource, not infinite).

None of the card text, space names, or specific rule tweaks are meant to be taken too seriously. Some of it's a joke. Some of it's a middle finger. That's intentional. I do like to go hard.

## Tech stack

- **C# / .NET** — core game logic (`Game.cs`) is plain C# with no framework dependencies, so it's portable if the front end ever changes.
- **Blazor** — the UI layer. Game state renders as HTML/CSS, driven by component state instead of a traditional game loop.
- **Photino** — wraps the Blazor UI in a lightweight native desktop window (cross-platform, including Linux).

The game started life as a plain console app I was building while finishing my Foundational C# certification in early June; the current version is a full rewrite of the interface layer on top of the same underlying rules engine, with a proper windowed UI, SVG board/token/dice art, and no more `Console.ReadLine()` anywhere.

## Contributing / Feedback

This is early and rough. If you poke at it and find something broken (very likely) or have opinions on what "unofficial rules" should make the cut, open an issue.

## License

Attribution required — do whatever you want with this (use it, modify it, build on it, redistribute it), just credit the original project. Formally licensed under [MIT](https://opensource.org/licenses/MIT), which covers exactly that: free use with no restrictions beyond keeping the original copyright/credit notice intact.

(For what it's worth: the underlying game concept this project riffs on is old enough that it probably *should* be public domain by now. This project's own code is MIT either way.)

## Roadmap / To-Do

1. Debug game logic
2. Finalize game rules and settings
3. Implement Azure SignalR Service for online play, plus in-game analytics (local play only for now)
4. Implement game session saves (local and online)
5. Overhaul the current debug-quality UI into something far more responsive and polished, including non-placeholder art (tokens, card/deed graphics, more TBD)
6. Implement CPU AI players, "trained" on analytics gathered from closed beta testing (No LLMs here, still traditionally coded routines)