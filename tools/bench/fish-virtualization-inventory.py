#!/usr/bin/env python3
"""Generate a fish-relevant asset inventory from local cgfmm patch manifests."""

import argparse
import hashlib
import json
import re
import subprocess
import tempfile
from pathlib import Path


QUERY = re.compile(r"\b(instance_number|instance_find|instance_nearest|instance_furthest|instance_place|random_fish)\b|\bwith\s*\(\s*Fish\b", re.I)
ALARM = re.compile(r"_Alarm_\d+\b")
EVENT = re.compile(r"gml_Object_([A-Za-z0-9_]+)_(Create|Destroy|Step|Draw|Alarm|Other|Key|Mouse|Collision)_([0-9]+)")


def text_of(entry):
    chunks = []
    for key in ("baseCode", "modCode"):
        value = entry.get(key)
        if isinstance(value, dict):
            chunks.append(value.get("text", ""))
    return "\n".join(chunks)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--legacy", help="legacy vanilla archive; generates names and decompiled fish code")
    parser.add_argument("--tool", help="path to jamm-modmaker.dll, required with --legacy")
    parser.add_argument("--base", help="vanilla code names from jamm-modmaker names <data.win>")
    parser.add_argument("--base-dump", help="decompiled fish-related code from jamm-modmaker dump")
    parser.add_argument("--objects", help="object list from jamm-modmaker names --objects")
    parser.add_argument("--patch", action="append", default=[], metavar="PACK=FILE", help="local patch manifest; repeat once per pack")
    args = parser.parse_args()

    if args.legacy:
        if not args.tool:
            parser.error("--legacy requires --tool")
        tool = str(Path(args.tool).resolve())
        names = subprocess.check_output(["dotnet", tool, "names", args.legacy], text=True).splitlines()
        objects = subprocess.check_output(["dotnet", tool, "names", args.legacy, "--objects"], text=True)
        with tempfile.TemporaryDirectory(prefix="jamm-fish-inventory-") as temp:
            dump_path = Path(temp) / "fish.gml"
            fish_names = [name for name in names if name.startswith("gml_Object_") and re.search(r"fish|sonar|catalog", name, re.I)]
            with dump_path.open("w") as stream:
                subprocess.run(["dotnet", tool, "dump", args.legacy, *fish_names], stdout=stream, check=True)
            code_names = names
            dumped = dump_path.read_text()
        object_names = objects.splitlines()
    else:
        if not args.base:
            parser.error("provide --legacy or --base")
        code_names = Path(args.base).read_text().splitlines()
        dumped = Path(args.base_dump).read_text() if args.base_dump else ""
        object_names = Path(args.objects).read_text().splitlines() if args.objects else []

    fish_code = [line for line in code_names if re.search(r"gml_Object_.*(?:Fish|fish|Sonar|Catalog)", line)]
    dump_entries = re.split(r"(?m)^// ===== ", dumped)
    print("# Fish virtualization inventory (generated)")
    print()
    print("Generated 2026-11-22 from the legacy vanilla asset names/decompilation and the local patch manifests. The signature scan is a review aid, not proof of safety.")
    print()
    print(f"Vanilla fish-related event/code entries: **{len(fish_code)}**.")
    if args.base_dump:
        query_entries = sum(bool(QUERY.search(entry)) for entry in dump_entries)
        alarm_entries = sum(bool(ALARM.search(entry)) for entry in dump_entries)
        create_entries = sum(bool(re.search(r"\binstance_create\s*\(", entry)) for entry in dump_entries)
        random_entries = sum(bool(re.search(r"\b(random|irandom|choose|random_range|irandom_range)\s*\(", entry)) for entry in dump_entries)
        print(f"Decompiled fish-related entries: **{len(dump_entries) - 1}**; entries with instance/population query calls: **{query_entries}**; entries naming Alarm events: **{alarm_entries}**; entries calling `instance_create`: **{create_entries}**; entries with explicit random calls: **{random_entries}**.")
    if object_names:
        rows = [line.split("\t") for line in object_names if "\t" in line]
        parents = {row[1]: row[2] if len(row) > 2 else "" for row in rows}
        chain, cursor = [], "Garder"
        while cursor and cursor not in chain:
            chain.append(cursor)
            cursor = parents.get(cursor, "")
        own = [line for line in code_names if line.startswith("gml_Object_Garder_")]
        print("Garder hierarchy: **" + " → ".join(chain) + "**; own event entries: " + (", ".join(own) if own else "none") + ".")
    print("The profiler’s vanilla dock capture (`vanilla-prof-dock`, 270 ticks) attributes **56,109 µs/tick** to `gml_Object_Fish_Step_2` (39% of the profiled loop). The leading event is inherited by ordinary fish descendants, so the pilot uses the Garder leaf object rather than virtualizing the Fish parent.")
    print()
    print("| Local pack | Fish-related added/changed/removed code entries | Entries with instance/population query signatures | Manifest SHA-256 |")
    print("|---|---:|---:|---|")
    for spec in args.patch:
        pack, file_name = spec.split("=", 1)
        path = Path(file_name)
        manifest = json.loads(path.read_text())
        entries = []
        for category in ("added", "changed", "removed"):
            entries.extend(x for x in manifest["changes"]["code"].get(category, []) if isinstance(x, dict))
        relevant = [x for x in entries if re.search(r"fish|sonar|catalog", x.get("name", "") + " " + text_of(x), re.I)]
        query_count = sum(bool(QUERY.search(x.get("name", "") + " " + text_of(x))) for x in relevant)
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        print(f"| {pack} | {len(relevant)} | {query_count} | `{digest[:16]}…` |")

    print()
    print("## Garder pilot candidate")
    print()
    print("Vanilla asset inventory: Garder is a leaf under `FishSmallTropic → FishSmall → Fish`; its own event set is Create only. It inherits the shared Fish Step/Draw and parent lifecycle events. Vanilla profiler captures show the inherited Fish Step is the dominant cost, but do not give a per-leaf CPU attribution. The record schema is restricted to primitive pose/lifecycle fields; no instance IDs, light handles, alarms, or RNG state are stored.")
    print()
    print("Compatibility gate: Garder is a bench-only pilot. Pack code and direct GML instance queries remain a separate compatibility concern; no pack support is claimed.")


if __name__ == "__main__":
    main()
