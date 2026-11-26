"use strict";

// Jobs board and job page for GameFactory jobs: stages from spike to approve, the design check, six reviewers and a
// playtest result per round. The playtest panel, verdict and follow-up come from play.js. Loaded before app.js, which owns routing and the shared helpers (element, asText,
// asList, asNumber, renderBlock, storyHash); those are only called after both scripts have run.
// Everything is built with createElement and textContent; fixed texts only, never server or exception text.

const BoardTexts = {
  live: "Live · ",
  unreachable: "Can't reach the server.",
  noJobs: "No jobs yet.",
  loading: "Loading…",
  jobFailed: "Can't load this job.",
  recordUnreadable: "Can't read this record.",
  noRecords: "No records yet.",
  needsYou: "Needs you",
  stuck: "Stuck",
  allProjects: "All projects",
  back: "Back",
  records: "Records",
  version: "PlayRoom ",
  updateAvailable: "Update available: PlayRoom ",
  updateThis: " (this is ",
  updateEnd: ").",
  copy: "Copy",
  copied: "Copied.",
  releaseNotes: "Release notes",
  autoUpdate: "Install updates automatically",
  autoUpdateUnreadable: "Can't read the update setting.",
  autoUpdateFailed: "Can't change the update setting. Try again.",
  updateChannel: "Update channel",
  channelStable: "Stable",
  channelDevelop: "Develop (prereleases)",
  channelFailed: "Can't change the update channel. Try again.",
  updateInterval: "Check for updates",
  interval30: "Every 30 minutes",
  interval60: "Every hour",
  interval120: "Every 2 hours",
  interval360: "Every 6 hours",
  interval720: "Every 12 hours",
  intervalFailed: "Can't change the update interval. Try again.",
  jobState: "Job state",
  jobRound: " · round ",
  designCheck: "design check",
  playtest: "playtest",
  flags: "Flags",
  started: "Started ",
  ended: " · ended ",
  endedOnly: "Ended ",
  took: " · took ",
  running: " · running ",
  underMinute: "< 1 min",
  minutes: " min",
  hours: " h",
  agents: "Agents",
  tok: " tok",
};

// The stages of a running job: its time runs from started to now, and a leftover ended is ignored.
const RunningStages = ["spike", "prototype", "design", "build", "playtest", "review", "approve"];

const AutoUpdateRoute = "/api/update/settings";

// Only a link to this repo's release page is shown.
const UpdateReleasePrefix = "https://github.com/placeholder/playroom/releases/tag/";

const Stages = [
  { key: "spike", label: "Spike" },
  { key: "prototype", label: "Prototype" },
  { key: "design", label: "Design" },
  { key: "build", label: "Build" },
  { key: "playtest", label: "Playtest" },
  { key: "review", label: "Review" },
  { key: "approve", label: "Approve" },
  { key: "done", label: "Merged", collapsed: true },
];

const Reviewers = ["security", "gameplay", "performance", "code", "ux", "art"];

// The flags a job can carry, with their fixed texts; any other value is not shown.
const FlagTexts = {
  "feel-check": "feel check",
  "assets-changed": "assets changed",
  "perf-over-budget": "over budget",
};

const BoardRoute = "#jobs";
const JobRoute = "#job/";
const JobIdPattern = /^[0-9]{1,6}-[a-z0-9-]{1,80}(\.[0-9a-f]{32})?$/;
const ProjectKey = "playroom.project";
const PollInterval = 5000;
// Reviews start with "VERDICT: PASS" or "VERDICT: CHANGES", playtest reports with "PLAYTEST: PASS" or
// "PLAYTEST: FAIL", a prototype's verdict with "VERDICT: KEEP", "KILL" or "PIVOT" (an optional note after it), and
// decisions with a bare APPROVE or ESCALATE.
const Verdict = /^(?:VERDICT: (PASS|CHANGES|KEEP|KILL|PIVOT)(?:\s.*)?|PLAYTEST: (PASS|FAIL)|(APPROVE|ESCALATE))$/;

let boardText = null;
let boardJobs = [];
let boardScroll = 0;
let boardPolling = false;
let mergedOpen = false;
let selectedProject = readSavedProject();

// Set when a card on the board opened the job page, so Back can go back in history.
let openedFromBoard = false;
let openJobId = null;
let openJobEntry = null;
let openJobRecord = null;
let jobRequest = 0;

function readSavedProject() {
  try {
    return localStorage.getItem(ProjectKey) || "";
  } catch {
    return "";
  }
}

function saveProject(name) {
  try {
    localStorage.setItem(ProjectKey, name);
  } catch {
    // Private mode or storage off: the filter still works for this page.
  }
}

