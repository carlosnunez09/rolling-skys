# Unity Git setup

This repository is configured as a Unity project at the repository root.

## Project layout

Unity should be opened from the repository root, not from a nested folder.

Required project folders:

- `Assets/` - game source, scenes, prefabs, art, scripts, settings assets, and `.meta` files
- `Packages/` - Unity package manifest, lock file, and embedded packages
- `ProjectSettings/` - Unity project settings and editor version metadata

Generated local folders are ignored by `.gitignore`, including `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, `Logs/`, and `UserSettings/`.

## LFS hydrate/decompress step

Binary art and package files are stored through Git LFS. A fresh checkout may contain pointer files until LFS is hydrated:

```bash
git lfs install
git lfs pull
git lfs checkout
```

Run these commands before opening Unity. If Unity imports a texture, model, font, PDF, DLL, terrain data, or baked lighting asset as a small text file beginning with `version https://git-lfs.github.com/spec/v1`, rerun the hydrate commands above.

## What goes in Git

Commit:

- Unity scene, prefab, material, animation, shader, and script files
- All matching `.meta` files
- `Packages/manifest.json` and `Packages/packages-lock.json`
- Project settings under `ProjectSettings/`
- Binary assets tracked by Git LFS

Do not commit:

- `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, `Logs/`, `UserSettings/`
- Local IDE files and generated solution/project files
- Unity recovery snapshots under `Assets/_Recovery/`
- Raw imported archives when the extracted assets are already committed

## Importing compressed packs

When adding a `.unitypackage`, `.zip`, `.rar`, `.7z`, `.tar`, or similar pack:

1. Decompress/import the pack into the intended folder, usually `Assets/ThirdParty/<PackageName>/`.
2. Keep the generated `.meta` files with the imported assets.
3. Commit the extracted/imported files.
4. Only keep the original archive if it is required for licensing or redistribution; `.gitattributes` stores such archives through LFS.

## LFS coverage

`.gitattributes` keeps Unity YAML-style files mergeable while putting large binary content in LFS. This includes common model, image, audio, video, font, archive, DLL, baked lighting, and terrain data files.
