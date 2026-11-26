"use strict";

// PlayRoom's game views (design §8): the playtest panel and a prototype's verdict on the job page, the Games tab,
// the asset ledger of one game, and the feel checks under the tasks count of the Backlogs. Read-only: every
// action here either copies a command, opens a story (where RoomKit's Done and Approve work) or fills the
// Capture form; nothing is sent from this file. Loaded after board.js and before app.js, whose helpers (element,
// asText, asList, asNumber, renderBlock, openStory) it uses when called.

const PlayTexts = {
  playtest: "Playtest",
  round: "Round ",
  runs: " runs",
  crashes: " crashes",
  softlocks: " softlocks",
  metrics: "Metrics",
  policy: "Policy",
  metric: "Metric",
  baseline: "Baseline",
  previous: "Previous",
  value: "This round",
  delta: "Δ baseline",
  gates: "Gates",
  badRuns: "Crashes and softlocks",
  seed: "seed ",
  shots: "Screenshots",
  shotFailed: "Screenshot not available",
  noMetrics: "No metrics in this round.",
  verdict: "Prototype verdict",
  followUp: "Follow-up: ",
  followUpWaiting: "waits for your approval",
  followUpMissing: "not in the backlog yet",
  prototypeNotes: "Prototype notes",
  verdictNotes: "Verdict",
  gamesLoading: "Loading…",
  gamesFailed: "Games could not be loaded.",
  noGames: "No games yet. Make one with /game init <Name>.",
  engine: "Engine",
  adapter: "Adapter check",
  adapterOk: "passed",
  adapterFailed: "failed: ",
  adapterNone: "not run yet",
  noBaseline: "No baseline yet. Store one with /game playtest --project ",
  crashFree: "Crash-free runs",
  frameP95: "p95 frame time",
  completion: "Completion rate",
  budgets: "Budgets",
  targets: "Difficulty targets",
  backlog: "Backlog",
  assets: "Assets",
  run: "Run",
  copy: "Copy",
  copied: "Copied",
  copyFailed: "Select and copy",
  feelChecks: "Feel checks",
  feelCheckDone: "Mark it Done in the story when it feels right.",
  tune: "Needs work: tune it",
  feature: "Needs work: new feature",
  tuneDraft: "Tuning after feel check #",
  featureDraft: "Feature after feel check #",
  assetsTitle: "Assets: ",
  assetsLoading: "Loading…",
  assetsFailed: "The asset ledger could not be loaded.",
  noAssets: "No asset files under ",
  path: "File",
  size: "Size",
  licence: "Licence",
  source: "Source",
  author: "Author",
  missing: "Missing ledger lines",
  badLicence: "Licences not allowed",
  wrongSize: "Wrong sizes",
  unused: "Ledger lines without a file",
  allowed: "Allowed licences: ",
  fromDisk: "Listed from disk (no git).",
  allGood: "Every file has a ledger line with an allowed licence and its size.",
};

const GamesRoute = "#games";
const AssetsRoutePrefix = "#assets/";
const FeelCheckTitle = "Feel check:";
const ShotMaxRequests = 60;

// Bumped whenever the job page is left, so late shot answers are dropped.
let shotGeneration = 0;
let gamesRequest = 0;
let assetsRequest = 0;
let feelRequest = 0;
// The feel-check tasks of the last /api/board answer, for the story buttons.
let feelTasks = [];

// A number as short text: up to three decimals, no trailing zeros; "–" when it is not a number.
function shortNumber(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "–";
  }
  return String(Math.round(value * 1000) / 1000);
}

// A change as text with its sign.
function deltaText(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "–";
  }
  return (value > 0 ? "+" : "") + shortNumber(value);
}

function bytesText(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "–";
  }
  if (value < 1024) {
    return value + " B";
  }
  if (value < 1024 * 1024) {
    return shortNumber(value / 1024) + " KB";
  }
  return shortNumber(value / (1024 * 1024)) + " MB";
}