function jobHash(id) {
  return JobRoute + encodeURIComponent(id);
}

// Returns null when the hash is not a job route, "" when it is one with a bad id.
function parseJobHash(hash) {
  if (!hash.startsWith(JobRoute)) {
    return null;
  }
  let id;
  try {
    id = decodeURIComponent(hash.slice(JobRoute.length));
  } catch {
    return "";
  }
  return JobIdPattern.test(id) ? id : "";
}

function formatTokens(count) {
  return count >= 1e6 ? (count / 1e6).toFixed(1) + "M" : Math.round(count / 1e3) + "k";
}

// Whole minutes, rounded down: "< 1 min", "4 min", "2 h", "1 h 12 min".
function formatDuration(seconds) {
  const minutes = Math.floor(Math.max(0, seconds) / 60);
  if (minutes < 1) {
    return BoardTexts.underMinute;
  }
  if (minutes < 60) {
    return minutes + BoardTexts.minutes;
  }
  const rest = minutes % 60;
  return Math.floor(minutes / 60) + BoardTexts.hours + (rest > 0 ? " " + rest + BoardTexts.minutes : "");
}

// A UTC record time in the browser's local time zone (no zone option given); timeOnly leaves out the date.
function formatTime(date, timeOnly) {
  const time = { hour: "2-digit", minute: "2-digit" };
  return timeOnly
    ? date.toLocaleTimeString([], time)
    : date.toLocaleString([], { month: "short", day: "numeric", ...time });
}

// started or ended as a Date, or null when missing or not a time.
function recordTime(value) {
  const text = asText(value);
  if (!text) {
    return null;
  }
  const date = new Date(text);
  return Number.isNaN(date.getTime()) ? null : date;
}

