# 002 Approve or rework a needs-human job from the phone

As the owner, I want to approve a needs-human job or send it back with a note from PlayRoom, so an escalated job doesn't wait until I'm at the Mac.

Category: feature

## What

On the page of a needs-human job, a "Your decision" section with Approve (with a confirm) and a Rework note (one line, at most 1,000 characters), as in ControlRoom #22 (act.js, JobActEndpoint.cs, JobAction.cs). The server only writes or deletes the job's request file under the job's lock; PlayRoom itself never changes state.json. The job card shows a chip while a request waits, and the request can be cancelled. The hint says nothing happens until the next /game session for that game runs.

## Why

Today an escalated GameFactory job can only be decided with /game approve or /game rework in Claude Code, so a job blocked in the evening waits until the next desk session.

## Notes

Needs a matching change in GameFactory: the /game skill (and job.py) must pick up the request file, the way /factory does for ControlRoom. Add that item to GameFactory's factory backlog first. The README line "PlayRoom reads the factory and never changes its jobs" changes with this.
