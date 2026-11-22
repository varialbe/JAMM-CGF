"""Build the public progress page from repository history and benchmark data."""

from datetime import datetime, timedelta, timezone
from html import escape
import json
from pathlib import Path
import re
import subprocess


SKIP_BRANCH_PARTS = ("private", "tge", "pwb", "personal")
EMAIL = re.compile(r"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", re.IGNORECASE)


def safe_text(value: str) -> str:
    """Remove email-like strings before displaying public git metadata."""
    return EMAIL.sub("[redacted]", value)


def git(site_dir: Path, *args: str) -> str:
    result = subprocess.run(
        ["git", "-C", str(site_dir), *args], capture_output=True, text=True,
        encoding="utf-8",
    )
    return result.stdout.strip()


def commit_time(site_dir: Path, commit: str) -> datetime | None:
    value = git(site_dir, "show", "-s", "--format=%cI", commit)
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)
    except ValueError:
        return None


def branch_data(site_dir: Path) -> dict:
    """Return active branches and recently merged branches with timeline positions."""
    main_ref = next((ref for ref in ("refs/remotes/origin/main", "refs/heads/main")
                     if subprocess.run(["git", "-C", str(site_dir), "show-ref", "--verify", "--quiet", ref],
                                       capture_output=True).returncode == 0), None)
    if main_ref is None:
        return {"branches": [], "older_merged": [], "ticks": [], "main_start": None, "main_end": None}

    main_commits = git(site_dir, "rev-list", "--first-parent", "--reverse", main_ref).splitlines()
    if not main_commits:
        return {"branches": [], "older_merged": [], "ticks": [], "main_start": None, "main_end": None}
    main_index = {commit: index for index, commit in enumerate(main_commits)}
    main_times = [commit_time(site_dir, commit) for commit in main_commits]
    valid_times = [value for value in main_times if value is not None]
    start_time = min(valid_times) if valid_times else datetime.now(timezone.utc)
    end_time = max(valid_times) if valid_times else start_time
    span = max((end_time - start_time).total_seconds(), 1)

    def position(commit: str) -> float:
        timestamp = commit_time(site_dir, commit)
        if timestamp is None:
            return 0.0
        return max(0.0, min(1.0, (timestamp - start_time).total_seconds() / span))

    recent_after = datetime.now(timezone.utc) - timedelta(days=3)
    raw_refs = git(site_dir, "for-each-ref", "--format=%(refname)%00%(objectname)",
                   "refs/heads", "refs/remotes/origin")
    candidates: dict[str, dict] = {}
    older_merged: set[str] = set()
    for line in raw_refs.splitlines():
        if "\x00" not in line:
            continue
        ref, tip = line.split("\x00", 1)
        if ref.endswith("/HEAD") or ref in {"refs/heads/main", "refs/remotes/origin/main"}:
            continue
        name = ref.removeprefix("refs/heads/").removeprefix("refs/remotes/origin/")
        if any(part in name.casefold() for part in SKIP_BRANCH_PARTS):
            continue
        tip_time = commit_time(site_dir, tip)
        if tip_time is None:
            continue
        metadata = git(site_dir, "show", "-s", "--format=%s", tip)
        base = git(site_dir, "merge-base", main_ref, tip)
        if not base:
            continue
        counts = git(site_dir, "rev-list", "--left-right", "--count", f"{main_ref}...{tip}")
        try:
            behind, ahead = (int(value) for value in counts.split())
        except (ValueError, TypeError):
            continue

        merged = subprocess.run(["git", "-C", str(site_dir), "merge-base", "--is-ancestor", tip, main_ref],
                                capture_output=True).returncode == 0
        merge_commit = tip if merged else None
        merge_time = tip_time if merged else None
        if merged:
            # The first first-parent main commit containing the branch tip is its join point.
            base_index = main_index.get(base, 0)
            for commit in main_commits[base_index:]:
                if subprocess.run(["git", "-C", str(site_dir), "merge-base", "--is-ancestor", tip, commit],
                                  capture_output=True).returncode == 0:
                    merge_commit = commit
                    merge_time = commit_time(site_dir, commit) or tip_time
                    break
            if merge_time < recent_after:
                older_merged.add(safe_text(name))
                continue
        elif ahead == 0:
            continue

        record = {
            "name": safe_text(name), "subject": safe_text(metadata.splitlines()[0]),
            "date": tip_time.strftime("%Y-%m-%d"), "ahead": ahead, "behind": behind,
            "merged": merged, "fork": position(base),
            "end": position(merge_commit) if merged else position(tip),
            "sort_time": merge_time if merged else tip_time,
        }
        current = candidates.get(name)
        if current is None or record["sort_time"] > current["sort_time"]:
            candidates[name] = record

    branches = sorted(candidates.values(), key=lambda item: (item["name"].casefold(), item["name"]))
    for branch in branches:
        branch.pop("sort_time")
    # Evenly spaced date ticks make the time axis legible when commit dates cluster.
    tick_format = "%b %d" if (end_time - start_time).days < 90 else "%b %Y"
    ticks = []
    for fraction in (0, .25, .5, .75, 1):
        tick_time = start_time + (end_time - start_time) * fraction
        ticks.append({"fraction": fraction, "label": tick_time.strftime(tick_format)})
    return {"branches": branches, "older_merged": sorted(older_merged, key=str.casefold), "ticks": ticks,
            "main_start": start_time.strftime("%b %Y"), "main_end": end_time.strftime("%b %Y")}


