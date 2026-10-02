# Avatar Recipe

Avatar Recipe is an Editor-only Unity package for recording supported avatar changes and applying them to another project after a compatibility preview.

## Status

- Version: `0.1.0`
- Unity: `2022.3` or later
- Runs in the Unity Editor; it does not add runtime code to builds.
- Does not depend on the VRChat SDK.
- Editor UI supports Japanese, English, Simplified Chinese, and Korean, with automatic system language selection.
- No public VPM registry or release archive is configured yet.

## Install from source

1. Clone this repository.
2. In the destination Unity project, open **Window > Package Manager**.
3. Select **+ > Add package from disk...**.
4. Select `Packages/com.raimu.avatar-recipe/package.json` from the checkout.

Unity Package Manager will load the package from that local folder. This repository is package source, not a complete Unity project.

## Basic workflow

1. Open **Window > Avatar Recipe** and choose a Recipe Root.
2. Start Tracking from an Avatar Root, or Scan a modified avatar against its original Prefab.
3. Edit the avatar and save the Scene. Recipe files are generated on save.
4. In the destination project, import the same base Avatar and any required Prefabs.
5. Select the target Avatar and Recipe `state.json`, build Compatibility Preview, review the plan, and Apply.
6. One Undo action reverts an Apply operation.

Recipes store supported deltas and asset references. They do not contain copies of avatar or third-party assets. Unsupported hierarchy and component changes are reported for manual review.

## Development and contribution

The Unity host project, local assets, agent instructions, and internal planning documents are excluded from Git. The package source is under `Packages/com.raimu.avatar-recipe/`.

## License and commercial terms

No license or commercial terms are included yet. Define the intended license and sales terms before making a public release.
