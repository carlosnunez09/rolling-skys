# Rolling Skys

Unity 6 game project for Rolling Skys.

## Unity editor

- Version: `6000.4.6f1`
- Open this repository root as the Unity project. The required Unity project folders are committed at the root:
  - `Assets/`
  - `Packages/`
  - `ProjectSettings/`

## First checkout setup

This project uses Git LFS for binary Unity assets. After cloning, install and hydrate LFS before opening the project:

```bash
git lfs install
git lfs pull
git lfs checkout
```

The `git lfs pull` / `git lfs checkout` steps replace LFS pointer text with the real binary assets Unity needs.

## Git rules

- Commit Unity source folders: `Assets/`, `Packages/`, and `ProjectSettings/`.
- Do not commit generated folders such as `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, `Logs/`, or `UserSettings/`.
- Keep `.meta` files with their assets.
- Import third-party packs into `Assets/ThirdParty/` and commit extracted Unity assets, not raw archives, unless the archive must be preserved.
- Large binary assets are handled by `.gitattributes` and Git LFS.

More detail: [`docs/UNITY_GIT.md`](docs/UNITY_GIT.md).