function tableOf(headers, rows) {
  const table = element("table", "play-table");
  const head = element("tr");
  for (const header of headers) {
    const cell = element("th", null, header);
    cell.scope = "col";
    head.appendChild(cell);
  }
  table.appendChild(element("thead")).appendChild(head);
  const body = element("tbody");
  for (const row of rows) {
    const tr = element("tr");
    for (const value of row) {
      tr.appendChild(value instanceof Node ? element("td").appendChild(value).parentNode : element("td", null, value));
    }
    body.appendChild(tr);
  }
  table.appendChild(body);
  return table;
}

function chipOf(ok, text) {
  return element("span", ok ? "chip chip-pass" : "chip chip-changes", text);
}

// A command with a Copy button; the button says Copied, or asks to select it when the clipboard is refused.
function commandLine(command) {
  const row = element("div", "play-command");
  const code = element("code", "play-command-text", command);
  const copy = element("button", "edit-button play-copy", PlayTexts.copy);
  copy.type = "button";
  copy.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(command);
      copy.textContent = PlayTexts.copied;
    } catch {
      const range = document.createRange();
      range.selectNodeContents(code);
      const selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
      copy.textContent = PlayTexts.copyFailed;
    }
  });
  row.append(code, copy);
  return row;
}

// --- Job page: playtest panels -------------------------------------------------------------------------------------

// Drops late shot answers; board.js calls it when the job page is left or rebuilt.
function leavePlaytest() {
  shotGeneration += 1;
}

// One panel per round, newest first: result, runs, crashes and softlocks, metrics with deltas against the
// baseline, gates, bad runs with their seeds, and the screenshot gallery.
function renderPlaytest(jobId, rounds) {
  leavePlaytest();
  const generation = shotGeneration;
  let requests = 0;
  const panels = [];
  const list = asList(rounds).filter((round) => round && typeof round === "object");
  list.sort((a, b) => asNumber(b.round) - asNumber(a.round));
  list.forEach((round, index) => {
    const number = asNumber(round.round);
    const panel = element("details", "play-panel");
    panel.open = index === 0;
    const summary = element("summary", "play-summary");
    summary.appendChild(element("span", "play-heading", PlayTexts.playtest + " · " + PlayTexts.round + number));
    const result = asText(round.result);
    if (result === "PASS" || result === "FAIL") {
      summary.appendChild(chipOf(result === "PASS", result));
    }
    const facts = [];
    if (typeof round.runs === "number") {
      facts.push(round.runs + PlayTexts.runs);
    }
    if (typeof round.crashes === "number") {
      facts.push(round.crashes + PlayTexts.crashes);
    }
    if (typeof round.softlocks === "number") {
      facts.push(round.softlocks + PlayTexts.softlocks);
    }
    if (facts.length > 0) {
      summary.appendChild(element("span", "play-facts", facts.join(" · ")));
    }
    panel.appendChild(summary);

    const metrics = asList(round.metrics);
    panel.appendChild(element("h3", "play-subtitle", PlayTexts.metrics));
    if (metrics.length === 0) {
      panel.appendChild(element("p", "empty", PlayTexts.noMetrics));
    } else {
      panel.appendChild(tableOf(
        [PlayTexts.policy, PlayTexts.metric, PlayTexts.baseline, PlayTexts.previous, PlayTexts.value, PlayTexts.delta],
        metrics.map((row) => [asText(row.policy), asText(row.metric), shortNumber(row.baseline),
          shortNumber(row.previous), shortNumber(row.value), deltaText(row.delta)])));
    }

    const gates = asList(round.gates);
    if (gates.length > 0) {
      panel.appendChild(element("h3", "play-subtitle", PlayTexts.gates));
      const gateList = element("ul", "play-gates");
      for (const gate of gates) {
        const item = element("li", "play-gate");
        item.append(chipOf(gate.ok === true, asText(gate.name)), element("span", "play-gate-detail", asText(gate.detail)));
        gateList.appendChild(item);
      }
      panel.appendChild(gateList);
    }

    const bad = asList(round.badRuns);
    if (bad.length > 0) {
      panel.appendChild(element("h3", "play-subtitle", PlayTexts.badRuns));
      const badList = element("ul", "play-bad-runs");
      for (const run of bad) {
        badList.appendChild(element("li", null,
          asText(run.outcome) + " · " + asText(run.policy) + " · " + PlayTexts.seed + asNumber(run.seed)));
      }
      panel.appendChild(badList);
    }

    const shots = asList(round.shots).filter((name) => typeof name === "string");
    if (shots.length > 0) {
      panel.appendChild(element("h3", "play-subtitle", PlayTexts.shots));
      const gallery = element("div", "play-gallery");
      for (const name of shots) {
        if (requests >= ShotMaxRequests) {
          break;
        }
        requests += 1;
        const figure = element("figure", "play-shot");
        const image = element("canvas", "play-shot-image");
        image.setAttribute("role", "img");
        image.setAttribute("aria-label", name);
        figure.append(image, element("figcaption", "play-shot-name", name));
        gallery.appendChild(figure);
        loadShot(image, figure, jobId, number, name, generation);
      }
      panel.appendChild(gallery);
    }
    panels.push(panel);
  });
  return panels;
}

