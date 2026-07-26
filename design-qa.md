# LumaClip Design QA

- overall source visual truth: `C:\Users\HUAWEI\.codex\generated_images\019f9dbd-139e-7152-95e1-470f7804cc59\call_2DqSAellR7Ndn6pWckZOHkUB.png`
- focused defect reference: `C:\Users\HUAWEI\AppData\Local\Temp\codex-clipboard-7d30d3b6-22e2-414f-839b-b7e7ddb859fc.png`
- implementation screenshot: `D:\codexAI\LumaClip\TestArtifacts\ui-selected-glass-no-blue.png`
- full-view comparison: `D:\codexAI\LumaClip\TestArtifacts\ui-comparison-reference-current.png`
- focused selected-card comparison: `D:\codexAI\LumaClip\TestArtifacts\ui-comparison-selected-card.png`
- viewport: 1429 × 895 logical pixels, Windows light/system theme
- state: an image history card is selected and its full preview is visible in the right inspector
- overall source pixels: 1487 × 1058
- focused source pixels: 716 × 412
- implementation pixels: 1429 × 895
- normalization: both full views were proportionally fit into equal 900 × 640 comparison cells; the focused implementation card was cropped from the native capture and proportionally fit into the same 716 × 412 cell as the defect reference

## Full-view evidence

The combined full-view board was inspected as one image. The implementation preserves the selected Apple-inspired structure: full-height sidebar, top search material, compact filters, two-column clipboard shelf, and a thicker right inspector. The native Windows title bar remains intentionally present because it is a product requirement.

## Focused evidence

The combined selected-card board was inspected as one image. The defect reference shows an accent-blue outline drawn directly on the clipping boundary and visibly broken at rounded corners. The revised card has no blue fill or blue selection stroke. The image is clipped by a dedicated inner rounded geometry, while the un-clipped outer shell supplies a neutral rim and soft elevation shadow. All four corners are visually continuous.

## Required fidelity surfaces

- Fonts and typography: Segoe UI Variable Text/Segoe UI remains the platform-appropriate San Francisco analogue. Grayscale rendering keeps text clean on translucent materials; hierarchy, wrapping, truncation, and caption weight remain coherent.
- Spacing and layout rhythm: selected cards retain the shelf grid rhythm. A small internal safe area now gives the elevation shadow room without changing the two-column structure or causing overlap.
- Colors and visual tokens: selection no longer introduces a pale-blue fill or blue line. It uses neutral glass, a white material rim already shared by unselected cards, a cool-grey diffuse shadow, a 0.8% lift scale, and a one-pixel upward offset.
- Image quality and asset fidelity: thumbnails still use high-quality bitmap scaling. The visible image is clipped inside a 15.5 px rounded geometry, independent of the outer 17 px rim, preventing fractional-DPI stroke clipping and square corner bleed.
- Copy and content: no interface copy changed. The QA capture enables sensitive-content masking so clipboard secrets are not included in the evidence.
- Icons: the existing Segoe Fluent icon family remains consistent and the favorite icon no longer conflicts with a blue selection box.
- Interaction and accessibility: hover feedback remains immediate; selected state is indicated by elevation and inspector synchronization rather than color alone. Keyboard focus visuals for actual inputs remain unchanged.

## Comparison history

### Iteration 1 — blocked

- [P1] Synthetic ambient shapes were flatter and greyer than the selected overall visual.
- [P1] Search, filters, and sidebar information order drifted from the source.
- [P2] WPF rectangular child clipping left square or jagged image corners.

Fixes: embedded the generated raster glass background, reordered the layout, rebalanced panel widths, added a true rounded clipping geometry, and refined translucent text rendering.

### Iteration 2 — passed

The prior overall comparison passed after the background, layout, and rounded geometry changes.

### Iteration 3 — blocked

- [P2] The selected history card still replaced its neutral material with pale blue and drew a one-pixel accent stroke on the same element that clipped the image. At high-DPI fractional coordinates, portions of that stroke were clipped, producing the broken blue-corner defect visible in the focused reference.

Fixes:

- removed the selected pale-blue background and accent-blue border;
- separated the outer rim from the inner image clipping surface;
- added a neutral, quality-biased diffuse shadow for selected elevation;
- added a restrained 1.008 scale and one-pixel upward lift so selection is clear without a colored rectangle.

### Iteration 4 — passed

Post-fix evidence is in `ui-comparison-selected-card.png` and `ui-comparison-reference-current.png`. The selected card has continuous rounded edges, no blue fill, no blue selection outline, and a restrained glass-elevation state. No actionable P0, P1, or P2 issue remains for this change.

## Accepted differences

- [P3] The reference omits a Windows title bar; LumaClip retains native minimize, maximize, close, snap, resize, and accessibility behavior.
- [P3] Clipboard subjects and counts are live local data, so they intentionally differ from the idealized overall mock.
- [P3] The doll and white toast visible over the right edge belong to another always-on-top application and are not rendered by LumaClip.
- [P3] Final DWM blur strength varies with Windows transparency settings and Remote Desktop; LumaClip retains its compatibility fallback.

final result: passed
