#!/usr/bin/env bash
# Build Baballonia.Desktop INSIDE the Steam Runtime 4 SDK — same environment the
# CI build job uses (.github/workflows/build.yml). Produces the normal
# self-contained publish so scripts/steamrt4-run.sh can launch it.
#
# Usage: scripts/steamrt4-build.sh [linux-x64|linux-arm64]   (default linux-x64)
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

ARCH="${1:-linux-x64}"
case "$ARCH" in
  linux-x64)   BASE="registry.gitlab.steamos.cloud/steamrt/steamrt4/sdk:latest" ;;
  linux-arm64) BASE="registry.gitlab.steamos.cloud/steamrt/steamrt4/sdk/arm64:latest" ;;  # native; needs an aarch64 host or qemu
  *) echo "usage: $0 [linux-x64|linux-arm64]" >&2; exit 2 ;;
esac
LOCAL_IMG="baballonia-steamrt4-sdk:dotnet10-${ARCH}"

# If this shell predates being added to the 'docker' group, re-enter with it active.
if ! docker info >/dev/null 2>&1; then
  if [ -z "${_BB_SG:-}" ] && getent group docker | grep -qw "${USER:-$(id -un)}"; then
    export _BB_SG=1; exec sg docker -c "$(printf '%q ' "$0" "$@")"
  fi
  echo "Can't reach the docker daemon. Start it with: sudo systemctl start docker" >&2
  echo "and make sure you're in the 'docker' group (sudo usermod -aG docker \$USER, then re-login)." >&2
  exit 1
fi

# One-time: bake .NET 10 into the SDK image so repeat builds skip the install.
if ! docker image inspect "$LOCAL_IMG" >/dev/null 2>&1; then
  echo ">> building $LOCAL_IMG (one-time, FROM $BASE) ..."
  docker build -t "$LOCAL_IMG" - <<DOCKER
FROM $BASE
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates xz-utils patchelf \
 && rm -rf /var/lib/apt/lists/*
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/d.sh \
 && bash /tmp/d.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && rm /tmp/d.sh
ENV PATH=/usr/share/dotnet:\$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
DOCKER
fi

mkdir -p "$HOME/.nuget/packages"

# The BabbleTrainer calibration binary is a gitignored symlink into a local
# PyInstaller build that lives OUTSIDE the repo. The csproj copies it during
# publish, but the container only mounts $PWD, so the symlink dangles and the
# copy fails (MSB3021). Bind-mount the link target at its own absolute path so
# it resolves inside the container. No-op when the symlink/target is absent.
TRAINER_LINK="src/Baballonia.Desktop/Calibration/Linux/Trainer/BabbleTrainer"
TRAINER_MOUNT=()
if [ -L "$TRAINER_LINK" ]; then
  TRAINER_TGT="$(readlink "$TRAINER_LINK")"
  if [ -e "$TRAINER_TGT" ]; then
    TRAINER_MOUNT=(-v "$TRAINER_TGT":"$TRAINER_TGT":ro)
  else
    echo ">> warning: $TRAINER_LINK -> $TRAINER_TGT is missing; publish will fail on the copy." >&2
  fi
fi

echo ">> publishing $ARCH in the steamrt4 SDK ..."
# --user keeps build outputs owned by you (not root); the host NuGet cache is
# reused so restore is fast.
docker run --rm \
  --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e NUGET_PACKAGES=/nuget \
  -v "$HOME/.nuget/packages":/nuget \
  -v "$PWD":/src -w /src \
  "${TRAINER_MOUNT[@]}" \
  "$LOCAL_IMG" \
  dotnet publish src/Baballonia.Desktop/Baballonia.Desktop.csproj \
    -r "$ARCH" -c Release --self-contained -f net10.0

echo ">> done -> src/Baballonia.Desktop/bin/Release/net10.0/$ARCH/publish"