function sameLocalDay(a, b) {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

// The times line of a card and the job page: "Started <start> · running <d>" for a running job, "Started <start>
// · ended <end> · took <d>" for a finished one; a missing part is left out, never shown as 0. Null when the job
// has neither time. The running part is a span with data-running-since, kept current by updateRunningTimes.
function jobTimes(job, className) {
  const started = recordTime(job.started);
  const ended = recordTime(job.ended);
  const running = RunningStages.includes(job.stage);
  if (started === null && (ended === null || running)) {
    return null;
  }
  const line = element("span", className);
  if (started !== null) {
    line.appendChild(element("span", null, BoardTexts.started + formatTime(started, false)));
  }
  if (running) {
    const since = element("span", null, runningText(started, Date.now()));
    since.dataset.runningSince = String(started.getTime());
    line.appendChild(since);
    return line;
  }
  if (ended !== null) {
    const endText = started !== null && sameLocalDay(started, ended)
      ? formatTime(ended, true)
      : formatTime(ended, false);
    line.appendChild(element("span", null, (started !== null ? BoardTexts.ended : BoardTexts.endedOnly) + endText));
    if (started !== null && ended.getTime() >= started.getTime()) {
      line.appendChild(element("span", null, BoardTexts.took + formatDuration((ended.getTime() - started.getTime()) / 1000)));
    }
  }
  return line;
}

function runningText(started, now) {
  return BoardTexts.running + formatDuration((now - started.getTime()) / 1000);
}

// Called by live.js after each check while the board or a job page is shown: every running time is recomputed
// in place, with no refetch.
function updateRunningTimes() {
  const now = Date.now();
  for (const span of document.querySelectorAll("[data-running-since]")) {
    const since = Number(span.dataset.runningSince);
    if (Number.isFinite(since)) {
      span.textContent = runningText(new Date(since), now);
    }
  }
}

// The job page's Agents list: name, tokens when known and time when seconds is known (never 0 for unknown).
function renderAgents(agents) {
  const rows = agents.filter((agent) => agent && typeof agent === "object" && asText(agent.name));
  if (rows.length === 0) {
    return null;
  }
  const section = element("section", "job-agents");
  section.setAttribute("aria-label", BoardTexts.agents);
  section.appendChild(element("h2", null, BoardTexts.agents));
  const list = element("ul", "agent-list");
  for (const agent of rows) {
    const parts = [asText(agent.name)];
    const tokens = asNumber(agent.tokens);
    if (tokens !== null) {
      parts.push(formatTokens(tokens) + BoardTexts.tok);
    }
    const seconds = asNumber(agent.seconds);
    if (seconds !== null) {
      parts.push(formatDuration(seconds));
    }
    list.appendChild(element("li", "agent", parts.join(" · ")));
  }
  section.appendChild(list);
  return section;
}

// Chip colours come from a fixed map, never from the value itself.
function chipClass(value) {
  if (value === "PASS" || value === "APPROVE" || value === "KEEP") {
    return "chip chip-pass";
  }
  if (value === "CHANGES" || value === "ESCALATE" || value === "FAIL" || value === "KILL") {
    return "chip chip-changes";
  }
  if (value === "pending" || value === "PIVOT") {
    return "chip chip-pending";
  }
  return "chip";
}

function asMap(value) {
  return value && typeof value === "object" && !Array.isArray(value) ? value : {};
}

// On the card a chip shows only its reviewer (the value is in title); on the job page it also shows the value.
function chipText(key, value, visible) {
  return visible ? key + " " + value : key;
}

// The playtest result of a round (PASS or FAIL) from the job's playtest list, or "".
function playtestResult(job, round) {
  const entry = asList(job.playtest).find((item) => item && asNumber(item.round) === round);
  const result = asText(entry && entry.result);
  return result === "PASS" || result === "FAIL" ? result : "";
}

function roundRow(label, reviews, past, visible, playtest) {
  const row = element("div", past ? "round past" : "round");
  row.appendChild(element("span", "round-label", label));
  if (playtest) {
    const chip = element("span", chipClass(playtest), chipText(BoardTexts.playtest, playtest, visible));
    chip.title = BoardTexts.playtest + ": " + playtest;
    row.appendChild(chip);
  }
  const map = asMap(reviews);
  for (const key of Reviewers) {
    const value = asText(map[key]) || "pending";
    if (value === "skipped") {
      continue;
    }
    const chip = element("span", chipClass(value), chipText(key, value, visible));
    chip.title = key + ": " + value;
    row.appendChild(chip);
  }
  return row;
}

function modeText(job) {
  let text = asText(job.mode);
  const path = asText(job.path);
  if (path && path !== "feature") {
    text += " · " + path;
  }
  if (job.risk === "high") {
    text += " · high risk";
  }
  return text;
}

function rightText(job) {
  const round = asNumber(job.round);
  if (asText(job.decision)) {
    return asText(job.decision);
  }
  if (job.stage === "build" && round !== null && round > 1) {
    return "rework " + (round - 1);
  }
  return round ? "round " + round : "";
}

// One row per round: the design check, each past round, then the current round once its playtest has a result
// or reviews have started. Each round row starts with its playtest result. Shared by the board card and the job
// page; visible puts each chip's value in its text.
function reviewRounds(job, visible) {
  const rounds = element("div", "rounds");
  const designReview = asText(job.designReview);
  if (designReview) {
    const row = element("div", job.stage === "design" ? "round" : "round past");
    row.appendChild(element("span", "round-label", job.designRevised === true ? "Design·2" : "Design"));
    const chip = element("span", chipClass(designReview), chipText(BoardTexts.designCheck, designReview, visible));
    chip.title = BoardTexts.designCheck + ": " + designReview;
    row.appendChild(chip);
    rounds.appendChild(row);
  }
  asList(job.pastRounds).forEach((reviews, index) => {
    rounds.appendChild(roundRow("R" + (index + 1), reviews, true, visible, playtestResult(job, index + 1)));
  });
  const round = asNumber(job.round);
  const reviewing = !["spike", "prototype", "design", "build", "playtest"].includes(job.stage);
  const result = round ? playtestResult(job, round) : "";
  if (round && (reviewing || result)) {
    rounds.appendChild(roundRow("R" + round, reviewing ? job.reviews : {}, false, visible, result));
  }
  return rounds;
}

// The fixed texts of the job's known flags, in the order of FlagTexts.
function flagTexts(job) {
  const flags = asList(job.flags);
  return Object.keys(FlagTexts).filter((flag) => flags.includes(flag)).map((flag) => FlagTexts[flag]);
}

function renderCard(job) {
  const id = asText(job.id);
  const card = element("a", job.stage === "needs-human" ? "card card-human" : "card");
  card.href = jobHash(id);
  card.addEventListener("click", () => {
    openedFromBoard = true;
  });

  const head = element("div", "card-head");
  const name = element("span", "card-id");
  if (asText(job.project)) {
    name.appendChild(element("span", "card-project", projectLabel(asText(job.project))));
    name.appendChild(element("span", null, " · "));
  }
  name.appendChild(element("span", null, id));
  head.appendChild(name);
  if (asText(job.mode)) {
    head.appendChild(element("span", "card-mode", modeText(job)));
  }
  card.appendChild(head);
  card.appendChild(element("span", "card-title", asText(job.title) || id));
  if (job.stage === "needs-human") {
    card.appendChild(element("span", "chip chip-human card-request", BoardTexts.needsYou));
  }
  const flags = flagTexts(job);
  if (flags.length > 0) {
    card.appendChild(element("span", "card-flags", flags.join(" · ")));
  }

  const rounds = reviewRounds(job, false);
  if (rounds.childElementCount > 0) {
    card.appendChild(rounds);
  }

  if (asText(job.note)) {
    card.appendChild(element("span", "card-note", asText(job.note)));
  }

  const times = jobTimes(job, "card-times");
  if (times) {
    card.appendChild(times);
  }

  const foot = element("div", "card-foot");
  foot.appendChild(element("span", "card-branch", asText(job.branch)));
  const facts = [];
  const tokens = asNumber(job.tokens);
  if (tokens !== null) {
    facts.push(formatTokens(tokens) + " tok");
  }
  const right = rightText(job);
  if (right) {
    facts.push(right);
  }
  foot.appendChild(element("span", "card-facts", facts.join(" · ")));
  card.appendChild(foot);
  return card;
}

function sectionHeading(label, count) {
  return label + " (" + count + ")";
}

// Jobs waiting for the user (needs-human); no other tab shows them. The user's own [h] tasks are in the backlog, counted on the Backlogs tab.
function renderStuck(jobs) {
  const section = element("section", "stuck");
  section.appendChild(element("h2", null, sectionHeading(BoardTexts.stuck, jobs.length)));
  const cards = element("div", "cards");
  for (const job of jobs) {
    cards.appendChild(renderCard(job));
  }
  section.appendChild(cards);
  return section;
}

function renderStage(stage, jobs) {
  const heading = sectionHeading(stage.label, jobs.length);
  const cards = element("div", "cards");
  for (const job of jobs) {
    cards.appendChild(renderCard(job));
  }
  if (stage.collapsed) {
    const details = element("details", "stage");
    details.open = mergedOpen;
    details.addEventListener("toggle", () => {
      mergedOpen = details.open;
    });
    details.appendChild(element("summary", null, heading));
    details.appendChild(cards);
    return details;
  }
  const section = element("section", "stage");
  section.appendChild(element("h2", null, heading));
  section.appendChild(cards);
  return section;
}

function projectNames() {
  const names = new Set();
  for (const job of boardJobs) {
    if (asText(job.project)) {
      names.add(job.project);
    }
  }
  return gamesFirst([...names].sort());
}

function renderFilter(names) {
  const filter = document.getElementById("board-filter");
  filter.hidden = names.length < 2;
  const buttons = [];
  for (const name of ["", ...names]) {
    const button = element("button", "filter-button", name ? projectLabel(name) : BoardTexts.allProjects);
    button.type = "button";
    button.setAttribute("aria-pressed", name === selectedProject ? "true" : "false");
    button.addEventListener("click", () => {
      selectedProject = name;
      saveProject(name);
      renderBoard();
    });
    buttons.push(button);
  }
  filter.replaceChildren(...buttons);
}

function renderBoard() {
  const names = projectNames();
  if (selectedProject && !names.includes(selectedProject)) {
    selectedProject = "";
  }
  renderFilter(names);
  // Game jobs first, then the factory's own; otherwise in answer order (the sort is stable).
  const shown = (selectedProject ? boardJobs.filter((job) => job.project === selectedProject) : [...boardJobs])
    .sort((a, b) => Number(a.project === FactoryName) - Number(b.project === FactoryName));
  const stuck = shown.filter((job) => job.stage === "needs-human");
  const nodes = stuck.length > 0 ? [renderStuck(stuck)] : [];
  if (boardJobs.length === 0) {
    nodes.push(element("p", "empty", BoardTexts.noJobs));
  } else {
    const stages = element("div", "stages");
    for (const stage of Stages) {
      stages.appendChild(renderStage(stage, shown.filter((job) => job.stage === stage.key)));
    }
    nodes.push(stages);
  }
  const content = document.getElementById("board-content");
  const scroll = window.scrollY;
  content.replaceChildren(...nodes);
  if (!document.getElementById("board").hidden) {
    window.scrollTo(0, scroll);
  }
}

function setBoardStatus(ok) {
  const status = document.getElementById("board-status");
  if (ok) {
    status.className = "live-status";
    status.textContent = BoardTexts.live + new Date().toLocaleTimeString();
  } else {
    status.className = "live-status error";
    status.textContent = connectionLost() ? LiveTexts.lost : BoardTexts.unreachable;
  }
}

function boardViewsVisible() {
  return !document.getElementById("board").hidden || !document.getElementById("job").hidden;
}

// The board answer as text; throws on a network error or an error status. The one /api/board request, used by
// pollBoard and by the job counts on the Jobs tab (counts.js).
async function fetchBoard() {
  const response = await authFetch("/api/board", { cache: "no-store" });
  if (!response.ok) {
    throw new Error("status");
  }
  return response.text();
}

// Fetches the board (driven by live.js when jobs or a backlog changed, and when the board or a job page is shown).
// Returns true when the board was fetched, false when it was skipped or failed.
async function pollBoard() {
  if (document.visibilityState !== "visible" || tokenViewShown() || !boardViewsVisible() || boardPolling) {
    return false;
  }
  boardPolling = true;
  try {
    let text;
    let data;
    try {
      text = await fetchBoard();
      data = JSON.parse(text);
    } catch {
      setJobCounts(null);
      setBoardStatus(false);
      return false;
    }
    // The counts on the Jobs tab follow every answer read, over all projects (counts.js).
    setJobCounts(countJobs(data && data.jobs));
    setBoardStatus(true);
    loadVersion();
    loadUpdate();
    if (text === boardText) {
      return true;
    }
    boardText = text;
    boardJobs = asList(data && data.jobs).filter((job) => job && typeof job === "object");
    renderBoard();
    refreshOpenJob();
    return true;
  } finally {
    boardPolling = false;
  }
}

// Reloads the open job page when its entry on the board has changed.
function refreshOpenJob() {
  if (openJobId === null || document.getElementById("job").hidden) {
    return;
  }
  const entry = boardJobs.find((job) => job.id === openJobId);
  if (entry && openJobEntry !== null && JSON.stringify(entry) !== openJobEntry) {
    loadJob(openJobId, true);
  }
}

function showBoard() {
  openedFromBoard = false;
  leaveJob();
  window.scrollTo(0, boardScroll);
  pollBoard();
  showPushSection();
}

// The version line at the foot of the board: asked for once per page load, after the first board load that
// worked (again only after a refused token), through authFetch; set with textContent, hidden when it fails.
let versionRequested = false;

async function loadVersion() {
  if (versionRequested || !hasToken()) {
    return;
  }
  versionRequested = true;
  const line = document.getElementById("app-version");
  try {
    const response = await authFetch("/api/version", { cache: "no-store" });
    if (!response.ok) {
      return;
    }
    const data = await response.json();
    const version = asText(data && data.version);
    if (version !== "") {
      line.textContent = BoardTexts.version + version;
      line.hidden = false;
    }
  } catch {
    // Unreachable: the line stays hidden. Refused: the token view is shown, and saving a token asks again.
    versionRequested = !tokenViewShown();
  }
}

// The update banner under the board heading: asked for once per page load like the version line; filled with
// textContent only, and left hidden when no update is available or the request fails.
let updateRequested = false;

async function loadUpdate() {
  if (updateRequested || !hasToken()) {
    return;
  }
  updateRequested = true;
  const section = document.getElementById("update");
  try {
    const response = await authFetch("/api/update", { cache: "no-store" });
    if (!response.ok) {
      return;
    }
    const data = await response.json();
    renderAutoUpdate(asText(data && data.current), data && typeof data.autoUpdate === "boolean" ? data.autoUpdate : null);
    renderUpdateChannel(asText(data && data.current),
      data && (data.channel === "stable" || data.channel === "develop") ? data.channel : null);
    renderUpdateInterval(asText(data && data.current),
      data && (data.channel === "stable" || data.channel === "develop") ? data.channel : null,
      data && UpdateIntervals.includes(data.updateInterval) ? data.updateInterval : null);
    const latest = asText(data && data.latest);
    const command = asText(data && data.command);
    if (!data || data.available !== true || latest === "" || command === "") {
      section.hidden = true;
      return;
    }
    renderUpdate(section, latest, asText(data.current), command, asText(data.url));
  } catch {
    // Unreachable: the banner stays hidden. Refused: the token view is shown, and saving a token asks again.
    updateRequested = !tokenViewShown();
  }
}

function renderUpdate(section, latest, current, command, url) {
  const text = element("p", "update-text",
    BoardTexts.updateAvailable + latest + BoardTexts.updateThis + current + BoardTexts.updateEnd);
  if (command.startsWith(".\\launch-agent.ps1 ")) {
    text.append(" Run this command in PowerShell from the extracted release folder.");
  }
  const code = element("code", "update-command", command);
  const copy = element("button", "edit-button", BoardTexts.copy);
  copy.type = "button";
  const note = element("span", "update-note");
  note.hidden = true;
  copy.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(command);
      note.textContent = BoardTexts.copied;
      note.hidden = false;
    } catch {
      // No clipboard access: the command can still be selected by hand.
    }
  });
  const actions = element("div", "update-actions");
  actions.append(copy, note);
  const parts = [text, code, actions];
  if (url.startsWith(UpdateReleasePrefix)) {
    const link = element("a", "update-link", BoardTexts.releaseNotes);
    link.href = url;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    actions.append(link);
  }
  section.replaceChildren(...parts);
  section.hidden = false;
}

