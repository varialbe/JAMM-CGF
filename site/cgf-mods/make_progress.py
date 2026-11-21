"""Build the public progress page from repository history and benchmark data."""

from datetime import datetime, timezone
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
        ["git", "-C", str(site_dir), *args],
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    return result.stdout.strip()


def branch_data(site_dir: Path) -> list[dict]:
    """Return active public branches without reading or exposing author data."""
    main_ref = None
    for ref in ("refs/remotes/origin/main", "refs/heads/main"):
        if subprocess.run(
            ["git", "-C", str(site_dir), "show-ref", "--verify", "--quiet", ref],
            capture_output=True,
        ).returncode == 0:
            main_ref = ref
            break
    if main_ref is None:
        return []
    main_length = max(1, int(git(site_dir, "rev-list", "--first-parent", "--count", main_ref)))

    raw_refs = git(
        site_dir,
        "for-each-ref",
        "--format=%(refname)%00%(objectname)",
        "refs/heads",
        "refs/remotes/origin",
    )
    candidates: dict[str, dict] = {}
    for line in raw_refs.splitlines():
        if "\x00" not in line:
            continue
        ref, tip = line.split("\x00", 1)
        if ref.endswith("/HEAD") or ref in {"refs/heads/main", "refs/remotes/origin/main"}:
            continue
        name = ref.removeprefix("refs/heads/").removeprefix("refs/remotes/origin/")
        if any(part in name.casefold() for part in SKIP_BRANCH_PARTS):
            continue
        metadata = git(site_dir, "show", "-s", "--format=%cI%x00%s", tip)
        if "\x00" not in metadata:
            continue
        date_text, subject = metadata.split("\x00", 1)
        try:
            commit_date = datetime.fromisoformat(date_text.replace("Z", "+00:00"))
        except ValueError:
            continue
        merged = subprocess.run(
            ["git", "-C", str(site_dir), "merge-base", "--is-ancestor", tip, main_ref],
            capture_output=True,
        ).returncode == 0
        counts = git(site_dir, "rev-list", "--left-right", "--count", f"{main_ref}...{tip}")
        behind, ahead = (int(value) for value in counts.split())
        base = git(site_dir, "merge-base", main_ref, tip)
        commits_after_base = int(git(site_dir, "rev-list", "--first-parent", "--count", f"{base}..{main_ref}"))
        fork_ratio = max(0.0, min(1.0, 1.0 - commits_after_base / main_length))
        join_ratio = None
        if merged:
            commits_after_tip = int(git(site_dir, "rev-list", "--first-parent", "--count", f"{tip}..{main_ref}"))
            join_ratio = max(fork_ratio, min(1.0, 1.0 - commits_after_tip / main_length))
        recent = (datetime.now(timezone.utc) - commit_date.astimezone(timezone.utc)).days <= 14
        if not recent and merged:
            continue
        branch = {
            "name": safe_text(name),
            "date": commit_date.astimezone(timezone.utc).strftime("%Y-%m-%d"),
            "subject": safe_text(subject.splitlines()[0]),
            "ahead": ahead,
            "behind": behind,
            "merged": merged,
            "fork_ratio": fork_ratio,
            "join_ratio": join_ratio,
            "tip": tip,
            "commit_date": commit_date,
        }
        old = candidates.get(name)
        if old is None or branch["commit_date"] > old["commit_date"]:
            candidates[name] = branch

    ordered = sorted(candidates.values(), key=lambda branch: (branch["name"].casefold(), branch["name"]))
    for branch in ordered:
        branch.pop("tip")
        branch.pop("commit_date")
    return ordered