def branch_svg(data: dict) -> str:
    """Render one horizontal time lane for every visible branch."""
    branches = data["branches"]
    groups: dict[str, list[dict]] = {}
    for branch in branches:
        prefix = branch["name"].split("/", 1)[0] if "/" in branch["name"] else "other"
        groups.setdefault(prefix, []).append(branch)
    width, trunk_x, left, timeline_width, label_right = 1210, 112, 168, 430, 1192
    axis_y, row_height, group_height = 48, 34, 29
    rows, y = [], axis_y + 28
    tick_marks = []
    for tick in data["ticks"]:
        x = left + timeline_width * tick["fraction"]
        tick_marks.append(f'<path class="timeline-grid" d="M {x:.1f} {axis_y + 10} V 9999"/>')
    for prefix, items in sorted(groups.items(), key=lambda pair: pair[0].casefold()):
        rows.append(f'<text class="branch-group" x="{trunk_x + 14}" y="{y + 14}">{escape(prefix)}/</text>')
        y += group_height
        for branch in items:
            fork_x = left + timeline_width * branch["fork"]
            end_x = left + timeline_width * branch["end"]
            lane_start, lane_end = sorted((fork_x, end_x))
            state = "merged" if branch["merged"] else "open"
            color = "var(--branch-merged)" if branch["merged"] else "var(--branch-open)"
            label_x = max(lane_end + 12, left + 14)
            text = branch["name"]
            badge = f'ahead {branch["ahead"]} / behind {branch["behind"]} · {branch["date"]}'
            estimated_width = min(920, max(250, len(text) * 7.1 + len(badge) * 6.4 + 42))
            label_x = min(label_x, label_right - estimated_width)
            badge_x = label_x + len(text) * 7.1 + 18
            badge_width = len(badge) * 6.4 + 12
            y_center = y + row_height / 2
            join = (f'<circle class="lane-join" cx="{end_x:.1f}" cy="{y_center:.1f}" r="4"/> '
                    if branch["merged"] else "")
            rows.append(
                f'<g class="branch-lane {state}"><path d="M {lane_start:.1f} {y_center:.1f} H {lane_end:.1f}"/>'
                f'<circle class="lane-fork" cx="{fork_x:.1f}" cy="{y_center:.1f}" r="4"/>{join}'
                f'<rect class="lane-label-bg" x="{label_x:.1f}" y="{y + 3}" width="{estimated_width:.1f}" height="28" rx="6"/>'
                f'<text class="lane-label" x="{label_x + 9:.1f}" y="{y + 21}">{escape(text)}</text>'
                f'<rect class="lane-badge-bg" x="{badge_x:.1f}" y="{y + 6}" width="{badge_width:.1f}" height="22" rx="5"/>'
                f'<text class="lane-badge" x="{badge_x + 6:.1f}" y="{y + 21}">{escape(badge)}</text></g>'
            )
            y += row_height
    height = y + 36
    # Bound grid lines to the actual diagram height.
    tick_marks = [mark.replace("9999", str(height - 22)) for mark in tick_marks]
    ticks = "".join(
        f'<text class="timeline-label" x="{left + timeline_width * tick["fraction"]:.1f}" y="{axis_y}" text-anchor="middle">{escape(tick["label"])}</text>'
        for tick in data["ticks"]
    )
    return (f'<svg class="branch-graph" viewBox="0 0 {width} {height}" role="img" aria-labelledby="branch-graph-title branch-graph-desc">'
            '<title id="branch-graph-title">Open and recently merged branches</title>'
            '<desc id="branch-graph-desc">Each horizontal lane runs from its merge base to the branch tip or merge commit, along a calendar time axis.</desc>'
            f'<path class="main-trunk" d="M {trunk_x} {axis_y + 6} V {height - 22}"/><text class="trunk-label" x="{trunk_x - 28}" y="{axis_y + 2}">main</text>'
            f'{"".join(tick_marks)}{ticks}{"".join(rows)}</svg>')


