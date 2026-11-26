# PlayRoom

PlayRoom is the control room for a GameFactory workspace. It runs a small server on your Mac. It serves one web app
that you open in a browser, or add to your iPhone's home screen. The app shows:

- **Jobs:** each job's stage, its round, the design check, the reviews and the flags. Each round shows its playtest:
  metrics against the baseline, the gates (crashes, softlocks, difficulty targets and budgets), bad seeds, and the
  screenshots. A prototype also shows its verdict and follow-up.
- **Backlogs:** every game's backlog and the factory's backlog, with the feel checks first, each with the command
  that runs the game.
- **Games:** one card per game: the engine, the last adapter check, crash-free runs, p95 frame time, completion rate,
  the budget bars, the difficulty targets and the command that runs it.
- **Assets:** each game's asset ledger (`ASSETS.md`). Unlisted files and licences that aren't allowed are marked.
- Plus RoomKit's capture, history, health and push notifications.

PlayRoom reads the factory and never changes its jobs. To approve a job or send it back, use `/game approve <id>`
and `/game rework <id> <note>` in Claude Code.

## Needs

- .NET 10 SDK.
- The RoomKit 0.3.0 package in the local feed at `~/Library/Application Support/RoomKit/feed` (see `nuget.config`).

## Run it

```bash
dotnet run --project src/PlayRoom.Server -- --FactoryRoot /path/to/GameFactory
```

Then open http://127.0.0.1:8110/. The first visit asks for the access token. This prints it:

```bash
dotnet run --project src/PlayRoom.Server -- token
```

PlayRoom listens on 127.0.0.1 only. To use it from your phone, serve it on your tailnet, the same way as ControlRoom,
and pass `--TailnetHost <mac>.<tailnet>.ts.net`.

## Keep it running

`scripts/launch-agent.sh` installs the LaunchAgents `local.playroom.server` and `local.playroom.updater`.
`scripts/launch-agent.ps1` sets up the Windows tasks. The updater looks for releases at `placeholder/playroom`, which
is a placeholder until PlayRoom has a release repository. Until then the update check reports nothing.

PlayRoom and ControlRoom can run side by side: they use different ports (8110 and 8100), folders and labels.

## Develop

```bash
dotnet build PlayRoom.slnx
```

```bash
dotnet test PlayRoom.slnx
```

The build must have 0 warnings. `CLAUDE.md` has the conventions.
