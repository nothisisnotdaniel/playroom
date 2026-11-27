# 003 Show the number of blocked jobs as the app badge on the iPhone

As the owner, I want PlayRoom's home-screen icon to show how many GameFactory jobs wait for me, so I see it without opening the app.

Category: feature

## What

Set the app badge (navigator.setAppBadge, and from the service worker on a push) to the number of needs-human jobs, and clear it at zero, as in ControlRoom #181 (appBadgeCount in app.js, RoomKit push.js and sw.js).

## Why

PlayRoom already sends push notifications, but the count is gone once a notification is dismissed.
