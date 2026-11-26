"use strict";

// Releases overview on the Backlogs view: per readable project one block (div.project-release, directly after the
// div.project-status line) with the last local release tag, the commits on develop since then, the version
// /factory release would suggest and a closed list of those commits, from /api/releases. Read-only. Fetched when the
// Backlogs view is shown and, while shown, when live.js sees a changed jobs or backlog fingerprint (never on a timer
// of its own, never while hidden). Older answers are dropped by a request counter. A refresh replaces only the
// release blocks; a block with focus inside is left alone and tried again on the next check, and an open commit
// list stays open. Loaded after status.js and before live.js and app.js; their functions (element, authFetch) are
// only called at run time. Every text is fixed or set with textContent: tags and subjects are text, never HTML.

const ReleaseTexts = {
  failed: "Releases unavailable.",
  unavailable: "Release info unavailable",
  lastRelease: "Last release ",
  noRelease: "No release yet",
  sinceOne: "1 commit on develop since ",
  sinceMany: " commits on develop since ",
  nothingSince: "Nothing on develop since ",
  allOne: "1 commit on develop",
  allMany: " commits on develop",
  next: "Next release: v",
  minor: " (new features)",
  patch: " (fixes only)",
  noJobs: "No merged jobs to release",
  note: "/factory release still checks for breaking changes",
  commits: "Commits (",
  commitsEnd: ")",
  separator: " · ",
};

const ReleasesApi = "/api/releases";
const ReleaseTagPattern = /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const ReleaseVersionPattern = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const ReleaseShaPattern = /^[0-9a-f]{7}$/;
const ReleaseJobPattern = /^[0-9]{1,6}-[a-z0-9-]{1,80}(\.[0-9a-f]{32})?$/;
const ReleaseBumps = ["minor", "patch"];

let releaseRequest = 0;
// null before the first answer, "failed" after a failed call, else a Map of project name -> release data.
let releaseData = null;
// The projects whose commit list the user opened; kept across refreshes and re-rendered sections.
const releaseOpen = new Set();

function releaseLocalDate(text) {
  if (typeof text !== "string" || text === "") {
    return "";
  }
  const date = new Date(text);
  if (Number.isNaN(date.getTime())) {
    return "";
  }
  return date.toLocaleDateString(undefined, { dateStyle: "medium" });
}

// One project from the response, with every field checked; unknown shapes count as unavailable.
function readReleaseProject(item) {
  const release = item.release && typeof item.release.tag === "string" && ReleaseTagPattern.test(item.release.tag)
    ? { tag: item.release.tag, date: typeof item.release.date === "string" ? item.release.date : "" }
    : null;
  const log = [];
  if (Array.isArray(item.log)) {
    for (const row of item.log) {
      if (!row || typeof row.sha !== "string" || !ReleaseShaPattern.test(row.sha) || typeof row.subject !== "string") {
        continue;
      }
      const job = typeof row.job === "string" && ReleaseJobPattern.test(row.job) ? row.job : null;
      log.push({ sha: row.sha, subject: row.subject, job: job });
    }
  }
  const commits = typeof item.commits === "number" && Number.isInteger(item.commits) && item.commits >= 0
    ? item.commits
    : null;
  const bump = ReleaseBumps.includes(item.bump) ? item.bump : null;
  const suggested = bump !== null && typeof item.suggested === "string" && ReleaseVersionPattern.test(item.suggested)
    ? item.suggested
    : null;
  return {
    available: item.available === true && commits !== null,
    release: release,
    commits: commits,
    log: log,
    suggested: suggested,
    bump: suggested === null ? null : bump,
  };
}

// The response as a Map of name -> release data, or null when it has another shape.
function readReleases(data) {
  if (!data || !Array.isArray(data.projects)) {
    return null;
  }
  const projects = new Map();
  for (const item of data.projects) {
    if (!item || typeof item.name !== "string") {
      continue;
    }
    projects.set(item.name, readReleaseProject(item));
  }
  return projects;
}

// The last-release text: tag and local date, or No release yet.
function releaseTagText(release) {
  if (release === null) {
    return ReleaseTexts.noRelease;
  }
  const date = releaseLocalDate(release.date);
  return ReleaseTexts.lastRelease + release.tag + (date === "" ? "" : ReleaseTexts.separator + date);
}

