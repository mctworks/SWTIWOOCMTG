# Steven Wright Thinks It's Wrong Only One Company Makes This Game (A.K.A SWTIWOOCMTG, or "Wright Thinks It's Wrong")

*Steven Wright Thinks It's Wrong Only One Company Makes This Game* (repo name: `SWTIWOOCMTG`) or **Wright Thinks It's Wrong** is a from-scratch reimagining of the classic property-trading board game, built in C# with a shared Blazor UI, currently running as a native desktop app via Photino.

> **Status: early development.** This is not a finished game. Core mechanics — including a functioning CPU opponent — are playable, but the UI is still placeholder/debug-quality, several rules are still being balanced, and things will break. Expect frequent, large changes.

## What this is

The base game is public-domain-adjacent at this point and everyone knows the loop: roll dice, move around a board, buy and upgrade properties, pay rent, avoid going broke. This project keeps that loop and reworks the rest — renamed spaces/cards (expect references to all the music I listen to while coding, including Electric Six and Ween), an HP mechanic, togglable House Rules, and a supply-limited property upgrade system (based on the real, official, often-ignored rule that houses/hotels are a limited shared resource, not infinite).

One House Rule worth calling out: the **Arfenhouse Rule**. Named after an old Flash animation series where, in the final episode, a Monopoly game features a similar gag, this is a specifically targeted, worse ruleset aimed at one unlucky player. Toggle it on, nominate who's playing under it (the game votes if more than one nomination comes in), and that player eats extra, harsher card effects for the rest of the game on top of the standard ones. In online play, the nominated player will need to explicitly confirm they accept before it locks in — that flow isn't built yet, since online play itself isn't built yet, but the data model already supports it.

None of the card text, space names, or specific rule tweaks are meant to be taken too seriously. Some of it's a joke. Some of it's a middle finger. That's intentional. I do like to go hard.

## Tech stack

**Current:**
- **C# / .NET** — `GameCore` holds all game logic and rules with zero framework dependencies, so it's portable regardless of what sits in front of it.
- **Blazor (Razor Class Library)** — the UI layer, shared across whatever hosts it. Game state renders as HTML/CSS, driven by component state.
- **Photino** — wraps the Blazor UI in a lightweight native desktop window (cross-platform, including Linux). Current primary way to run the game.
- **CPU opponents** — a hand-coded `IPlayerAI` interface with rules-based heuristics (property/monopoly evaluation, debt triage via selling upgrades and mortgaging, jail decisions). No LLMs, no trained models — traditional coded logic, same as the rest of the game.

**Planned:**
- **ASP.NET Core (Kestrel) host** — a server project referencing `GameCore` directly, runs anywhere Kestrel runs (a free-tier host for now, a VPS or personal box later).
- **Self-hosted SignalR Hub** — real-time push for dice rolls, moves, rent, card draws, debt/bankruptcy resolution, and turn changes.
- **Web API** — session lobby (create/join/list), save/load, and an events-ingestion endpoint for analytics. No live connection required to test it.
- **Blazor web client** — served from the same host, reusing the existing Razor Class Library and talking to the SignalR hub. This is how the game reaches phones/other devices — no native mobile app planned.
- **RabbitMQ analytics pipeline** — self-hosted or free-tier, decoupled from live gameplay, for tracking closed-beta stats separately from the real-time game loop.

## Contributing / Feedback

This is early and rough. If you poke at it and find something broken (very likely) or have opinions on what "unofficial rules" should make the cut, open an issue.

## License

Attribution required — do whatever you want with this (use it, modify it, build on it, redistribute it), just credit the original project. Formally licensed under [MIT](https://opensource.org/licenses/MIT), which covers exactly that: free use with no restrictions beyond keeping the original copyright/credit notice intact.

(For what it's worth: the underlying game concept this project riffs on is old enough that it probably *should* be public domain by now. This project's own code is MIT either way.)

## Roadmap / To-Do

1. Debug game logic & finalize rules/settings — ongoing, at a good place
2. Split UI into a shared Razor Class Library — complete
3. Add an ASP.NET Core (Kestrel) host project referencing `GameCore` directly, to become the server — no managed cloud service, runs anywhere Kestrel runs
4. Self-hosted SignalR Hub on that host for real-time push actions (rolls, moves, rent, cards, debt/bankruptcy, turn changes)
5. Web API on the same host for session lobby, save/load, and analytics ingestion
6. Blazor web client served from the same host, reusing the existing UI library and talking to the SignalR hub — this is how the game reaches other devices, no native mobile app
7. RabbitMQ analytics pipeline, decoupled from live gameplay
8. CPU AI — hand-coded heuristics behind `IPlayerAI` (buy/upgrade/debt/jail decision hooks). Currently functional at a placeholder level; ongoing refinement of the heuristics themselves (property valuation, monopoly pursuit, debt triage) as testing surfaces gaps
9. UI overhaul / non-placeholder art — ongoing