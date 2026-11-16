# Phase 14 — Manager polish for normal players (implementer: Codex)

Branch `ui/polish-2` (from main). No commits/pushes/git config, no tools/ changes. Network enabled (read-only).
Gemini creates icons in parallel in `src/Jamm.Gui/Assets/ModIcons/` on another branch (`art/mod-icons`): do not
create files there; implement loading them (see 3) so they work once merged (missing files -> placeholder).

## User request (German, verbatim)
"es sollen automatisch die statusse von bereits compatible checked mods geladen werden in dem programm. [...]
der enable button soll ein ios mäßiger toggle sein, außerdem soll er auf der dropdown card sein, er soll nicht
direkt auslösen und das cgfmm build of profile everything ist unklar für jemand der keine ahnung hat das soll
bisschen simpler sein"
Current screen (orchestrator copy): `/run/media/varialbe/Festplatte 2/JAMM-CGF/.orchestrator/ui-current.png`
(rows with chevron + green "Disable" button outside the card; status panel titled "cgfmm build of profile
everything" / "Mod enabled. Press Play to apply your mods and start the game.").

## Requirements
1. Statuses load automatically: on app start (and when the catalog/base changes) fetch the shared compatibility
   index from varialbe.xyz (existing SharedCompatibility, respects the opt-out) and apply verdicts + "Adds" facts
   to every Browse card AND every "Your mods" row without any click — matching by provenance file id or by the
   mod file SHA-256 (local mods like Colour Expansion have no provenance but their hash is in the index).
   Fall back to cached local facts; only analyze locally on demand.
2. Toggle: replace the Enable/Disable button by an iOS-style switch (animated knob, green when on, grey when off,
   accessible: keyboard Space, screen-reader name "Enable <mod>") placed INSIDE the expanded card (with the summary),
   not next to the row. Switching only marks the change as pending (row shows a small "pending" dot); nothing is
   built or installed until Play. Play applies pending changes (existing guarded flow). Add "Undo changes".
3. Icons: show `Assets/ModIcons/litterbox-<id>.svg` for catalog mods, `local-<slug>.svg` for known local mods
   (abyss, cave, smooth, hd/hdlite, perf; by name/provenance), otherwise `default.svg`; render SVG in Avalonia
   (e.g. Avalonia.Svg.Skia — pinned version compatible with Avalonia 11, 0 warnings), fall back to the current
   placeholder when the file is missing. Same icons on Browse cards.
4. Plain status panel: replace "cgfmm build of profile everything" with plain language, e.g. title
   "Ready to play" / "3 changes waiting" / "Installing…" and a one-line explanation; profile names and technical
   terms only in Advanced mode or "Show details".
5. Tests for: automatic status application (hash and provenance match, opt-out), toggle pending semantics + Play
   applies + Undo, icon resolution/fallback, status texts. Report docs/reports/phase14.md.
