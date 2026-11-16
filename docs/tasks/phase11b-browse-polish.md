# Phase 11b — Browse polish (implementer: Codex, after Phase 11, branch ui/browse-mods)

User request (German, verbatim): "und die die man nicht downloaden kann sollen by default hidden sein. bei retry /
refresh soll einfach nur "Refresh" stehen. bei mods die nicht kompatibel sind soll es ein kleines "info" neben dem
kreuz geben wo steht wieso genau"

1. Mods without a downloadable file (previews, unsupported hosts) are hidden by default; one small toggle
   "Show mods without download" (persisted) reveals them. Counts stay honest ("10 of 24 shown").
2. Every retry/refresh/reload button in Browse says exactly "Refresh".
3. Next to the red cross of incompatible mods (and the yellow "?" when there is a reason) show a small "info"
   icon; hover/click shows the exact reason in plain words (the ModFacts reason incl. the technical detail line,
   e.g. the failing entry), selectable/copyable. Keyboard accessible.
Tests for the default filter, label, and info content. Report: docs/reports/phase11b.md.
No commits/pushes/git config.
