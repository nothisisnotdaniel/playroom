# 006 Show each game's CI status in the Health view

As the owner, I want the Health view to show whether each game's last CI run passed, so I notice a red build from the phone.

Category: feature

## What

Per game (and the factory), show CI passed, failed, timed out, skipped or none recorded, with the time and a link to the run on github.com only, as in ControlRoom #25 (statusCiText in status.js and the project status endpoint). Read it from the ci field job.py ci writes into a job's state.json.

## Why

A failed CI run in a game repo is otherwise only seen on GitHub.