// The commits text: singular for 1, Nothing for 0 after a tag, and without "since" when there is no tag.
function releaseCommitsText(commits, release) {
  if (release === null) {
    return commits === 1 ? ReleaseTexts.allOne : String(commits) + ReleaseTexts.allMany;
  }
  if (commits === 0) {
    return ReleaseTexts.nothingSince + release.tag;
  }
  if (commits === 1) {
    return ReleaseTexts.sinceOne + release.tag;
  }
  return String(commits) + ReleaseTexts.sinceMany + release.tag;
}

function releaseNextText(data) {
  if (data.suggested === null) {
    return ReleaseTexts.noJobs;
  }
  return ReleaseTexts.next + data.suggested + (data.bump === "minor" ? ReleaseTexts.minor : ReleaseTexts.patch);
}

// The closed commit list: each row's sha and subject as text, a subject with a job linking to its job page.
function releaseLog(projectName, log) {
  const details = element("details", "release-log");
  details.open = releaseOpen.has(projectName);
  details.addEventListener("toggle", () => {
    if (details.open) {
      releaseOpen.add(projectName);
    } else {
      releaseOpen.delete(projectName);
    }
  });
  details.appendChild(element("summary", "", ReleaseTexts.commits + String(log.length) + ReleaseTexts.commitsEnd));
  const list = element("ul", "release-commits");
  for (const row of log) {
    const item = element("li", "release-commit");
    item.appendChild(element("code", "release-sha", row.sha));
    if (row.job !== null) {
      const link = element("a", "release-subject", row.subject);
      link.href = "#job/" + row.job;
      item.appendChild(link);
    } else {
      item.appendChild(element("span", "release-subject", row.subject));
    }
    list.appendChild(item);
  }
  details.appendChild(list);
  return details;
}

// Fills one block from the current data: nothing yet (hidden), the failed text, the unavailable text, or the lines.
function fillReleaseBlock(block) {
  if (releaseData === null) {
    block.replaceChildren();
    block.hidden = true;
    return;
  }
  block.hidden = false;
  if (releaseData === "failed") {
    block.replaceChildren(element("p", "release-failed", ReleaseTexts.failed));
    return;
  }
  const name = block.dataset.project;
  const data = releaseData.get(name);
  if (!data || !data.available) {
    block.replaceChildren(element("p", "release-unavailable", ReleaseTexts.unavailable));
    return;
  }
  const old = block.querySelector("details.release-log");
  if (old !== null && old.open) {
    releaseOpen.add(name);
  }
  const parts = [
    element("p", "release-tag", releaseTagText(data.release)),
    element("p", "release-commits-count", releaseCommitsText(data.commits, data.release)),
    element("p", "release-next", releaseNextText(data)),
    element("p", "release-note", ReleaseTexts.note),
  ];
  if (data.log.length > 0) {
    parts.push(releaseLog(name, data.log));
  }
  block.replaceChildren(...parts);
}

// A new release block for a project section (app.js renderProject), filled from the last answer.
function releaseBlock(projectName) {
  const block = element("div", "project-release");
  block.dataset.project = projectName;
  fillReleaseBlock(block);
  return block;
}

// Replaces the release blocks on the Backlogs view only; false when a block with focus inside was left alone.
function applyReleaseBlocks() {
  let all = true;
  for (const block of document.querySelectorAll("#backlogs section.project > div.project-release")) {
    if (block.contains(document.activeElement)) {
      all = false;
      continue;
    }
    fillReleaseBlock(block);
  }
  return all;
}

// One /api/releases call; an older answer is dropped. True when it succeeded and every block was replaced.
async function loadReleases() {
  releaseRequest += 1;
  const request = releaseRequest;
  let data = null;
  try {
    const response = await authFetch(ReleasesApi, { cache: "no-store" });
    if (response.ok) {
      data = readReleases(await response.json());
    }
  } catch {
    data = null;
  }
  if (request !== releaseRequest) {
    return true;
  }
  releaseData = data === null ? "failed" : data;
  const applied = applyReleaseBlocks();
  return data !== null && applied;
}

// Called by app.js each time the Backlogs view is shown.
function showReleases() {
  loadReleases();
}