def branch_section(data: dict) -> str:
    branches = data["branches"]
    open_count = sum(not branch["merged"] for branch in branches)
    recent_count = len(branches) - open_count
    older_rows = data.get("older_merged", [])
    older = len(older_rows)
    older_html = "".join(f'<li><code>{escape(name)}</code></li>' for name in older_rows)
    return f'''<section class="progress-section" aria-labelledby="branches-heading">
      <p class="eyebrow">Work in progress</p><h2 id="branches-heading">Branch tree</h2>
      <p>Open branches and branches merged in the last three days are shown on a calendar timeline. Private and personal branch names are omitted.</p>
      <div class="graph-wrap">{branch_svg(data)}<p class="graph-key"><span class="key-open">● Open</span><span class="key-merged">● Merged recently</span><span>Scroll horizontally to view complete lanes · ahead / behind counts compare with main</span></p></div>
      <details class="older-branches"><summary>Older merged branches ({older})</summary><p>Older merged branches are hidden from the timeline.</p><ul>{older_html}</ul></details>
    </section>'''


def wrapped_words(text: str, max_chars: int = 30) -> list[str]:
    """Wrap chart labels into SVG text lines without truncating them."""
    words, lines, current = text.split(), [], ""
    for word in words:
        candidate = f"{current} {word}".strip()
        if current and len(candidate) > max_chars:
            lines.append(current)
            current = word
        else:
            current = candidate
    if current:
        lines.append(current)
    return lines


def performance_svg(item: dict) -> str:
    """Draw one independently scaled chart for a single benchmark series."""
    stages = item.get("stages", [])
    width, left, right, top = 1000, 300, 565, 44
    maximum = max((float(stage["loop_ms"]) for stage in stages), default=1.0)
    scale = (right - left) / maximum if maximum else 1
    row_heights = [max(68, 52 + 15 * (len(wrapped_words(stage["label"])) - 1)) for stage in stages]
    height = top + sum(row_heights or [68]) + 30
    rows = []
    base = float(stages[0]["loop_ms"]) if stages else 0
    y = top
    for index, stage in enumerate(stages):
        value = float(stage["loop_ms"])
        label_lines = wrapped_words(stage["label"])
        role = "reference" if stage["label"].casefold().startswith("reference") else (
            "start" if index == 0 else "latest" if index == len(stages) - 1 else "intermediate")
        text_nodes = "".join(f'<tspan x="{left - 14}" dy="{0 if line_index == 0 else 15}">{escape(line)}</tspan>'
                             for line_index, line in enumerate(label_lines))
        if index == 0:
            change = "baseline"
        elif value < base and value > 0:
            change_pct = (base - value) / base * 100
            faster = base / value
            change = f'−{change_pct:.0f} % time · {faster:.2f}× faster'
        elif value > base and base > 0:
            change = f'+{(value - base) / base * 100:.0f} % time · {value / base:.2f}× slower'
        else:
            change = "unchanged from first stage"
        width_value = value * scale
        rows.append(
            f'<g class="perf-row {role}"><text class="bar-label" x="{left - 14}" y="{y + 15}" text-anchor="end">{text_nodes}</text>'
            f'<rect class="perf-bar" x="{left}" y="{y + 3}" width="{width_value:.2f}" height="30" rx="5"/>'
            f'<text class="bar-value" x="{left + width_value + 10:.2f}" y="{y + 22}">{value:g} ms · {change}</text>'
            f'<text class="bar-note" x="{left}" y="{y + 50}">{escape(stage.get("date", ""))} · {escape(stage.get("note", ""))}</text></g>'
        )
        y += row_heights[index]
    ticks = []
    for tick in range(5):
        x = left + (right - left) * tick / 4
        ticks.append(f'<path class="grid-line" d="M {x:g} {top - 6} V {height - 22}"/><text class="axis-label" x="{x:g}" y="{height - 2}" text-anchor="middle">{maximum * tick / 4:.0f} ms</text>')
    return (f'<svg class="perf-chart" viewBox="0 0 {width} {height}" role="img" aria-label="{escape(item.get("title", item.get("id", "Performance")))} stage timings">'
            f'{"".join(ticks)}{"".join(rows)}</svg>')


