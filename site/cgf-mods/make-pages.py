#!/usr/bin/env python3
"""Render the CGF mod overview and detail pages from the catalog and package content."""

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
    ("compatibility", "Compatibility"),
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


def page(title: str, stylesheet: str, body: str) -> str:
    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{escape(title)}</title>
  <link rel="stylesheet" href="{stylesheet}">
</head>
<body>
  <main>
{body}
  </main>
</body>
</html>
"""


def badges(mod: dict, content: dict, detail: bool = False) -> str:
    if mod["isDefault"]:
        status, style = "First-Party Default", "b-default"
    elif "ModKit" in mod["categories"] and not detail:
        status, style = "Test ModKit Mod", "b-test"
    elif content.get("preview") and not detail:
        status, style = "Preview", "b-optional"
    else:
        status, style = "Optional", "b-optional"
    package_type = PACKAGE_TYPES[mod["packageType"]] if detail else content["cardPackageType"]
    return (
        f'<div class="badges"><span class="badge {style}">{status}</span> '
        f'<span class="badge b-optional">{escape(package_type)}</span></div>'
    )


def download(mod: dict, prefix: str = "") -> str:
    path = local_url(mod["fileUrl"])
    return f'<a class="btn" href="{prefix}{escape(path)}" download>Download {escape(Path(path).name)}</a>'


def checksum(mod: dict) -> str:
    return f'<div class="checksum">SHA-256: {escape(mod["sha256"])} ({mod["sizeBytes"]:,} B)</div>'


def card(mod: dict, content: dict) -> str:
    mod_id = escape(mod["id"])
    icon = escape(local_url(mod["iconUrl"]))
    description = inline(content.get("cardSummary", mod["description"]))
    return f"""      <article class="card" id="{mod_id}">
        <div class="card-head">
          <img src="{icon}" alt="" width="44" height="44">
          <div>
            <h2 class="card-title"><a class="card-link" href="mods/{mod_id}.html">{escape(mod["name"])}</a></h2>
            {badges(mod, content)}
          </div>
        </div>
        <p class="card-desc">{description}</p>
        {checksum(mod)}
        {download(mod)}
      </article>"""


def overview(cards: list[str]) -> str:
    card_text = "\n\n".join(cards)
    return page("JAMM CGF Mods — Official Pure-Source Packages", "style.css", f"""    <header>
      <h1>JAMM CGF Mods</h1>
      <p>
        Pure-source packages and recipes for <strong>Cat Goes Fishing</strong> (built locally against your own untouched legacy 2023 <code>data.win</code> via JAMM-CGF).
        No game archives, copied sprites, or extracted GML are hosted or redistributed here.
      </p>
      <div class="meta-bar">
        <span>Machine catalog: <a href="catalog.json"><code>catalog.json</code></a></span>
        <span>Format: <code>jamm-recipe.json</code> / <code>jamm-mod.json</code> ZIP</span>
        <span>Import: Browse tab or <code>cgfmm add &lt;package.zip&gt;</code></span>
      </div>
    </header>

    <section aria-label="Available packages" class="grid">
{card_text}
    </section>

    <footer>
      <p>
        All packages contain only original manifests, recipes, art, audio, and GML hooks authored for JAMM-CGF.
        When imported, JAMM compiles each package locally against your own read-only <code>data.win</code>.
      </p>
    </footer>""")


def paragraphs(lines: list[str]) -> str:
    return "\n".join(f"      <p>{inline(line)}</p>" for line in lines)


def items(lines: list[str], css_class: str) -> str:
    entries = "\n".join(f"        <li>{inline(line)}</li>" for line in lines)
    return f'      <ul class="{css_class}">\n{entries}\n      </ul>'


def section(title: str, body: str) -> str:
    return f"""    <section class="mod-section">
      <h2>{title}</h2>
{body}
    </section>"""


def gallery(mod_id: str, images: list[dict]) -> str:
    figures = []
    for entry in images:
        path = f'../images/{escape(mod_id)}/{escape(entry["image"])}'
        thumbnail = escape(entry["thumbnail"])
        figures.append(f"""        <figure>
          <a href="{path}"><img src="../images/{escape(mod_id)}/{thumbnail}" loading="lazy" alt="{escape(entry["alt"])}"></a>
          <figcaption>{escape(entry["caption"])}</figcaption>
        </figure>""")
    figure_text = "\n".join(figures)
    return section("Gallery", f'      <div class="gallery">\n{figure_text}\n      </div>')


def datasheet(mod: dict, content: dict, manifest: dict) -> str:
    rows = [
        ("Package type", PACKAGE_TYPES[mod["packageType"]]),
        ("Manifest file", f'`{mod["manifestFile"]}`'),
    ]
    if mod["packageType"] == "recipe":
        generator = f'`{manifest["generator"]}`'
        if "generatorVersion" in manifest:
            generator += f' (recipe generator version `{manifest["generatorVersion"]}`)'
        rows.append(("Generator", generator))
    rows.extend((label, content[key]) for key, label in DATASHEET_ROWS if key in content)
    row_text = "\n".join(
        f'          <tr><th scope="row">{label}</th><td>{inline(value)}</td></tr>'
        for label, value in rows
    )
    return section("Technical datasheet", f"""      <table class="datasheet">
        <tbody>
{row_text}
        </tbody>
      </table>""")


def detail(mod: dict, content: dict) -> str:
    mod_id = escape(mod["id"])
    name = escape(mod["name"])
    icon = escape(local_url(mod["iconUrl"]))
    with ZipFile(SITE_DIR / local_url(mod["fileUrl"])) as archive:
        manifest = json.loads(archive.read(mod["manifestFile"]))
        files = sorted(entry.filename for entry in archive.infolist() if not entry.is_dir())

    sections = [f"""    <header class="mod-header">
      <a class="back" href="../index.html#{mod_id}">← All mods</a>
      <div class="card-head">
        <img src="../{icon}" alt="" width="44" height="44">
        <div>
          <h1>{name}</h1>
          {badges(mod, content, detail=True)}
          <p>Version {escape(mod["version"])}</p>
        </div>
      </div>
      {download(mod, "../")}
      {checksum(mod)}
    </header>"""]
    sections.append(section("What it does", paragraphs([content["summary"]])))
    if content.get("controls"):
        sections.append(section("Controls", items(content["controls"], "controls")))
    if content.get("gallery"):
        sections.append(gallery(mod["id"], content["gallery"]))
    sections.append(datasheet(mod, content, manifest))
    sections.append(section("How it works", paragraphs(content["how"])))
    sections.append(section("Files in the package", items([f"`{name}`" for name in files], "package-files")))
    return page(f'{mod["name"]} — JAMM CGF Mods', "../style.css", "\n\n".join(sections))


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


if __name__ == "__main__":
    main()
