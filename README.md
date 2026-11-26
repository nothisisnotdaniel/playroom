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
- The RoomKit 0.1.0 package in the local feed at `~/Library/Application Support/RoomKit/feed` (see `nuget.config`):

  ```bash
  gh release download v0.1.0 --repo nothisisnotdaniel/roomkit --pattern RoomKit.0.1.0.nupkg --dir ~/Library/Application\ Support/RoomKit/feed
  ```

## Run it

```bash
dotnet run --project src/PlayRoom.Server -- --FactoryRoot /path/to/GameFactory
```

Then open http://127.0.0.1:8110/. The first visit asks for the access token. This prints it:

```bash
dotnet run --project src/PlayRoom.Server -- token
```

PlayRoom listens on 127.0.0.1 only. To use it from your phone, serve it on your tailnet. ControlRoom already has
port 443 of the Mac's tailnet name, so give PlayRoom port 8443:

```bash
tailscale serve --bg --https=8443 http://127.0.0.1:8110
```

```bash
dotnet run --project src/PlayRoom.Server -- --FactoryRoot /path/to/GameFactory --TailnetHost <mac>.<tailnet>.ts.net --TailnetPort 8443
```

Then open `https://<mac>.<tailnet>.ts.net:8443/`. Without ControlRoom on 443, serve it on 443 and leave out
`--TailnetPort`.

## Keep it running

`scripts/launch-agent.sh` installs the LaunchAgent `local.playroom.server` (and, with `--release`, the updater
`local.playroom.updater`). PlayRoom sits next to the factory root, not inside it, so pass the root, and the tailnet
name and port when it is served on the tailnet. Notifications on the iPhone need a push subject: a `mailto:` address
or `https://` page of yours (Apple refuses the default `mailto:playroom@localhost`, and the test notification then
fails with 403):

```bash
scripts/launch-agent.sh install --factory-root /path/to/GameFactory --tailnet-host <mac>.<tailnet>.ts.net --tailnet-port 8443 --push-subject mailto:<you>@<your-domain>
```

Run it again after you pull changes: it publishes the server again and restarts it. `scripts/launch-agent.ps1` sets
up the Windows tasks.

To get updates automatically, install from a release instead (the newest one, or name a tag after `--release`), with
the same options:

```bash
scripts/launch-agent.sh install --release --factory-root /path/to/GameFactory --tailnet-host <mac>.<tailnet>.ts.net --tailnet-port 8443 --push-subject mailto:<you>@<your-domain>
```

That also installs the updater `local.playroom.updater`. Then turn on "Install updates automatically" in the app and
pick the channel: stable (releases) or develop (a prerelease for every push to `develop`). The updater never swaps the
server while a factory job is running.

## Releases

`.github/workflows/release.yml` builds the macOS (Apple silicon) tarball. Every push to `develop` makes a
`-develop` prerelease; a pull request from `develop` into `main` titled `release vX.Y.Z` makes release vX.Y.Z once it
is merged. Builds are not signed yet: committing `signing-cert.sha1` and setting the `SIGNING_CERT_P12` and
`SIGNING_CERT_PASSWORD` secrets turns signing on (as in ControlRoom).

PlayRoom and ControlRoom can run side by side: they use different ports (8110 and 8100), folders and labels.

## Develop

```bash
dotnet build PlayRoom.slnx
```

```bash
dotnet test PlayRoom.slnx
```

The build must have 0 warnings. `CLAUDE.md` has the conventions.
