# Phase 2 development fixtures

These are deterministic test assets, not official user characters. `dev-basic` exercises Static PNG; `dev-standard` exercises sequence timing, non-loop clips and declarative Standard profiles. No behavior, emotion, AI or voice logic is executed.

Regenerate only the PNGs with `tools/Generate-CharacterFixtures.py`. This does not modify `resource/`. Manifests follow `docs/character-manifest.schema.json`. Runtime builds copy the two package folders to `DevelopmentCharacters/` as explicitly labelled development seeds.