def branch_svg(branches: list[dict]) -> str:
    """Render a compact trunk and one readable lane for each listed branch."""
    width, row_height, top = 920, 54, 56
    height = top + max(1, len(branches)) * row_height + 20
    trunk_x, lane_x = 80, 260
    rows = []
    for index, branch in enumerate(branches):
        y = top + index * row_height
        trunk_span = height - top - 24
        fork_y = top + branch["fork_ratio"] * trunk_span
        color = "var(--accent)" if branch["merged"] else "var(--branch-open)"
        endpoint = lane_x + (120 if branch["merged"] else min(max(branch["ahead"], 1), 28) * 15)
        state = "merged" if branch["merged"] else "open"
        join_y = top + branch["join_ratio"] * trunk_span if branch["join_ratio"] is not None else 0
        join = (
            f' C {endpoint + 40} {y + 20}, {trunk_x + 75} {join_y:.1f}, {trunk_x} {join_y:.1f}'
            if branch["merged"] else ""
        )
        rows.append(
            f'<g class="branch-lane {state}"><path d="M {trunk_x} {fork_y:.1f} C 145 {fork_y:.1f}, 145 {y + 20}, {lane_x} {y + 20} H {endpoint}" '
            f'{join} stroke="{color}"/><circle cx="{trunk_x}" cy="{fork_y:.1f}" r="5" fill="{color}"/>'
            f'{f"<circle cx=\"{trunk_x}\" cy=\"{join_y:.1f}\" r=\"5\" fill=\"{color}\"/>" if branch["merged"] else f"<circle cx=\"{endpoint}\" cy=\"{y + 20}\" r=\"5\" fill=\"{color}\"/>"}'
            f'<text x="{lane_x + 12}" y="{y + 25}">{escape(branch["name"])}</text></g>'
        )
    return (
        f'<svg class="branch-graph" viewBox="0 0 {width} {height}" role="img" '
        'aria-labelledby="branch-graph-title branch-graph-desc" preserveAspectRatio="xMinYMin meet">'
        '<title id="branch-graph-title">Active git branch tree</title>'
        '<desc id="branch-graph-desc">Main is the vertical trunk. Each listed branch forks from it; colored endpoints distinguish merged and open branches.</desc>'
        f'<path class="main-trunk" d="M {trunk_x} 24 V {height - 12}"/><text class="trunk-label" x="{trunk_x + 12}" y="30">main</text>'
        f'{"".join(rows) if rows else "<text x=\"110\" y=\"70\">No public active branches found.</text>"}</svg>'
    )


def branch_section(branches: list[dict]) -> str:
    table_rows = []
    for branch in branches:
        merged = "Yes" if branch["merged"] else "No"
        table_rows.append(
            "<tr>"
            f'<th scope="row"><code>{escape(branch["name"])}</code></th>'
            f'<td>{escape(branch["date"])}</td><td>{escape(branch["subject"])}</td>'
            f'<td>+{branch["ahead"]} / −{branch["behind"]}</td><td>{merged}</td></tr>'
        )
    if not table_rows:
        table_rows.append('<tr><td colspan="5">No public active branches found.</td></tr>')
    return f'''<section class="progress-section" aria-labelledby="branches-heading">
      <p class="eyebrow">Work in progress</p><h2 id="branches-heading">Branch tree</h2>
      <p>Branches appear when their latest commit is within 14 days or they still contain work outside main. Private and personal branch names are omitted.</p>
      <div class="graph-wrap">{branch_svg(branches)}<p class="graph-key"><span class="key-merged">●</span> Merged into main <span class="key-open">●</span> Open work</p></div>
      <div class="table-wrap"><table class="branch-table"><thead><tr><th scope="col">Branch</th><th scope="col">Last commit</th><th scope="col">Latest change</th><th scope="col">Ahead / behind</th><th scope="col">Merged</th></tr></thead><tbody>{"".join(table_rows)}</tbody></table></div>
    </section>'''


