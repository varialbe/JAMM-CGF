#!/usr/bin/env python3
"""Render the static JAMM CGF Mods site from catalog and editorial content."""

from html import escape
import json
from pathlib import Path
from urllib.parse import urlsplit
from zipfile import ZipFile

SITE_DIR = Path(__file__).resolve().parent
PACKAGE_TYPES = {"recipe": "Recipe", "modkit": "ModKit"}
DATASHEET_ROWS = (
    ("apis", "APIs used"),
    ("assets", "Assets and scripts added"),
    ("saves", "Save-file behaviour"),
    ("compatibility", "Compatibility and merge behaviour"),
    ("minimumJamm", "Minimum JAMM version"),
)


def inline(value: str) -> str:
    """Escape content text and render backtick-delimited code."""
    return "".join(
        f"<code>{escape(part)}</code>" if index % 2 else escape(part)
        for index, part in enumerate(value.split("`"))
    )


def local_url(url: str) -> str:
    return urlsplit(url).path.removeprefix("/cgf-mods/")


def page(title: str, stylesheet: str, body: str, current: str = "") -> str:
    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="description" content="JAMM-CGF mod packages, technical notes, installation and compatibility.">
  <title>{escape(title)}</title>
  <link rel="stylesheet" href="{stylesheet}">
</head>
<body>
  <a class="skip-link" href="#main-content">Skip to content</a>
  <nav class="site-nav" aria-label="Main navigation">
    <a class="brand" href="{('../' if current == 'detail' else '')}index.html">JAMM <span>CGF Mods</span></a>
    <div class="nav-links">
      <a href="{('../' if current == 'detail' else '')}index.html#packages">Packages</a>
      <a href="{('../' if current == 'detail' else '')}how-jamm-builds.html">How builds work</a>
      <a href="{('../' if current == 'detail' else '')}compatibility.html">Compatibility</a>
    </div>
  </nav>
  <main id="main-content">
{body}
  </main>