// The "Install updates automatically" switch under the version line, only for a release (never on dev). It shows
// the stored setting; null (a refused settings file) shows a fixed text and no switch.
function renderAutoUpdate(current, autoUpdate) {
  const box = document.getElementById("auto-update");
  if (current === "" || current === "dev") {
    box.replaceChildren();
    box.hidden = true;
    return;
  }
  if (autoUpdate === null) {
    box.replaceChildren(element("p", "auto-update-text", BoardTexts.autoUpdateUnreadable));
    box.hidden = false;
    return;
  }
  const label = element("label", "push-switch");
  const input = document.createElement("input");
  input.type = "checkbox";
  input.setAttribute("role", "switch");
  input.checked = autoUpdate;
  const note = element("p", "auto-update-note");
  note.setAttribute("role", "status");
  note.setAttribute("aria-live", "polite");
  note.hidden = true;
  input.addEventListener("change", () => setAutoUpdate(input, note));
  label.append(input, element("span", null, BoardTexts.autoUpdate));
  box.replaceChildren(label, note);
  box.hidden = false;
}

// Sends the tapped value; on any error the switch goes back to its old state and a fixed text is shown.
async function setAutoUpdate(input, note) {
  const wanted = input.checked;
  input.disabled = true;
  note.hidden = true;
  try {
    const response = await authFetch(AutoUpdateRoute, {
      method: "POST",
      cache: "no-store",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ autoUpdate: wanted }),
    });
    if (!response.ok) {
      throw new Error("status");
    }
    const data = await response.json();
    if (!data || data.autoUpdate !== wanted) {
      throw new Error("data");
    }
  } catch {
    input.checked = !wanted;
    note.textContent = BoardTexts.autoUpdateFailed;
    note.hidden = false;
  } finally {
    input.disabled = false;
  }
}

