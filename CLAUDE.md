# PlayRoom — conventions

Every agent reads this file before working on PlayRoom. Keep it short and accurate.

- **What it is:** the control room for GameFactory. A small ASP.NET Core server on the user's Mac reads a
  GameFactory root (the folder that holds `factory/` and the game folders) and serves one web app (browser and
  home-screen PWA): jobs with their playtest results and screenshots, the backlogs with feel checks, one card per
  game, and each game's asset ledger. It is built on **RoomKit 0.1.0** (from `nothisisnotdaniel/roomkit`, restored from the local folder feed `~/Library/Application Support/RoomKit/feed`) the same way ControlRoom is: RoomKit gives the
  host, token gate, `Host` check, CSP, push, update check, backlogs, stories, search, capture, history and doctor;
  PlayRoom adds GameFactory's job format (`GameJobFormat`, `IJobFormat`) and its own read-only endpoints.
- **Shared code comes from RoomKit only.** Never copy RoomKit's (or ControlRoom's) source into this repo. A change
  that belongs to every room goes to RoomKit; PlayRoom takes a new package version.
- **Identity, which must never clash with ControlRoom:** port `8110`, data folder
  `~/Library/Application Support/PlayRoom/` (token `access-token`), LaunchAgent labels `local.playroom.server` and
  `local.playroom.updater`, release repository `nothisisnotdaniel/playroom` (public: the installer and updater read it without a token),
  release archives `playroom-*`, Windows tasks prefixed `PlayRoom`. These values sit in `PlayRoomApp.Options` and
  in the app block at the top of each script in `scripts/`; change them together.
- **Factory root:** always given with `--FactoryRoot <path>` (there is no parent-folder default: PlayRoom does not
  live inside the factory root). Jobs are `<root>/factory/jobs/<id>/state.json` with ids matching
  `\A[0-9]{3,6}-[a-z0-9-]{1,80}\.[0-9a-f]{32}\z`; games are top-level folders with `CLAUDE.md` and `backlog.md`.
  The meaning of every field and record is in GameFactory's `docs/design.md`.
- **Read-only toward the factory:** PlayRoom never writes `state.json`, job records, playtest files or assets.
  Approve and rework stay with `/game approve <id>` and `/game rework <id> <note>`. The only writes are RoomKit's
  own backlog edits and capture drafts. Git runs only through RoomKit or with fixed argument lists
  (`ArgumentList`), never through a shell, and never with input from a request except validated names.
- **Endpoints (all `GET`, all behind the token):** `/api/status`, `/api/releases`, `/api/board`, `/api/changes`,
  `/api/job?id=`, `/api/shot?id=&round=&name=` (a PNG from `round-<n>/shots/`, names checked, no symlinks),
  `/api/games` and `/api/assets?project=`.
- **Web app:** `src/PlayRoom.Server/wwwroot/` (`app.js`, `board.js`, `play.js`, `room.js` and the rest). Shots are
  fetched with the token and drawn on a `<canvas>`, because RoomKit's CSP allows only `img-src 'self'`. Any change
  to a shell file (the files `room.js` lists) needs a new `CacheVersion` in `room.js` and a new line
  `<version> <sha256>` in `tests/PlayRoom.Server.Tests/shell-cache.ledger`; the failing test prints the hash.
- **Icons** are drawn by code: `python3 tools/make-icons.py` writes `wwwroot/icons/*.png`. Don't hand-edit them.
- **Commands:** `dotnet build PlayRoom.slnx` (0 warnings: warnings are errors), `dotnet test PlayRoom.slnx`, and
  `dotnet run --project src/PlayRoom.Server -- --FactoryRoot <root>` to run it (http://127.0.0.1:8110/).
  `dotnet run --project src/PlayRoom.Server -- token` prints the access token.
- **Tests** build fake factory roots in temp folders (`TempFactoryRoot`, `GameSamples`), with fake job folders,
  records and PNG shots, and never read a real factory root or the user's token.
- **Git:** work lands on `develop`; `main` holds releases only. The remote is `nothisisnotdaniel/playroom`.
- **Releases:** `.github/workflows/release.yml` (macOS osx-arm64 only). A push to `develop` makes a prerelease
  `vX.Y.(Z+1)-develop.<run>`; a merged pull request from `develop` into `main` titled `release vX.Y.Z` makes a release.
  The workflow downloads the RoomKit version pinned in `PlayRoom.Server.csproj` from RoomKit's GitHub release (checked
  against its `SHA256SUMS` and attestation) into the folder feed. A new RoomKit means: bump the version in the csproj,
  put the `.nupkg` in the local feed (`gh release download v<version> --repo nothisisnotdaniel/roomkit`), and take
  over the package's `scripts/` keeping each app block. Builds are unsigned until `signing-cert.sha1` is committed.
