"use strict";

// PlayRoom's values for the RoomKit scripts: a classic script, loaded first by index.html and by the service
// worker (importScripts), whose top-level consts the other scripts read as globals.

// Sign-in and picks (token.js, capture.js, history.js). Never change a key: stored tokens and picks would be lost.
const TokenKey = "playroom-token";
const CaptureProjectKey = "playroom-capture-project";
const HistoryProjectKey = "playroom-history-project";

// The app's entries of push.js' PushTexts.
const PushAppTexts = {
  homeScreen: "To get notifications on iPhone, add PlayRoom to the Home Screen and open it from there.",
  denied: "Notifications are blocked for PlayRoom. Allow them in Settings, then check again.",
};

// Service worker (sw.js). Caches only the app shell. Bump the version whenever a shell file changes; ShellCacheLedger
// checks it.
const CacheVersion = "v2";
const CacheName = "playroom-shell-" + CacheVersion;

const Shell = [
  "/",
  "/index.html",
  "/app.css",
  "/app.js",
  "/board.js",
  "/play.js",
  "/play.css",
  "/edit.js",
  "/counts.js",
  "/search.js",
  "/capture.js",
  "/history.js",
  "/doctor.js",
  "/status.js",
  "/releases.js",
  "/live.js",
  "/push.js",
  "/token.js",
  "/room.js",
  "/manifest.webmanifest",
  "/icons/apple-touch-icon.png",
  "/icons/icon-192.png",
  "/icons/icon-512.png",
];

// The app's entries of the service worker's PushTitles, its fallback title, notification tag and update body prefix.
const PushAppTitles = {
  updated: "PlayRoom updated",
};
const PushFallbackTitle = "PlayRoom";
const PushDefaultTag = "playroom";
const PushUpdatePrefix = "PlayRoom ";

// The factory's own project: the jobs and backlog that change GameFactory itself, not a game. Its name on disk and
// in the API stays "factory"; the page shows it under a clear label and after the games.
const FactoryName = "factory";
const FactoryLabel = "GameFactory (the factory itself)";

// The name to show for a project; the name itself is still what links, filters and stored picks use.
function projectLabel(name) {
  return name === FactoryName ? FactoryLabel : name;
}

// The names in the same order, with the factory moved after every game.
function gamesFirst(names) {
  return [...names.filter((name) => name !== FactoryName), ...names.filter((name) => name === FactoryName)];
}
