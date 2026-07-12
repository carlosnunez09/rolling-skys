# Networking Setup — Rolling Skys

Unity Netcode for GameObjects (NGO) with Unity Transport (UDP port **7777**).

## Pieces already in the project

| Piece | Location |
|---|---|
| Network bootstrap | `Assets/Scenes/MainMenu.unity` (`networkmanager` object) |
| Session coordinator | `Assets/scripts/Gameplay/GameNetworkManager.cs` |
| Menu UI | `Assets/scripts/Gameplay/MainMenuNetworkUI.cs` |
| Player prefab | `Assets/Prefabs/CarNetworked.prefab` |
| Prefab list | `Assets/DefaultNetworkPrefabs.asset` |
| Game scene | `Assets/Scenes/SampleScene.unity` (spawn points + `RaceRuntime`) |
| Linux server CI/Docker | `.github/workflows/unity-build.yml`, `Dockerfile.server`, `scripts/deploy-server.sh` |

## Play in the Unity Editor (server + client)

### Option A — Host (listen server) + extra clients

1. Open `Assets/Scenes/MainMenu.unity`.
2. Press Play.
3. Click **Host** on the main instance.
4. Open **Window → Multiplayer → Multiplayer Play Mode**, activate a Virtual Player, and click **Client** there (address `127.0.0.1:7777`).

### Option B — Dedicated server role + client role

1. Confirm **Project Settings → Multiplayer → Enable Multiplayer Roles** is on.
2. In the editor toolbar Multiplayer Role dropdown, choose **Server**, press Play — the menu hides and `GameNetworkManager` calls `StartServer()`.
3. Use Multiplayer Play Mode (or a second editor instance) with role **Client**, press Play, click **Client** (or enable `Connect Automatically` on `GameNetworkManager`).

### Option C — Menu buttons only

On Main Menu:

- **Host** — listen server + local player
- **Server** — dedicated server (no local player car ownership UI)
- **Client** — connects to the Address field (`host:port`)
- **Disconnect** — shuts the session down

## Builds

| Target | How |
|---|---|
| Windows client | CI matrix `StandaloneWindows64` / tag `v*` |
| Linux dedicated server | CI matrix `StandaloneLinux64` + `-standaloneBuildSubtarget Server` |
| Local Linux server | Unity menu / `ServerBuild.BuildLinuxServer()` → `Builds/DockerServer/ServerBuild` |

Dedicated server builds define `UNITY_SERVER`, so `GameNetworkManager` auto-starts the server and loads `SampleScene`. Command-line overrides:

```text
-port 7777 -listen 0.0.0.0 -address 127.0.0.1 -scene SampleScene
```

Edgegap also sets `ARBITRIUM_PORT_GAME_PORT_INTERNAL`.

## Checklist if clients cannot connect

1. Server/host is listening (`GameNetworkManager` status / logs).
2. Client address is reachable (`127.0.0.1:7777` locally, Tailscale/Edgegap endpoint remotely).
3. UDP **7777** is open (Docker maps UDP).
4. Both peers use the same Netcode prefab list / build.
5. `CarNetworked` is assigned on `GameNetworkManager` and listed in `DefaultNetworkPrefabs`.
