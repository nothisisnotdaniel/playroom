# PlayRoom backlog

`/factory next --project PlayRoom` runs the first open item, checks it off and records the job id.
Each item is a user story in its own file under `backlog/`, numbered `NNN-slug.md`; the line links it as `#N`.
The order of the lines is the priority. Add items with `python3 factory/tools/job.py add --project PlayRoom '<json>'`
(or by hand: write the story file first, then the line), and keep a number once it is given.
Each story has one `Category: <name>` line after its story sentence, one of: feature, bug, security, ux, ui, tidy-up, test, ci, docs.
`- [?]` items are follow-ups the factory proposed: change `[?]` to `[ ]` to approve one, or delete the line and its file.
`- [h]` items are tasks for you. `(after: #3)` waits until item 3 is done; `(after: Times#5)` names another project's item.
- [h] [#1](backlog/001-try-playroom-on-the-phone-against.md) Try PlayRoom on the phone against the sandbox jobs
- [ ] [#2](backlog/002-approve-or-rework-a-needs-human.md) Approve or rework a needs-human job from the phone
- [ ] [#3](backlog/003-show-the-number-of-blocked-jobs.md) Show the number of blocked jobs as the app badge on the iPhone
- [ ] [#4](backlog/004-move-history-and-health-behind-a.md) Move History and Health behind a More menu in the tab bar
- [ ] [#5](backlog/005-show-when-the-updater-last-checked.md) Show when the updater last checked for updates
- [ ] [#6](backlog/006-show-each-game-s-ci-status.md) Show each game's CI status in the Health view
- [ ] [#7](backlog/007-this-week-view-and-badge-from.md) This week view and badge from the weekly plan (after: factory#102)
