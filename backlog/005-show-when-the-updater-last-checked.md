# 005 Show when the updater last checked for updates

As the owner, I want PlayRoom's update settings to say when the updater last checked, so I can tell that auto update is running.

Category: ui

## What

Next to the update interval setting, show "Last checked <local time>" or "Not checked yet", from the lastCheck field RoomKit's update endpoint already returns, as in ControlRoom #163 (renderUpdateInterval in board.js).

## Why

When auto update seemed off on 26 Nov, the only way to see what the updater did was to read updater.log on the Mac.
