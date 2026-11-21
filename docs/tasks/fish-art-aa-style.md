# Task E: design note on the current CGF fish art style (branch `docs/fish-art-aa-style`, Codex Luna)

Idea from Vel (Discord): new JAMM fish should match the current Cat Goes Fishing art style (AA update, lead artist
Formex) instead of the 2023 legacy look. Write ONLY a design note `docs/design/fish-art-aa-style.md`, no code:
- what distinguishes the current style from the legacy build (outline, shading, palette, proportions, animation
  frames), judged from the user's own local Steam installation if present (YYC build; see AGENTS.md for paths)
  and the legacy base `~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`,
- what could be sampled locally at build time on the player's machine (like the existing composed sprite recipes)
  so nothing copyrighted is redistributed; what must never be shipped,
- how ModKit fish recipes/SVG parts would need to change, with a short step plan and risks.
Never commit game data, extracted sprites or screenshots of game art. Commit the note on this branch; do not push.
