---
topic: scene-spatial-facts
default_location: scene
sources: [project docs/data-statement.md, project docs/method-terrain-from-dem.md]
purpose: "Facts about THIS Unity scene specifically, so the guide's statements match what the player actually sees."
---

# This scene — spatial ground truth

## gz-scene-01 — What the player is standing on
> location: scene · confidence: established · sources: docs/data-statement.md

The terrain in this experience is built from a real drone-photogrammetry survey of Great Zimbabwe by Daniel Löwenborg and Ezekia Mtetwa of Uppsala University (Zenodo, DOI 10.5281/zenodo.19093686, CC BY 4.0). The ground is a 19 cm-per-pixel elevation model draped with a 4.8 cm-per-pixel aerial photograph — the actual site, at real scale, not an artist's impression.

## gz-scene-02 — What the scene covers
> location: scene · confidence: established · sources: docs/data-statement.md

The playable terrain covers about 1,133 × 1,203 meters of the site — the Valley Complex and the Great Enclosure area. The Hill Complex appears only along its southern face at the northern edge of the terrain. Ground elevation in the scene runs from about 984 to 1,181 meters above sea level, roughly 197 meters of relief.

## gz-scene-03 — Honest limits of the recreation
> location: scene · confidence: established · sources: docs/data-statement.md

The survey data is a surface model: standing walls and vegetation are baked into the terrain shape rather than modeled as separate objects, and about 13% of the elevation data consists of gaps filled by interpolation. The guide should describe the real site's features confidently but should not invent details of spots the survey did not capture (most of the Hill Complex interior, for example).

## gz-scene-04 — Credit line (required attribution)
> location: scene · confidence: established · sources: docs/data-statement.md

Terrain and aerial imagery: "Great Zimbabwe, DEM and orthophoto" by Daniel Löwenborg and Ezekia Mtetwa (Uppsala University), Zenodo, doi:10.5281/zenodo.19093686, licensed CC BY 4.0; cropped, hole-filled, resampled, and converted to a game-engine terrain. This credit must appear in the application's credits screen.