// The update channel choice under the switch, only for a release with readable settings: it adds nothing on dev
// or when the channel is null (a refused settings file, where the switch box already shows the fixed text).
function renderUpdateChannel(current, channel) {
  const box = document.getElementById("auto-update");
  if (current === "" || current === "dev" || channel === null || box.hidden) {
    return;
  }
  const label = element("label", "update-channel");
  const select = document.createElement("select");
  select.className = "update-channel-select";
  for (const [value, text] of [["stable", BoardTexts.channelStable], ["develop", BoardTexts.channelDevelop]]) {
    const option = element("option", null, text);
    option.value = value;
    select.append(option);
  }
  select.value = channel;
  select.dataset.saved = channel;
  const note = element("p", "auto-update-note");
  note.setAttribute("role", "status");
  note.setAttribute("aria-live", "polite");
  note.hidden = true;
  select.addEventListener("change", () => setUpdateChannel(select, note));
  label.append(element("span", null, BoardTexts.updateChannel), select);
  box.append(label, note);
}

// Sends the chosen channel; on any error the choice goes back to the stored one and a fixed text is shown.
async function setUpdateChannel(select, note) {
  const wanted = select.value;
  const saved = select.dataset.saved;
  select.disabled = true;
  note.hidden = true;
  try {
    const response = await authFetch(AutoUpdateRoute, {
      method: "POST",
      cache: "no-store",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ channel: wanted }),
    });
    if (!response.ok) {
      throw new Error("status");
    }
    const data = await response.json();
    if (!data || data.channel !== wanted) {
      throw new Error("data");
    }
    select.dataset.saved = wanted;
  } catch {
    select.value = saved;
    note.textContent = BoardTexts.channelFailed;
    note.hidden = false;
  } finally {
    select.disabled = false;
  }
}