// Shots go through authFetch (the token is a header, so a plain src would be refused). RoomKit's CSP allows only
// img-src 'self', which refuses blob: and data: URLs, so each shot is decoded and drawn on a canvas instead.
async function loadShot(canvas, figure, jobId, round, name, generation) {
  const query = new URLSearchParams({ id: jobId, round: String(round), name: name });
  try {
    const response = await authFetch("/api/shot?" + query.toString(), { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    const bitmap = await createImageBitmap(await response.blob());
    if (generation !== shotGeneration) {
      bitmap.close();
      return;
    }
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    canvas.getContext("2d").drawImage(bitmap, 0, 0);
    bitmap.close();
  } catch {
    if (generation === shotGeneration) {
      canvas.remove();
      figure.prepend(element("p", "error", PlayTexts.shotFailed));
    }
  }
}

// The record of the given name from the job answer, or null.
function jobRecord(data, name) {
  return asList(data && data.records).find((record) => record && record.name === name && record.readable === true) || null;
}

function recordDetails(record, title) {
  const details = element("details", "play-notes");
  details.appendChild(element("summary", null, title));
  for (const block of asList(record.blocks)) {
    details.appendChild(renderBlock(block));
  }
  return details;
}

// A prototype's KEEP, KILL or PIVOT with its follow-up item (a link to the story, where Approve works while it is
// [?]), the verdict's text and the prototype's notes. Null for a job without a verdict.
function renderVerdict(data) {
  const verdict = asText(data && data.verdict);
  if (!["KEEP", "KILL", "PIVOT"].includes(verdict)) {
    return null;
  }
  const section = element("section", "play-verdict");
  const heading = element("h2", "play-verdict-title", PlayTexts.verdict + " ");
  heading.appendChild(element("span", chipClass(verdict), verdict));
  section.appendChild(heading);
  const follow = data.followUp;
  const project = asText(data.job && data.job.project);
  if (follow && asText(follow.title)) {
    const line = element("p", "play-follow-up", PlayTexts.followUp);
    const number = asNumber(follow.number);
    if (number && project) {
      const link = element("a", null, "#" + number + " " + asText(follow.title));
      link.href = storyHash(project, number);
      line.appendChild(link);
      if (follow.mark === "?") {
        line.appendChild(document.createTextNode(" · " + PlayTexts.followUpWaiting));
      }
    } else {
      line.appendChild(document.createTextNode(asText(follow.title) + " · " + PlayTexts.followUpMissing));
    }
    section.appendChild(line);
  }
  const verdictRecord = jobRecord(data, "verdict.md");
  if (verdictRecord) {
    section.appendChild(recordDetails(verdictRecord, PlayTexts.verdictNotes));
  }
  const notes = jobRecord(data, "prototype.md");
  if (notes) {
    section.appendChild(recordDetails(notes, PlayTexts.prototypeNotes));
  }
  return section;
}

// --- Games tab -----------------------------------------------------------------------------------------------------

// The worst value of a metric over the baseline's policies, or null.
function baselineWorst(baseline, metric, pick) {
  const values = Object.values((baseline && baseline.policies) || {})
    .map((summary) => summary && summary[metric])
    .filter((value) => typeof value === "number" && Number.isFinite(value));
  return values.length === 0 ? null : pick(...values);
}

// Crash-free runs over every policy as "n / total", or "–".
function crashFreeText(baseline) {
  let runs = 0;
  let bad = 0;
  for (const summary of Object.values((baseline && baseline.policies) || {})) {
    if (summary && typeof summary.runs === "number") {
      runs += summary.runs;
      bad += (typeof summary.crashes === "number" ? summary.crashes : 0)
        + (typeof summary.softlocks === "number" ? summary.softlocks : 0);
    }
  }
  return runs === 0 ? "–" : (runs - bad) + " / " + runs;
}

function factRow(list, label, value) {
  list.append(element("dt", null, label), value instanceof Node ? element("dd").appendChild(value).parentNode
    : element("dd", null, value));
}

function gameCard(game) {
  const name = asText(game.name);
  const card = element("article", "play-game");
  card.appendChild(element("h2", "play-game-title", name));
  const facts = element("dl", "play-facts-list");
  factRow(facts, PlayTexts.engine, asText(game.engine) || "–");
  const check = game.check;
  let checkText = PlayTexts.adapterNone;
  if (check && check.ok === true) {
    checkText = PlayTexts.adapterOk;
  } else if (check) {
    checkText = PlayTexts.adapterFailed + asList(check.failed).map(asText).join(", ");
  }
  if (check && asText(check.at)) {
    checkText += " · " + asText(check.at);
  }
  factRow(facts, PlayTexts.adapter, check ? chipOf(check.ok === true, checkText) : checkText);
  const baseline = game.baseline;
  if (baseline) {
    factRow(facts, PlayTexts.crashFree, crashFreeText(baseline));
    const p95 = baselineWorst(baseline, "frameMsP95", Math.max);
    factRow(facts, PlayTexts.frameP95, p95 === null ? "–" : shortNumber(p95) + " ms");
    const completion = baselineWorst(baseline, "completionRate", Math.max);
    factRow(facts, PlayTexts.completion, completion === null ? "–" : Math.round(completion * 100) + " %");
  }
  card.appendChild(facts);
  if (!baseline) {
    card.appendChild(element("p", "empty", PlayTexts.noBaseline + name));
  }
  const budgets = asList(game.budgets);
  if (budgets.length > 0) {
    card.appendChild(element("h3", "play-subtitle", PlayTexts.budgets));
    const list = element("ul", "play-budgets");
    for (const budget of budgets) {
      const max = budget.max;
      const used = budget.used;
      const item = element("li", "play-budget");
      item.appendChild(element("span", "play-budget-name", asText(budget.metric)));
      const meter = element("meter", "play-budget-meter");
      meter.min = 0;
      meter.max = typeof max === "number" && max > 0 ? max : 1;
      meter.high = meter.max * 0.8;
      meter.optimum = 0;
      meter.value = typeof used === "number" ? Math.min(used, meter.max) : 0;
      item.appendChild(meter);
      item.appendChild(element("span", "play-budget-text", shortNumber(used) + " / " + shortNumber(max)));
      list.appendChild(item);
    }
    card.appendChild(list);
  }
  const targets = asList(game.targets);
  if (targets.length > 0) {
    card.appendChild(element("h3", "play-subtitle", PlayTexts.targets));
    const list = element("ul", "play-targets");
    for (const target of targets) {
      list.appendChild(element("li", null, asText(target.metric) + " (" + asText(target.policy) + "): "
        + shortNumber(target.min) + "–" + shortNumber(target.max)));
    }
    card.appendChild(list);
  }
  const run = asText(game.run);
  if (run) {
    card.appendChild(element("h3", "play-subtitle", PlayTexts.run));
    card.appendChild(commandLine(run));
  }
  const links = element("p", "play-links");
  const backlog = element("a", null, PlayTexts.backlog);
  backlog.href = "#backlogs";
  backlog.addEventListener("click", () => chooseBacklogProject(name));
  const assets = element("a", null, PlayTexts.assets);
  assets.href = AssetsRoutePrefix + encodeURIComponent(name);
  links.append(backlog, document.createTextNode(" · "), assets);
  const feel = asNumber(game.feelChecks);
  if (feel > 0) {
    links.appendChild(document.createTextNode(" · " + feel + " " + PlayTexts.feelChecks.toLowerCase()));
  }
  card.appendChild(links);
  return card;
}

async function showGames() {
  const main = document.getElementById("games");
  gamesRequest += 1;
  const request = gamesRequest;
  if (main.childElementCount === 0) {
    main.replaceChildren(element("p", "status", PlayTexts.gamesLoading));
  }
  let data;
  try {
    const response = await authFetch("/api/games", { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    data = await response.json();
  } catch {
    if (request === gamesRequest && !tokenViewShown()) {
      main.replaceChildren(element("p", "error", PlayTexts.gamesFailed));
    }
    return;
  }
  if (request !== gamesRequest) {
    return;
  }
  const games = asList(data).filter((game) => game && typeof game === "object" && asText(game.name));
  main.replaceChildren(...(games.length === 0 ? [element("p", "empty", PlayTexts.noGames)] : games.map(gameCard)));
}

// --- Assets view ---------------------------------------------------------------------------------------------------

// The game of an #assets/<G> hash, or null.
function parseAssetsHash(hash) {
  if (!hash.startsWith(AssetsRoutePrefix)) {
    return null;
  }
  try {
    const name = decodeURIComponent(hash.slice(AssetsRoutePrefix.length));
    return name === "" ? null : name;
  } catch {
    return null;
  }
}

function problemList(title, items) {
  const section = element("section", "play-problems");
  section.appendChild(element("h3", "play-subtitle", title + " (" + items.length + ")"));
  const list = element("ul");
  for (const item of items) {
    list.appendChild(element("li", null, typeof item === "string" ? item : asText(item && item.path)));
  }
  section.appendChild(list);
  return section;
}

async function showAssets(name) {
  const main = document.getElementById("assets");
  assetsRequest += 1;
  const request = assetsRequest;
  const title = element("h1", "play-page-title", PlayTexts.assetsTitle + name);
  title.tabIndex = -1;
  main.replaceChildren(backButton(), title, element("p", "status", PlayTexts.assetsLoading));
  let data;
  try {
    const response = await authFetch("/api/assets?project=" + encodeURIComponent(name), { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    data = await response.json();
  } catch {
    if (request === assetsRequest && !tokenViewShown()) {
      main.replaceChildren(backButton(), title, element("p", "error", PlayTexts.assetsFailed));
    }
    return;
  }
  if (request !== assetsRequest) {
    return;
  }
  const nodes = [backButton(), title];
  nodes.push(element("p", "play-note", PlayTexts.allowed + asList(data.licences).map(asText).join(", ")
    + (data.fromGit === false ? " · " + PlayTexts.fromDisk : "")));
  const missing = asList(data.missing);
  const badLicence = asList(data.badLicence);
  const wrongSize = asList(data.wrongSize);
  const unused = asList(data.unused);
  if (missing.length > 0) {
    nodes.push(problemList(PlayTexts.missing, missing));
  }
  if (badLicence.length > 0) {
    nodes.push(problemList(PlayTexts.badLicence, badLicence));
  }
  if (wrongSize.length > 0) {
    nodes.push(problemList(PlayTexts.wrongSize, wrongSize));
  }
  if (unused.length > 0) {
    nodes.push(problemList(PlayTexts.unused, unused));
  }
  const files = asList(data.files);
  if (files.length === 0) {
    nodes.push(element("p", "empty", PlayTexts.noAssets + asText(data.folder) + "/"));
  } else {
    if (missing.length + badLicence.length + wrongSize.length === 0) {
      nodes.push(element("p", "play-note", PlayTexts.allGood));
    }
    nodes.push(tableOf([PlayTexts.path, PlayTexts.size, PlayTexts.licence, PlayTexts.source, PlayTexts.author],
      files.map((file) => {
        const ledger = file.ledger;
        const licence = ledger ? chipOf(file.licenceOk === true, asText(ledger.licence)) : chipOf(false, "–");
        const size = file.sizeOk === false ? chipOf(false, bytesText(file.bytes)) : bytesText(file.bytes);
        return [asText(file.path), size, licence, ledger ? asText(ledger.source) : "", ledger ? asText(ledger.author) : ""];
      })));
  }
  main.replaceChildren(...nodes);
}

// --- Feel checks ---------------------------------------------------------------------------------------------------

function feelCheckItem(task) {
  const item = element("li", "play-feel");
  const link = element("a", "play-feel-title", projectLabel(asText(task.project)) + " #" + asNumber(task.number) + " "
    + asText(task.title));
  link.href = storyHash(asText(task.project), asNumber(task.number));
  item.appendChild(link);
  if (asText(task.what)) {
    item.appendChild(element("p", "play-feel-what", asText(task.what)));
  }
  if (asText(task.run)) {
    item.appendChild(commandLine(asText(task.run)));
  }
  return item;
}

// The feel checks of every game, at the top of the Backlogs under the tasks count; hidden when there are none.
async function showFeelChecks() {
  const section = document.getElementById("feel-checks");
  if (section === null) {
    return;
  }
  feelRequest += 1;
  const request = feelRequest;
  let data;
  try {
    const response = await authFetch("/api/board", { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    data = await response.json();
  } catch {
    return;
  }
  if (request !== feelRequest) {
    return;
  }
  feelTasks = asList(data && data.tasks).filter((task) => task && task.feelCheck === true && asNumber(task.number));
  if (feelTasks.length === 0) {
    section.hidden = true;
    section.replaceChildren();
    return;
  }
  const details = element("details", "play-feel-checks");
  const old = section.querySelector("details");
  details.open = old === null ? true : old.open;
  details.appendChild(element("summary", "play-feel-summary", PlayTexts.feelChecks + " (" + feelTasks.length + ")"));
  const list = element("ul", "play-feel-list");
  for (const task of feelTasks) {
    list.appendChild(feelCheckItem(task));
  }
  details.appendChild(list);
  section.replaceChildren(details);
  section.hidden = false;
}

// Opens the Capture form for the project with a draft in it; the user picks the words and saves.
function captureDraft(project, text) {
  storeCaptureProject(project);
  history.pushState(null, "", CaptureRoute);
  route();
  const box = captureText();
  if (box) {
    box.value = text;
    box.focus();
    updateCaptureSave();
  }
}

// On a feel check's story page: the run command and the two "needs work" buttons, which open Capture with a draft
// for a tuning or a feature story. Done is RoomKit's own button on the [h] story.
async function showFeelCheckActions(target, data) {
  const main = document.getElementById("story");
  const old = main.querySelector(".play-feel-actions");
  if (old !== null) {
    old.remove();
  }
  const title = asText(data && data.title);
  const number = asNumber(data && data.number);
  if (!title.startsWith(FeelCheckTitle) || number === null) {
    return;
  }
  const story = storyRequest;
  let entry = null;
  try {
    entry = await findStoryEntry(target.project, number);
  } catch {
    entry = null;
  }
  // Only an open [h] line is a feel check still to do; a done one gets no buttons.
  if (story !== storyRequest || currentView !== "story" || entry === null || entry.mark !== "h"
    || main.querySelector(".play-feel-actions") !== null) {
    return;
  }
  const project = asText(target.project);
  const name = title.slice(FeelCheckTitle.length).trim();
  const section = element("section", "play-feel-actions");
  const task = feelTasks.find((item) => item.project === project && asNumber(item.number) === number);
  if (task && asText(task.run)) {
    section.appendChild(commandLine(asText(task.run)));
  }
  section.appendChild(element("p", "play-note", PlayTexts.feelCheckDone));
  const buttons = element("div", "edit-row");
  const tune = element("button", "edit-button", PlayTexts.tune);
  tune.type = "button";
  tune.addEventListener("click", () => captureDraft(project, PlayTexts.tuneDraft + number + " (" + name + "): "));
  const feature = element("button", "edit-button", PlayTexts.feature);
  feature.type = "button";
  feature.addEventListener("click", () => captureDraft(project, PlayTexts.featureDraft + number + " (" + name + "): "));
  buttons.append(tune, feature);
  section.appendChild(buttons);
  const article = main.querySelector(".story");
  if (article !== null) {
    article.after(section);
  } else {
    main.appendChild(section);
  }
  if (!task) {
    // Opened straight from a link: fetch the tasks once so the run command can be shown.
    showFeelChecks().then(() => {
      const found = feelTasks.find((item) => item.project === project && asNumber(item.number) === number);
      if (found && asText(found.run) && section.isConnected && section.querySelector(".play-command") === null) {
        section.prepend(commandLine(asText(found.run)));
      }
    });
  }
}
