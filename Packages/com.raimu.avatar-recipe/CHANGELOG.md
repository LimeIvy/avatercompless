# Changelog

## Unreleased

- Track and apply Renderer material assignments, Shader changes, and supported material property deltas.
- Apply Shader/property overrides through generated Avatar-specific Materials without modifying imported source Materials.
- Read schema version 1 Recipes and write schema version 2.

## 0.1.0

- Record Transform, BlendShape, Active State, and added Prefab changes.
- Scan an edited avatar against its original Prefab.
- Preview compatibility and apply supported changes with one Undo group.
- Store portable Recipe deltas in `state.json` and generate `recipe.md` on scene save.