def performance_svg(payload: dict) -> str:
    series = payload.get("series", [])
    stages = [(item, stage) for item in series for stage in item.get("stages", [])]
    width, left, right, row_height, top = 960, 210, 650, 45, 42
    height = top + max(1, len(stages)) * row_height + 32
    maximum = max((float(stage["loop_ms"]) for _, stage in stages), default=1.0)
    scale = (right - left) / maximum
    colors = ["var(--accent)", "var(--bar-alt)", "var(--bar-third)", "var(--bar-fourth)"]
    rows = []
    for row, (item, stage) in enumerate(stages):
        y = top + row * row_height
        value = float(stage["loop_ms"])
        bar_width = value * scale
        scenario_base = float(item["stages"][0]["loop_ms"])
        improvement = (scenario_base - value) / scenario_base * 100 if scenario_base else 0
        fps = min(60.0, 1000.0 / value) if value > 0 else 60.0
        label = f'{item.get("title", item.get("id", "Scenario"))}: {stage["label"]}'
        rows.append(
            f'<g class="perf-row"><text class="bar-label" x="{left - 12}" y="{y + 19}" text-anchor="end">{escape(label)}</text>'
            f'<rect x="{left}" y="{y}" width="{bar_width:.2f}" height="25" rx="5" fill="{colors[row % len(colors)]}"/>'
            f'<text class="bar-value" x="{left + bar_width + 10:.2f}" y="{y + 18}">{value:g} ms · {fps:.1f} FPS · {improvement:+.0f}%</text>'
            f'<text class="bar-note" x="{left}" y="{y + 39}">{escape(stage.get("date", ""))} · {escape(stage.get("note", ""))}</text></g>'
        )
    ticks = []
    for tick in range(0, 5):
        x = left + (right - left) * tick / 4
        ticks.append(f'<path class="grid-line" d="M {x:g} {top - 7} V {height - 20}"/><text class="axis-label" x="{x:g}" y="{height - 2}" text-anchor="middle">{maximum * tick / 4:.0f} ms</text>')
    return (
        f'<svg class="perf-chart" viewBox="0 0 {width} {height}" role="img" aria-labelledby="perf-title perf-desc">'
        '<title id="perf-title">Optimisation stages by scenario</title>'
        '<desc id="perf-desc">Horizontal bars compare loop time per tick. Lower bars are faster; labels show capped frames per second and percent change from each scenario baseline.</desc>'
        f'{"".join(ticks)}{"".join(rows)}</svg>'
    )


def performance_section(site_dir: Path) -> str:
    source = site_dir / "content" / "perf-history.json"
    payload = json.loads(source.read_text(encoding="utf-8"))
    titles = "".join(f'<li><strong>{escape(item.get("title", item.get("id", "Scenario")))}</strong></li>' for item in payload.get("series", []))
    return f'''<section class="progress-section" aria-labelledby="performance-heading">
      <p class="eyebrow">Measured performance</p><h2 id="performance-heading">Optimisation stages</h2>
      <p>Loop time per tick, in milliseconds. Shorter bars mean less work per tick; the chart also shows the capped FPS equivalent and change from the first stage in each scenario.</p>
      <ul class="scenario-list">{titles}</ul>
      <div class="chart-wrap">{performance_svg(payload)}</div>
      <p class="method-note"><strong>Measurement method:</strong> {escape(payload["method"])}</p>
    </section>'''


def progress_page(site_dir: Path) -> str:
    """Create complete static progress HTML with inline SVG and local styles only."""
    branches = branch_data(site_dir)
    body = f'''<nav class="breadcrumbs" aria-label="Breadcrumb"><a href="index.html">Packages</a><span aria-hidden="true">/</span><span>Progress</span></nav>
    <header class="guide-header"><p class="eyebrow">JAMM-CGF development</p><h1>Progress</h1><p>A readable snapshot of active work and the optimisation steps behind the current build.</p></header>
    {branch_section(branches)}
    {performance_section(site_dir)}
    <p class="guide-back"><a href="index.html#packages">← Back to all packages</a></p>'''
    return f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="description" content="JAMM-CGF development branches and optimisation progress."><title>Progress — JAMM CGF Mods</title>
<link rel="stylesheet" href="style.css"></head><body><a class="skip-link" href="#main-content">Skip to content</a>
<nav class="site-nav" aria-label="Main navigation"><a class="brand" href="index.html">JAMM <span>CGF Mods</span></a><div class="nav-links"><a href="index.html#packages">Packages</a><a href="how-jamm-builds.html">How builds work</a><a href="compatibility.html">Compatibility</a><a href="progress.html" aria-current="page">Progress</a></div></nav>
<main id="main-content">{body}</main></body></html>'''