def performance_mobile(item: dict) -> str:
    """Render the same series as readable stacked bars on narrow screens."""
    stages = item.get("stages", [])
    maximum = max((float(stage["loop_ms"]) for stage in stages), default=1.0)
    base = float(stages[0]["loop_ms"]) if stages else 0
    rows = []
    for index, stage in enumerate(stages):
        value = float(stage["loop_ms"])
        role = "reference" if stage["label"].casefold().startswith("reference") else (
            "start" if index == 0 else "latest" if index == len(stages) - 1 else "intermediate")
        if index == 0:
            change = "baseline"
        elif value < base and value > 0:
            change = f'−{(base - value) / base * 100:.0f} % time · {base / value:.2f}× faster'
        elif value > base and base > 0:
            change = f'+{(value - base) / base * 100:.0f} % time · {value / base:.2f}× slower'
        else:
            change = "unchanged from first stage"
        percent = value / maximum * 100 if maximum else 0
        rows.append(
            f'<div class="mobile-stage {role}"><strong class="mobile-stage-label">{escape(stage["label"])}</strong>'
            f'<div class="mobile-bar-track"><span class="mobile-bar" style="width:{percent:.2f}%"></span></div>'
            f'<p class="mobile-stage-value">{value:g} ms · {escape(change)}</p>'
            f'<p class="mobile-stage-note">{escape(stage.get("date", ""))} · {escape(stage.get("note", ""))}</p></div>'
        )
    return f'<div class="perf-mobile" role="list">{"".join(rows)}</div>'


def performance_section(site_dir: Path) -> str:
    payload = json.loads((site_dir / "content" / "perf-history.json").read_text(encoding="utf-8"))
    charts = "".join(
        f'<section class="series-chart" aria-labelledby="series-{escape(item.get("id", str(index)))}">'
        f'<h3 id="series-{escape(item.get("id", str(index)))}">{escape(item.get("title", item.get("id", "Scenario")))}</h3>'
        f'<div class="chart-wrap">{performance_svg(item)}{performance_mobile(item)}</div></section>'
        for index, item in enumerate(payload.get("series", []))
    )
    return f'''<section class="progress-section" aria-labelledby="performance-heading">
      <p class="eyebrow">Measured performance</p><h2 id="performance-heading">Optimisation stages</h2>
      <p>Loop time per tick, in milliseconds. Shorter bars mean less work per tick; each series uses its own time scale.</p>
      {charts}
      <p class="method-note"><strong>Measurement method:</strong> {escape(payload["method"])}</p>
    </section>'''


def progress_page(site_dir: Path) -> str:
    """Create complete static progress HTML with inline SVG and local styles only."""
    data = branch_data(site_dir)
    body = f'''<nav class="breadcrumbs" aria-label="Breadcrumb"><a href="index.html">Packages</a><span aria-hidden="true">/</span><span>Progress</span></nav>
    <header class="guide-header"><p class="eyebrow">JAMM-CGF development</p><h1>Progress</h1><p>A readable snapshot of active work and the optimisation steps behind the current build.</p></header>
    {branch_section(data)}
    {performance_section(site_dir)}
    <p class="guide-back"><a href="index.html#packages">← Back to all packages</a></p>'''
    return f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="description" content="JAMM-CGF development branches and optimisation progress."><title>Progress — JAMM CGF Mods</title>
<link rel="stylesheet" href="style.css"></head><body><a class="skip-link" href="#main-content">Skip to content</a>
<nav class="site-nav" aria-label="Main navigation"><a class="brand" href="index.html">JAMM <span>CGF Mods</span></a><div class="nav-links"><a href="index.html#packages">Packages</a><a href="how-jamm-builds.html">How builds work</a><a href="compatibility.html">Compatibility</a><a href="progress.html" aria-current="page">Progress</a></div></nav>
<main id="main-content">{body}</main></body></html>'''
