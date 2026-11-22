# Fish art for the current AA style

Status: design note. The goal is for new JAMM fish to sit naturally beside the current
Cat Goes Fishing fish while keeping every sampled game pixel on the player's machine.

## Evidence and limits

The legacy merge base is present at
`~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`. No current Steam
`data.win` was found in the usual native, Flatpak, or Snap Steam locations during this
review. The UndertaleModTool submodule is also absent from this worktree, so neither
archive's sprites could be rendered for a side by side visual inspection.

That means this note does not claim that the current art uses a particular outline width,
shade count, palette, body ratio, or frame cadence. Those are the comparison points to
record from the player's own current install and the local 2023 archive before changing a
recipe. “AA update” is the target named for this work; it is not evidence that every sprite
uses conventional anti-aliasing. Review at the game's native 1× scale and inspect enlarged
pixels separately so edge treatment is not confused with display scaling.

| Feature | Legacy baseline | Current AA target | What the local review should record |
| --- | --- | --- | --- |
| Outline | Inspect representative fish silhouettes and internal lines in the 2023 archive. | Not available in this worktree. | Edge colour, pixel width, whether the contour is continuous, and how outlines meet fins and mouths. |
| Shading | Inspect light direction, shadow blocks, highlights, and any texture on the same species or body type. | Not available in this worktree. | Number and shape of shade regions, highlight placement, and whether gradients or soft pixels appear. |
| Palette | Sample representative body, fin, eye, and outline colours from local pixels. | Not available in this worktree. | Saturation and value range, outline neutrality, and whether accents use the same hues. |
| Proportions | Record body length/height, head and eye scale, fin size, and tail shape. | Not available in this worktree. | Compare like-sized fish and distinguish silhouette changes from sprite scaling. |
| Animation | Count frames and inspect silhouette changes over a loop. | Not available in this worktree. | Frame count, spacing, tail/body motion, and whether shading or outlines shift consistently. |

Use several fish from more than one size class. A single example can mistake species design
for a version-wide style rule. Save notes about dimensions, frame counts, origins, and sampled
colour values, but do not save or commit extracted pixels or screenshots.

## Local sampling and distribution boundary

ModKit already supports recipes that name a base sprite with `from`, select a frame, crop it,
mask it, transform it, and combine it with original package artwork. At build time JAMM can
read those source pixels from an archive selected on the player's machine and write the
composed result into the new build. The recipe JSON and genuinely original package parts can
be distributed; sampled source pixels should exist only in memory or temporary build output.

The current Steam build is YYC, while JAMM's merge target is the 2023 VM archive. Treat these
as separate inputs: a future recipe workflow may use a locally selected current archive as
an **art source** and the legacy archive as the **build target**, but only after confirming
that the current archive can be parsed for sprite data and that its exact names, frames,
dimensions, and masks are checked against that source. Never silently substitute the art
source for the build base. If this is not technically supported, stop with a clear message
and require original artwork or an explicitly supported local source archive.

Never ship or commit:

* `data.win`, sprite exports, crops, palette sheets, sprite atlases, or copied game pixels;
* screenshots or reference sheets containing game art;
* generated PNGs whose visible pixels were derived from a game sprite, including cached
  recipe output or build intermediates.

Keep source names and crop coordinates in the recipe, and produce the composed sprite only
inside the player's local build. Do not put temporary renders under tracked example or
documentation directories. A finished local mod archive may need the composed sprite to run,
but any archive containing sampled game pixels must stay on that player's machine and must
not be uploaded or shared. The distributable source package contains the recipe and original
parts only.

## Recipe and SVG changes

The existing recipe fields cover composition, crop, alpha masks, frame arrays, palette
replacement, tone mapping, scale, and edge softness. A first AA-style fish should use those
fields only where the local comparison demonstrates a need:

* Prefer a silhouette crop from a same-version local source sprite where a vanilla body part
  is suitable. Keep the crop tight enough to avoid carrying unrelated pixels, while retaining
  the source outline intact.
* Use small, self-contained SVG details only for genuinely new anatomy. Draw their outlines
  and fills with the colours and pixel-scale proportions measured from the local comparison;
  do not trace or embed a vanilla fish silhouette in an SVG.
* Avoid global `hueShift`, `tint`, `tones`, or `edgeSoftness` as style fixes. They affect the
  whole layer and can erase the distinction between outline, shade, and highlight. Apply
  explicit palette replacements only when measured pairs preserve outline and alpha.
* If the AA target has visibly different frame motion, author an explicit `compose` frame
  array or original animation part for every frame. The recipe's first frame defines its
  collision mask, so keep the silhouette stable or validate that mask against all frames.
* Store the origin on the composed sprite and verify it at 1× in game. Avoid resizing a
  legacy part just to match the new sprite's canvas; that changes outline weight and body
  proportions together.

No new recipe syntax is justified until a locally reviewed fish demonstrates a limitation in
the existing fields. If sampling current Steam sprites requires a separate source archive,
add an explicit source selection and validation path rather than guessing names against the
legacy build.

## Short plan

1. On a machine with both installs, select the current Steam archive and the documented
   2023 legacy base independently. Confirm the current archive's sprite data can be read;
   do not write to either input.
2. Compare a small set of similar fish at 1× and enlarged pixel view. Record outline,
   shading, palette, proportions, dimensions, origins, and animation frames in review notes
   without exporting reference images.
3. Choose one JAMM fish and make a source-only recipe using local source sprite names plus
   original SVG parts. Build to a new output path and verify no source pixels were written
   into the package directory or repository.
4. Review in game beside comparable vanilla fish, then refine the recipe. Only add a ModKit
   field if this example exposes a concrete gap; document how the field avoids baking game
   pixels into distributable source.

## Risks

* **Unverified target look:** without both local archives, visual claims can be wrong. Keep
  current-style conclusions provisional until the 1× comparison is performed.
* **Archive mismatch:** current and legacy sprite names, frame layouts, origins, or masks can
  differ. Validate every reference against the declared art source and keep it separate
  from the merge target.
* **Pixel contamination:** crops, caches, screenshots, and generated output can accidentally
  enter source control or a source package. Keep them out of tracked paths and inspect the
  package contents before distribution.
* **Style drift from transforms:** rescaling, global colour shifts, and softened edges can
  make a recipe look less coherent. Prefer measured parts and local palette edits, then
  review at native scale.
* **Animation and collision mismatch:** frame variation can cause a swimming silhouette to
  outgrow the first-frame mask. Keep the outline consistent or test the mask across the loop.