// The update interval choices in minutes, in the order the select shows them.
const UpdateIntervals = [30, 60, 120, 360, 720];

// The update interval choice after the channel select, in the same places only: it adds nothing on dev, when the
// channel is null (a refused settings file) or when the interval is not one of the choices.
function renderUpdateInterval(current, channel, interval) {
  const box = document.getElementById("auto-update");
  if (current === "" || current === "dev" || channel === null || interval === null || box.hidden) {
    return;
  }
  const label = element("label", "update-channel update-interval");
  const select = document.createElement("select");
  select.className = "update-channel-select update-interval-select";
  const texts = [BoardTexts.interval30, BoardTexts.interval60, BoardTexts.interval120, BoardTexts.interval360,
    BoardTexts.interval720];
  UpdateIntervals.forEach((minutes, index) => {
    const option = element("option", null, texts[index]);
    option.value = String(minutes);
    select.append(option);
  });
  select.value = String(interval);
  select.dataset.saved = String(interval);
  const note = element("p", "auto-update-note");
  note.setAttribute("role", "status");
  note.setAttribute("aria-live", "polite");
  note.hidden = true;
  select.addEventListener("change", () => setUpdateInterval(select, note));
  label.append(element("span", null, BoardTexts.updateInterval), select);
  box.append(label, note);
}

