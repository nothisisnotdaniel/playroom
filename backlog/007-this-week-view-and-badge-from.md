# 007 This week view and badge from the weekly plan (after: factory#102)

As the factory owner, I want a This week view on the phone, so that I see what the weekly plan still waits on me for wherever I am.

Category: feature

## What

Add a This week view that shows the resolved weekly plan from `job.py week --json` (factory #102): goals with their status, then the open tasks with their live state, those owned by you first, then the release lines. Keep it compact for the phone; done tasks fold away. Count the open tasks owned by you as part of the app badge, alongside the blocked jobs of #3.

## Why

The weekly plan becomes a structured file in factory #102. On the phone the question is what is still waiting on me this week, and the badge can answer it without opening the app.

## Notes

Decided on 28 Nov 2026. Read the data from the factory, do not parse the markdown view. Works with #3 (blocked jobs badge).