</body>
</html>
"""


def badges(mod: dict, content: dict, detail: bool = False) -> str:
    if mod["isDefault"]:
        status, style = "First-party default", "b-default"
    elif "ModKit" in mod["categories"] and not detail:
        status, style = "Example package", "b-test"
    elif content.get("preview") and not detail:
        status, style = "Preview", "b-optional"
    else:
        status, style = "Optional", "b-optional"
    package_type = PACKAGE_TYPES[mod["packageType"]] if detail else content["cardPackageType"]
    return (
        f'<div class="badges"><span class="badge {style}">{escape(status)}</span> '
        f'<span class="badge b-optional">{escape(package_type)}</span></div>'
    )


def download(mod: dict, prefix: str = "") -> str:
    path = local_url(mod["fileUrl"])
    return f'<a class="btn" href="{prefix}{escape(path)}" download>Download source ZIP</a>'


def checksum(mod: dict) -> str:
    return (f'<dl class="download-meta"><div><dt>Download size</dt><dd>{mod["sizeBytes"]:,} bytes</dd></div>'
            f'<div><dt>SHA-256</dt><dd><code>{escape(mod["sha256"])}</code></dd></div></dl>')


def card(mod: dict, content: dict) -> str:
    mod_id = escape(mod["id"])
    icon = escape(local_url(mod["iconUrl"]))
    description = inline(content.get("cardSummary", mod["description"]))
    return f"""      <article class="card" id="{mod_id}">
        <div class="card-head">
          <img src="{icon}" alt="" width="48" height="48" loading="lazy">
          <div><h2 class="card-title"><a class="card-link" href="mods/{mod_id}.html">{escape(mod["name"])}</a></h2>{badges(mod, content)}</div>
        </div>
        <p class="card-desc">{description}</p>
        <p class="version-line">Version {escape(mod["version"])}</p>
        {checksum(mod)}
        {download(mod)}
      </article>"""


def overview(cards: list[str]) -> str:
    return page("JAMM CGF Mods — source packages", "style.css", f"""    <header class="hero">
      <p class="eyebrow">Cat Goes Fishing · legacy VM</p>
      <h1>Build a modded game from source</h1>
      <p>JAMM combines mod packages with your own untouched August 2023 <code>data.win</code>. The downloads here contain recipes and original ModKit source, never a game archive or extracted game art.</p>
      <div class="hero-links"><a class="btn" href="how-jamm-builds.html">How a build works</a><a class="text-link" href="compatibility.html">See tested combinations →</a></div>
    </header>
    <section class="install-note" aria-labelledby="install-heading"><h2 id="install-heading">Install a package</h2>
      <ol><li>Set up JAMM with a read-only copy of the supported August 2023 legacy game archive. The current Steam YYC archive is not a compatible build base.</li>
      <li>Download a source ZIP and add it from JAMM’s <strong>Add mods</strong> flow. Keep each package enabled in the profile you want to build.</li>
      <li>Check the profile, then build or install to a separate game copy. JAMM backs up the installed original; your source archive stays untouched.</li></ol>
      <p>Package ZIPs contain source manifests, original hooks, recipes and authored assets. JAMM compiles them locally against your base game.</p>
    </section>
    <section aria-label="Available packages" class="package-section" id="packages"><div class="section-heading"><div><p class="eyebrow">The catalog</p><h2>Packages</h2></div><a href="catalog.json">Machine catalog ↗</a></div>
      <div class="grid">\n{chr(10).join(cards)}\n      </div>
    </section>
    <footer><p>Open a package page for installation notes, version history, mechanics, save behavior and compatibility details. <a href="how-jamm-builds.html">Read how JAMM builds</a>.</p></footer>""")


def paragraphs(lines: list[str]) -> str:
    return "\n".join(f"      <p>{inline(line)}</p>" for line in lines)


def items(lines: list[str], css_class: str) -> str:
    entries = "\n".join(f"        <li>{inline(line)}</li>" for line in lines)
    return f'<ul class="{css_class}">\n{entries}\n      </ul>'


def section(title: str, body: str, section_id: str = "") -> str:
    ident = f' id="{escape(section_id)}"' if section_id else ""
    return f"""    <section class="mod-section"{ident}>
      <h2>{escape(title)}</h2>
{body}
    </section>"""


def diagram(spec: dict) -> str:
    labels = spec["labels"]
    width, box_w, box_h, gap = 880, 188, 92, 30
    start = (width - (len(labels) * box_w + (len(labels) - 1) * gap)) / 2
    nodes, arrows = [], []
    for index, label in enumerate(labels):
        x = start + index * (box_w + gap)
        nodes.append(f'<g><rect x="{x:g}" y="35" width="{box_w}" height="{box_h}" rx="14"/><text x="{x+box_w/2:g}" y="{81 if len(label) < 25 else 65}" text-anchor="middle">{escape(label)}</text></g>')
        if index < len(labels) - 1:
            arrows.append(f'<path d="M {x+box_w+5:g} 81 H {x+box_w+gap-7:g}" marker-end="url(#arrow)"/>')
    mobile = "".join(f"<li>{escape(label)}</li>" for label in labels)
    return f'''<figure class="diagram"><svg viewBox="0 0 {width} 160" role="img" aria-labelledby="diag-title diag-desc"><title id="diag-title">{escape(spec["title"])}</title><desc id="diag-desc">{escape(spec["description"])}</desc><defs><marker id="arrow" markerWidth="8" markerHeight="8" refX="6" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z"/></marker></defs>{''.join(arrows)}{''.join(nodes)}</svg><ol class="diagram-mobile" aria-label="{escape(spec["title"])}">{mobile}</ol><figcaption>{escape(spec["caption"])}</figcaption></figure>'''


def datasheet(mod: dict, content: dict, manifest: dict) -> str:
    rows = [("Package type", PACKAGE_TYPES[mod["packageType"]]), ("Manifest file", f'`{mod["manifestFile"]}`'), ("Version", mod["version"])]
    if mod["packageType"] == "recipe":
        generator = f'`{manifest["generator"]}`'
        if "generatorVersion" in manifest:
            generator += f' (recipe generator version `{manifest["generatorVersion"]}`)'
        rows.append(("Generator", generator))
    rows.extend((label, content[key]) for key, label in DATASHEET_ROWS if key in content)
    row_text = "\n".join(f'<tr><th scope="row">{label}</th><td>{inline(value)}</td></tr>' for label, value in rows)
    return section("Technical facts", f'<div class="table-wrap"><table class="datasheet"><tbody>\n{row_text}\n</tbody></table></div>')


def benchmark_section(mod: dict) -> str:
    path = SITE_DIR / "content" / "benchmarks.json"
    if not path.exists():
        body = '<p class="placeholder">Measured performance data will appear here when <code>content/benchmarks.json</code> is added. No performance figures are assumed.</p>'
    else:
        payload = json.loads(path.read_text(encoding="utf-8"))
        entries = payload.get(mod["id"], []) if isinstance(payload, dict) else []
        body = paragraphs(entries) if entries else '<p class="placeholder">No measured performance entry is available for this package yet.</p>'
    return section("Measured performance", body)


def faq(lines: list[dict]) -> str:
    parts = []
    for item in lines:
        parts.append(f'<details><summary>{escape(item["q"])}</summary><p>{inline(item["a"])}</p></details>')
    return section("FAQ", '\n'.join(parts))


def detail(mod: dict, content: dict) -> str:
    mod_id = escape(mod["id"])
    name = escape(mod["name"])
    icon = escape(local_url(mod["iconUrl"]))
    with ZipFile(SITE_DIR / local_url(mod["fileUrl"])) as archive:
        manifest = json.loads(archive.read(mod["manifestFile"]))
        files = sorted(entry.filename for entry in archive.infolist() if not entry.is_dir())

    sections = [f'''    <nav class="breadcrumbs" aria-label="Breadcrumb"><a href="../index.html#packages">Packages</a><span aria-hidden="true">/</span><span>{name}</span></nav>
    <header class="mod-header">
      <a class="back" href="../index.html#packages">← All packages</a>
      <div class="card-head"><img src="../{icon}" alt="" width="52" height="52"><div><p class="eyebrow">Package detail</p><h1>{name}</h1>{badges(mod, content, detail=True)}<p>Version {escape(mod["version"])}</p></div></div>
      <div class="download-row">{download(mod, "../")}<span>Source-only ZIP · {mod["sizeBytes"]:,} bytes</span></div>
      {checksum(mod)}
    </header>''']
    sections.append(section("What it does", paragraphs([content["summary"]])))
    sections.append(section("Requirements", items(content.get("requirements", ["JAMM-CGF with a configured August 2023 legacy VM base.", "Build into a separate game copy; do not use the current Steam YYC archive as the base."]), "requirements")))
    sections.append(section("Install", items(content.get("install", ["Download this source ZIP.", "In JAMM, choose Add mods and import the ZIP.", "Enable it in a profile, check the profile, then build or install to a separate game copy."]), "install-steps")))
    if content.get("controls"):
        sections.append(section("In-game controls", items(content["controls"], "controls")))
    if content.get("gallery"):
        sections.append(gallery(mod["id"], content["gallery"]))
    sections.append(section("How it works", paragraphs(content["how"]) + (diagram(content["diagram"]) if content.get("diagram") else "")))
    if content.get("codeExample"):
        example = content["codeExample"]
        sections.append(section(example.get("title", "Manifest example"),
            f'<p>{inline(example["description"])}</p><pre><code>{escape(example["code"])}</code></pre>'))
    sections.append(datasheet(mod, content, manifest))
    sections.append(section("Version history", items(content.get("changelog", [f'{mod["version"]}: Current source package.']), "changelog")))
    sections.append(benchmark_section(mod))
    sections.append(faq(content.get("faq", [])))
    sections.append(section("Files in the package", items([f"`{filename}`" for filename in files], "package-files")))
    return page(f'{mod["name"]} — JAMM CGF Mods', "../style.css", "\n\n".join(sections), current="detail")


def guide_page(title: str, eyebrow: str, lead: str, sections_data: list[dict], stylesheet: str = "style.css") -> str:
    body = [f'<nav class="breadcrumbs" aria-label="Breadcrumb"><a href="index.html">Packages</a><span aria-hidden="true">/</span><span>{escape(title)}</span></nav>',
            f'<header class="guide-header"><p class="eyebrow">{escape(eyebrow)}</p><h1>{escape(title)}</h1><p>{escape(lead)}</p></header>']
    for item in sections_data:
        content = paragraphs(item.get("text", []))
        if item.get("list"):
            content += items(item["list"], "guide-list")
        if item.get("diagram"):
            content += diagram(item["diagram"])
        body.append(section(item["heading"], content))
    body.append('<p class="guide-back"><a href="index.html#packages">← Back to all packages</a></p>')
    return page(f'{title} — JAMM CGF Mods', stylesheet, "\n\n".join(body))


def gallery(mod_id: str, images: list[dict]) -> str:
    figures = []
    for entry in images:
        path = f'../images/{escape(mod_id)}/{escape(entry["image"])}'
        thumbnail = escape(entry["thumbnail"])
        figures.append(f'''        <figure><a href="{path}"><img src="../images/{escape(mod_id)}/{thumbnail}" loading="lazy" alt="{escape(entry["alt"])}" width="480" height="288"></a><figcaption>{escape(entry["caption"])}</figcaption></figure>''')
    return section("Screenshots", '<div class="gallery">\n' + "\n".join(figures) + '\n</div>')


def main() -> None:
    catalog = json.loads((SITE_DIR / "catalog.json").read_text(encoding="utf-8"))
    mods_dir = SITE_DIR / "mods"
    mods_dir.mkdir(exist_ok=True)
    cards = []
    for mod in catalog:
        mod_id = mod["id"]
        content = json.loads((SITE_DIR / "content" / f"{mod_id}.json").read_text(encoding="utf-8"))
        cards.append(card(mod, content))
        (mods_dir / f"{mod_id}.html").write_text(detail(mod, content), encoding="utf-8")
    (SITE_DIR / "index.html").write_text(overview(cards), encoding="utf-8")
    (SITE_DIR / "how-jamm-builds.html").write_text(guide_page(
        "How JAMM builds a modded game", "Under the hood",
        "A build starts with a read-only legacy archive, resolves selected mods against their common ancestors, applies compatibility rules, and verifies a new game archive.",
        [{"heading":"From original game to a new archive", "text":["The legacy August 2023 `data.win` is the merge ancestor and is opened read-only. A recipe or ModKit source package is compiled locally against that base into a patch; JAMM never downloads a replacement game archive.", "Profile order is explicit. JAMM combines each package's added assets and semantic changes, then runs the selected compatibility and runtime installers against the merged result. The output is written to a new path and verified before it can be installed."], "diagram":{"title":"JAMM build pipeline","description":"Original game becomes package patches, merged in profile order, compatibility rules run, then a verified output archive is written.","labels":["Read-only base","Per-mod patches","Resolve conflicts","Guards and APIs","Verify new file"],"caption":"The original data.win remains the ancestor; JAMM writes a separate verified output."}},
         {"heading":"How conflict decisions work", "text":["Each patch records changes relative to its declared ancestor. Disjoint edits can be combined. Overlapping changes to the same code or resource are reported with the affected asset and require the profile's resolution choice; the selected winner supplies the conflicting hunk while other compatible edits remain.", "Identical API placeholders and independent insertions can be retained when their meaning is known. A keep-both decision is limited to compatible code insertions. Area placement resolves `follow` anchors against the final room, uses the latest selected placement of that same landmark, and reports competing room edits as `CGF-AREA-001`.", "Final compatibility passes include object-index and instance guards such as `CGF-INSTANCE-004` (use a typed object constant when checking an object type), `CGF-INSTANCE-005` (guard eligible instance member reads/writes), and `CGF-INSTANCE-007` (guard eligible lookup-result reads). These are targeted repairs, not a proof that arbitrary dynamic IDs are always valid."], "list":["Review `cgfmm check` findings and resolve named conflicts before building.","JAMM applies shared Fish, Catalog, Items, Weather and Area runtimes only when the selected package types need them.","The verifier compiles and reads back the output archive; the original archive remains untouched."]},
         {"heading":"Anchors and shared APIs", "text":["ModKit packages add namespaced assets and initializers instead of replacing shared item tables. During the profile build, JAMM generates deterministic initializer calls in profile load order, replaces API placeholder scripts with the real runtime, and injects the required controller and save hooks.", "A recipe can patch known legacy code only when its expected unique anchors are present. If an anchor is missing or ambiguous, the build stops with the entry to inspect rather than silently patching a different location."]}]))
    (SITE_DIR / "compatibility.html").write_text(guide_page(
        "Compatibility", "Tested profiles",
        "These combinations are supported by recorded archive builds or runtime checks in the repository reports. A reported build does not guarantee every third-party mod revision behaves the same way.",
        [{"heading":"Public five-pack", "text":["The public five-pack used in the reports is Antiquity, PWB, The Abyss, Colour Expansion and Verdant. Silo and Weather Pack were built with that pack at zero pack-specific conflicts. Endless Ocean completed a route across the five-pack's extended east boundary using its seeded chunk stream."], "list":["The Silo: five-pack and all-seven archive builds passed; the Area follow anchor tracks Cave Expansion's moved and rotated MissileSilo.","Weather Pack: five-pack and all-seven builds passed with zero Weather Pack conflicts and all 53 Weather assets accepted.","Endless Ocean: the five-pack route crossed the generated boundary, streamed terrain and returned west with generated instances released."]},
         {"heading":"First-party defaults", "text":["JAMM Perf, Smooth, HD Lite and Console are documented together as a verified first-party combination. The site recipes are source packages that still build locally against the configured legacy base."], "list":["Example Fish was verified alongside Harbor Pack and Showcase Upgrades.","Example Rod uses the shared Items runtime and stable item keys, so its save ownership is independent of its current numeric slot."]},
         {"heading":"Know the limits", "text":["Compatibility is tied to the August 2023 legacy VM archive and the tested package versions. The current Steam YYC build is not a supported base. Legacy whole-archive mods can edit the same code or room entries; JAMM reports those overlaps and profile choices determine the retained changes.","The reports contain some broader all-seven runs. One Silo all-seven save run hit an existing Maize patrol crash after the four catches had been deposited; the save was independently reloaded and checked. This is recorded as a limitation of that pack combination, not a Silo package failure.","Keep boat contents and live lure interactions clear when changing profiles because some legacy save sections still use numeric object IDs. Back up saves before profile changes."]}]))


if __name__ == "__main__":
    main()