// Sends only the chosen interval; on any error the choice goes back to the stored one and a fixed text is shown.
async function setUpdateInterval(select, note) {
  const wanted = Number(select.value);
  const saved = select.dataset.saved;
  select.disabled = true;
  note.hidden = true;
  try {
    const response = await authFetch(AutoUpdateRoute, {
      method: "POST",
      cache: "no-store",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ updateInterval: wanted }),
    });
    if (!response.ok) {
      throw new Error("status");
    }
    const data = await response.json();
    if (!data || data.updateInterval !== wanted) {
      throw new Error("data");
    }
    select.dataset.saved = String(wanted);
  } catch {
    select.value = saved;
    note.textContent = BoardTexts.intervalFailed;
    note.hidden = false;
  } finally {
    select.disabled = false;
  }
}

function rememberBoardScroll() {
  boardScroll = window.scrollY;
}

// Job page.

function leaveJob() {
  jobRequest += 1;
  openJobId = null;
  openJobEntry = null;
  openJobRecord = null;
  leavePlaytest();
  document.getElementById("job").replaceChildren();
}

function goBackToBoard() {
  if (openedFromBoard) {
    history.back();
    return;
  }
  history.replaceState(null, "", BoardRoute);
  route();
}

function jobBackButton() {
  const button = element("button", "back", BoardTexts.back);
  button.type = "button";
  button.addEventListener("click", goBackToBoard);
  return button;
}

function showJobContent(nodes) {
  const main = document.getElementById("job");
  const bar = element("div", "story-bar");
  bar.appendChild(jobBackButton());
  const footer = element("div", "story-footer");
  footer.appendChild(jobBackButton());
  main.replaceChildren(bar, ...nodes, footer);
}

function blockText(block) {
  return asList(block && block.inlines).map((inline) => asText(inline && inline.text)).join("").trim();
}

function renderRecord(record) {
  const panel = element("div", "record");
  if (!record || record.readable !== true) {
    panel.appendChild(element("p", "error", BoardTexts.recordUnreadable));
    return panel;
  }
  const blocks = asList(record.blocks);
  let start = 0;
  if (blocks.length > 0 && asText(blocks[0] && blocks[0].kind) === "paragraph") {
    const match = Verdict.exec(blockText(blocks[0]));
    if (match) {
      panel.appendChild(element("span", chipClass(match[1] || match[2]) + " verdict", blockText(blocks[0])));
      start = 1;
    }
  }
  for (const block of blocks.slice(start)) {
    panel.appendChild(renderBlock(block));
  }
  return panel;
}

function renderTabs(records, onSelect) {
  const tabs = element("div", "tabs");
  tabs.setAttribute("role", "tablist");
  tabs.setAttribute("aria-label", BoardTexts.records);
  const buttons = records.map((record, index) => {
    const name = asText(record && record.name);
    const tab = element("button", "tab", asText(record && record.label));
    tab.type = "button";
    tab.id = "tab-" + index;
    tab.setAttribute("role", "tab");
    tab.setAttribute("aria-controls", "record-panel");
    const selected = name === openJobRecord;
    tab.setAttribute("aria-selected", selected ? "true" : "false");
    tab.tabIndex = selected ? 0 : -1;
    tab.addEventListener("click", () => onSelect(index));
    tab.addEventListener("keydown", (event) => {
      if (event.key === "ArrowRight" || event.key === "ArrowLeft") {
        event.preventDefault();
        const step = event.key === "ArrowRight" ? 1 : -1;
        onSelect((index + step + records.length) % records.length, true);
      }
    });
    return tab;
  });
  tabs.replaceChildren(...buttons);
  return tabs;
}

// The board's label for a stage; an unknown stage is shown as its own text.
function stageLabel(stage) {
  if (stage === "needs-human") {
    return BoardTexts.needsYou;
  }
  const known = Stages.find((entry) => entry.key === stage);
  return known ? known.label : stage;
}

