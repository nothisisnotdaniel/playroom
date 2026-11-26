"use strict";

// Project status on the Backlogs view: per readable project one line (div.project-status, between the head and the
// plan) with how many commits develop has that main does not (GameFactory records no CI result), from
// /api/status. Read-only. Fetched when the Backlogs view is shown and, while shown, when live.js sees a changed jobs
// or backlog fingerprint (never on a timer of its own, never while hidden). A refresh replaces only the status lines;
// a line with focus inside is left alone and tried again on the next check. Loaded after doctor.js and before
// live.js and app.js; their functions (element, asText, authFetch) are only called at run time. Every text is fixed
// and set with textContent.

const StatusTexts = {
  failed: "Status unavailable.",
  aheadOne: "1 commit on develop not on main",
  aheadMany: " commits on develop not on main",
  level: "develop is level with main",
  gitUnknown: "Git status unavailable",
};

const StatusApi = "/api/status";

let statusRequest = 0;
// null before the first answer, "failed" after a failed call, else a Map of project name -> { ahead }.
let statusData = null;

// The fixed develop/main text: singular for 1, level for 0, unavailable for null.
function statusAheadText(ahead) {
  if (typeof ahead !== "number" || !Number.isInteger(ahead) || ahead < 0) {
    return StatusTexts.gitUnknown;
  }
  if (ahead === 0) {
    return StatusTexts.level;
  }
  if (ahead === 1) {
    return StatusTexts.aheadOne;
  }
  return String(ahead) + StatusTexts.aheadMany;
}

// The response as a Map of name -> { ahead }, or null when it has another shape.
function readStatus(data) {
  if (!data || !Array.isArray(data.projects)) {
    return null;
  }
  const projects = new Map();
  for (const item of data.projects) {
    if (!item || typeof item.name !== "string") {
      continue;
    }
    projects.set(item.name, { ahead: typeof item.ahead === "number" ? item.ahead : null });
  }
  return projects;
}

// Fills one line from the current data: nothing yet (hidden), the failed text, or the git part.
function fillStatusLine(line) {
  if (statusData === null) {
    line.replaceChildren();
    line.hidden = true;
    return;
  }
  line.hidden = false;
  if (statusData === "failed") {
    line.replaceChildren(element("span", "status-failed", StatusTexts.failed));
    return;
  }
  const status = statusData.get(line.dataset.project) || { ahead: null };
  line.replaceChildren(element("span", "status-ahead", statusAheadText(status.ahead)));
}

// A new status line for a project section (app.js renderProject), filled from the last answer.
function statusLine(projectName) {
  const line = element("div", "project-status");
  line.dataset.project = projectName;
  fillStatusLine(line);
  return line;
}

// Replaces the status lines on the Backlogs view only; false when a line with focus inside was left alone.
function applyStatusLines() {
  let all = true;
  for (const line of document.querySelectorAll("#backlogs section.project > div.project-status")) {
    if (line.contains(document.activeElement)) {
      all = false;
      continue;
    }
    fillStatusLine(line);
  }
  return all;
}

// One /api/status call; an older answer is dropped. True when it succeeded and every line was replaced.
async function loadStatus() {
  statusRequest += 1;
  const request = statusRequest;
  let data = null;
  try {
    const response = await authFetch(StatusApi, { cache: "no-store" });
    if (response.ok) {
      data = readStatus(await response.json());
    }
  } catch {
    data = null;
  }
  if (request !== statusRequest) {
    return true;
  }
  statusData = data === null ? "failed" : data;
  const applied = applyStatusLines();
  return data !== null && applied;
}

// Called by app.js each time the Backlogs view is shown.
function showStatus() {
  loadStatus();
}
