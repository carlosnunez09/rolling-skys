#!/usr/bin/env bash
set -Eeuo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACT_NAME="${ARTIFACT_NAME:-rolling-skys-linux-server}"
IMAGE_NAME="${IMAGE_NAME:-rolling-skys-server:latest}"
CONTAINER_NAME="${CONTAINER_NAME:-rolling-skys-server}"
GAME_PORT="${GAME_PORT:-7777}"
SCENE_NAME="${SCENE_NAME:-SampleScene}"
DEPLOY_DIR="$PROJECT_DIR/deploy"
ARTIFACT_DIR="$DEPLOY_DIR/artifacts"
EXTRACT_DIR="$DEPLOY_DIR/extracted"
DOCKER_CONTEXT="$DEPLOY_DIR/docker-context/app"

json_escape() {
    python3 -c 'import json,sys; print(json.dumps(sys.stdin.read())[1:-1])'
}

fail_json() {
    local step="$1"
    local message="$2"
    local logs="${3:-}"
    printf '{\n'
    printf '  "status": "error",\n'
    printf '  "step": "%s",\n' "$(printf '%s' "$step" | json_escape)"
    printf '  "message": "%s",\n' "$(printf '%s' "$message" | json_escape)"
    printf '  "logs_tail": "%s"\n' "$(printf '%s' "$logs" | json_escape)"
    printf '}\n'
    exit 1
}

find_server_executable() {
    local candidate

    while IFS= read -r candidate; do
        [[ -d "${candidate}_Data" ]] && printf '%s\n' "$candidate" && return 0
    done < <(find "$EXTRACT_DIR" -type f \( -name "$ARTIFACT_NAME" -o -name "ServerBuild" -o -name "rolling-skys-linux-server" \))

    while IFS= read -r candidate; do
        [[ -d "${candidate}_Data" ]] && printf '%s\n' "$candidate" && return 0
    done < <(find "$EXTRACT_DIR" -type f)

    return 1
}

cd "$PROJECT_DIR"

command -v gh >/dev/null 2>&1 || fail_json "preflight" "GitHub CLI is required on the self-hosted runner."
command -v docker >/dev/null 2>&1 || fail_json "preflight" "Docker is not installed or not on PATH."
command -v python3 >/dev/null 2>&1 || fail_json "preflight" "python3 is not installed or not on PATH."
command -v tailscale >/dev/null 2>&1 || fail_json "preflight" "Tailscale is not installed or not on PATH."
command -v unzip >/dev/null 2>&1 || fail_json "preflight" "unzip is not installed or not on PATH."

docker info >/dev/null 2>&1 || fail_json "preflight" "Docker is installed but the daemon is not reachable for this user."

TAILNET_IP="$(tailscale ip -4 2>/dev/null | head -n 1)"
[[ -n "$TAILNET_IP" ]] || fail_json "preflight" "Could not determine a Tailscale IPv4 address."

[[ -n "${GITHUB_RUN_ID:-}" ]] || fail_json "preflight" "GITHUB_RUN_ID is not set."
[[ -n "${GITHUB_REPO:-}" ]] || fail_json "preflight" "GITHUB_REPO is not set."
[[ -n "${GH_TOKEN:-}" ]] || fail_json "preflight" "GH_TOKEN is not set."

rm -rf "$DEPLOY_DIR"
mkdir -p "$ARTIFACT_DIR" "$EXTRACT_DIR" "$DOCKER_CONTEXT"

if ! gh run download "$GITHUB_RUN_ID" --repo "$GITHUB_REPO" --name "$ARTIFACT_NAME" --dir "$ARTIFACT_DIR"; then
    fail_json "artifact_download" "Could not download artifact '$ARTIFACT_NAME' from run '$GITHUB_RUN_ID'."
fi

release_zip="$(find "$ARTIFACT_DIR" -type f -name '*.zip' | head -n 1)"
[[ -n "$release_zip" ]] || fail_json "artifact_download" "Downloaded artifact did not contain a release zip."

if ! unzip -q "$release_zip" -d "$EXTRACT_DIR"; then
    fail_json "artifact_extract" "Could not extract '$release_zip'."
fi

server_executable="$(find_server_executable || true)"
[[ -n "$server_executable" ]] || fail_json "artifact_extract" "Could not find a Unity Linux server executable with a sibling *_Data folder."

server_dir="$(dirname "$server_executable")"
server_binary="$(basename "$server_executable")"
cp -a "$server_dir"/. "$DOCKER_CONTEXT"/
chmod +x "$DOCKER_CONTEXT/$server_binary"

cat > "$DOCKER_CONTEXT/entrypoint.sh" <<EOF
#!/usr/bin/env bash
set -Eeuo pipefail
exec "/app/$server_binary" "\$@"
EOF
chmod +x "$DOCKER_CONTEXT/entrypoint.sh"

if ! docker build -t "$IMAGE_NAME" -f "$PROJECT_DIR/Dockerfile.server" "$PROJECT_DIR"; then
    fail_json "docker_build" "Docker image build failed."
fi

docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true

if ! docker run -d \
    --name "$CONTAINER_NAME" \
    --restart unless-stopped \
    -e ARBITRIUM_PORT_GAME_PORT_INTERNAL="$GAME_PORT" \
    -p "$TAILNET_IP:$GAME_PORT:$GAME_PORT/udp" \
    "$IMAGE_NAME" \
    -batchmode -nographics -logFile - -port "$GAME_PORT" -listen 0.0.0.0 -scene "$SCENE_NAME" >/dev/null; then
    fail_json "docker_run" "Docker container failed to start."
fi

sleep 2
if ! docker ps --filter "name=^/${CONTAINER_NAME}$" --filter "status=running" --format '{{.Names}}' | grep -qx "$CONTAINER_NAME"; then
    fail_json "verify" "Container is not running after startup." "$(docker logs --tail 120 "$CONTAINER_NAME" 2>&1 || true)"
fi

LOGS="$(docker logs --tail 80 "$CONTAINER_NAME" 2>&1 || true)"
printf '{\n'
printf '  "status": "ok",\n'
printf '  "image": "%s",\n' "$(printf '%s' "$IMAGE_NAME" | json_escape)"
printf '  "container": "%s",\n' "$(printf '%s' "$CONTAINER_NAME" | json_escape)"
printf '  "tailnet_ip": "%s",\n' "$(printf '%s' "$TAILNET_IP" | json_escape)"
printf '  "endpoint": "%s:%s",\n' "$(printf '%s' "$TAILNET_IP" | json_escape)" "$(printf '%s' "$GAME_PORT" | json_escape)"
printf '  "protocol": "udp",\n'
printf '  "logs_tail": "%s"\n' "$(printf '%s' "$LOGS" | json_escape)"
printf '}\n'