// Stage and round, reviews per round, decision, flags and note; a missing or empty field adds nothing.
function renderJobState(job) {
  const section = element("section", "job-state");
  section.setAttribute("aria-label", BoardTexts.jobState);
  const stage = asText(job.stage);
  if (stage) {
    const round = asNumber(job.round);
    section.appendChild(element("p", "job-stage", stageLabel(stage) + (round ? BoardTexts.jobRound + round : "")));
  }
  const rounds = reviewRounds(job, true);
  if (rounds.childElementCount > 0) {
    section.appendChild(rounds);
  }
  const outcome = element("div", "job-outcome");
  const decision = asText(job.decision);
  if (decision) {
    outcome.appendChild(element("span", chipClass(decision), decision));
  }
  const flags = flagTexts(job);
  if (flags.length > 0) {
    outcome.appendChild(element("span", "job-flags", BoardTexts.flags + ": " + flags.join(" · ")));
  }
  if (outcome.childElementCount > 0) {
    section.appendChild(outcome);
  }
  const note = asText(job.note);
  if (note) {
    section.appendChild(element("p", "job-note", note));
  }
  return section;
}

function renderJobPage(data) {
  const job = asMap(data && data.job);
  const records = asList(data && data.records);
  const names = records.map((record) => asText(record && record.name));
  if (!names.includes(openJobRecord)) {
    openJobRecord = names.length > 0 ? names[names.length - 1] : null;
  }

  const article = element("article", "job");
  const title = element("h1", "story-title", asText(job.title) || asText(job.id));
  title.tabIndex = -1;
  article.appendChild(title);
  const facts = [
    asText(job.id),
    asText(job.branch),
    asText(job.mode) ? job.mode + " mode" : "",
    asText(job.path) ? job.path + " path" : "",
    asText(job.risk) ? job.risk + " risk" : "",
  ].filter((fact) => fact !== "");
  article.appendChild(element("p", "job-facts", facts.join(" · ")));
  const times = jobTimes(job, "job-times");
  if (times) {
    article.appendChild(times);
  }
  const state = renderJobState(job);
  if (state.childElementCount > 0) {
    article.appendChild(state);
  }
  const agents = renderAgents(asList(data && data.agents));
  if (agents) {
    article.appendChild(agents);
  }
  // A prototype's verdict with its follow-up item, then one playtest panel per round (play.js).
  const verdict = renderVerdict(data);
  if (verdict) {
    article.appendChild(verdict);
  }
  for (const panel of renderPlaytest(asText(job.id), asList(data && data.playtest))) {
    article.appendChild(panel);
  }

  const panel = element("div", "record-panel");
  panel.id = "record-panel";
  panel.setAttribute("role", "tabpanel");
  let tabs = null;

  const select = (index, focus) => {
    openJobRecord = names[index];
    const next = renderTabs(records, select);
    tabs.replaceWith(next);
    tabs = next;
    showRecord(index);
    if (focus) {
      tabs.children[index].focus();
    }
  };
  const showRecord = (index) => {
    panel.setAttribute("aria-labelledby", "tab-" + index);
    panel.replaceChildren(renderRecord(records[index]));
  };

  if (records.length === 0) {
    article.appendChild(element("p", "empty", BoardTexts.noRecords));
  } else {
    tabs = renderTabs(records, select);
    article.appendChild(tabs);
    article.appendChild(panel);
    showRecord(names.indexOf(openJobRecord));
  }
  showJobContent([article]);
  return title;
}

async function loadJob(id, refresh) {
  jobRequest += 1;
  const request = jobRequest;
  if (!refresh) {
    openJobId = id;
    openJobEntry = null;
    openJobRecord = null;
    showJobContent([element("p", "status", BoardTexts.loading)]);
  }
  if (id === "") {
    showJobContent([element("p", "error", BoardTexts.jobFailed)]);
    return;
  }
  let data;
  try {
    const response = await authFetch("/api/job?id=" + encodeURIComponent(id), { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    data = await response.json();
  } catch {
    if (request === jobRequest && !refresh) {
      showJobContent([element("p", "error", BoardTexts.jobFailed)]);
      document.querySelector("#job .back").focus({ preventScroll: true });
    }
    return;
  }
  if (request !== jobRequest) {
    return;
  }
  openJobEntry = JSON.stringify(data && data.job);
  const scroll = window.scrollY;
  const title = renderJobPage(data);
  if (refresh) {
    window.scrollTo(0, scroll);
  } else {
    title.focus({ preventScroll: true });
  }
}

function showJob(id) {
  window.scrollTo(0, 0);
  loadJob(id, false);
  pollBoard();
}
