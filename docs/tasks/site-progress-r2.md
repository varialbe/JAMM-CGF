# Round 2: progress page review (screenshots in .review/progress.png and .review/chart.png)

Branch tree — currently unreadable:
- ~60 branches, all lines bunch into one bundle at the trunk; branch labels are drawn ON the lines (strike-through look).
- Show only: open branches (not merged into main) + branches merged in the last 3 days. Everything older goes into a
  collapsed `<details>` list "Older merged branches (N)" as plain text, no graph.
- Lanes: one horizontal row per branch, trunk on the left, fork point = merge-base position on a time axis (date ticks),
  merged branches join back at their merge commit. Labels to the RIGHT of the lane end with a background so lines
  never cross text. Colour by state: open (accent), merged recently (muted green), and a small badge with
  "ahead N / behind M" and the last commit date. Max height reasonable on mobile (horizontal scroll inside the box is OK).
- Group by prefix (exp/, fix/, art/, …) with small headings.

Chart:
- One small chart PER SERIES (own heading + own x scale), not one mixed chart; y labels are the stage names only
  (series title is the heading), never truncated — wrap long labels.
- Colour by role: start = grey, intermediate = blue, latest = green, reference (label starts with "Reference") = outline.
- Replace "+43%" with an unambiguous "−43 % time · 1.76× faster" vs the first stage.
- Keep the method note.
Re-run the site tests, regenerate, screenshot with the headless chromium in ~/.cache/ms-playwright and look at it
yourself (desktop 1280 px and mobile 390 px) before committing. Commit on exp/site-progress. No merge/push/deploy.
