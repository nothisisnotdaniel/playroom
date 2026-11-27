# PlayRoom backlog

`/factory next --project PlayRoom` runs the first open item, checks it off and records the job id.
Each item is a user story in its own file under `backlog/`, numbered `NNN-slug.md`; the line links it as `#N`.
The order of the lines is the priority. Add items with `python3 factory/tools/job.py add --project PlayRoom '<json>'`
(or by hand: write the story file first, then the line), and keep a number once it is given.
Each story has one `Category: <name>` line after its story sentence, one of: feature, bug, security, ux, ui, tidy-up, test, ci, docs.
`- [?]` items are follow-ups the factory proposed: change `[?]` to `[ ]` to approve one, or delete the line and its file.
`- [h]` items are tasks for you. `(after: #3)` waits until item 3 is done; `(after: Times#5)` names another project's item.
- [h] [#1](backlog/001-try-playroom-on-the-phone-against.md) Try PlayRoom on the phone against the sandbox jobs
