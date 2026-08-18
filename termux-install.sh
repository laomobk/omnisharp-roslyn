#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

if [[ -z "${PREFIX:-}" || ! -d "$PREFIX" || ! -d "$PREFIX/bin" ]]; then
    echo "This script must run inside Termux (PREFIX is not set)." >&2
    exit 1
fi

cd -- "$script_dir"

dotnet_bin="$(command -v dotnet || true)"
if [[ -z "$dotnet_bin" ]]; then
    echo "dotnet was not found. Install a Termux .NET 8 SDK first." >&2
    exit 1
fi

sdk_version="$(dotnet --version)"
if [[ "$sdk_version" != 8.0.* ]]; then
    echo "This source requires a .NET 8 SDK; dotnet reports '$sdk_version'." >&2
    exit 1
fi

if ! dotnet --list-runtimes | grep -qE '^Microsoft\.NETCore\.App 8\.0\.'; then
    echo "Microsoft.NETCore.App 8.0.x is not installed." >&2
    exit 1
fi

dotnet_real="$dotnet_bin"
if command -v readlink >/dev/null 2>&1; then
    resolved_dotnet="$(readlink -f "$dotnet_bin" 2>/dev/null || true)"
    if [[ -n "$resolved_dotnet" && -x "$resolved_dotnet" ]]; then
        dotnet_real="$resolved_dotnet"
    fi
fi

dotnet_root=""
if [[ -d "$PREFIX/lib/dotnet/sdk" ]]; then
    dotnet_root="$PREFIX/lib/dotnet"
else
    dotnet_dir="$(dirname -- "$dotnet_real")"
    if [[ -d "$dotnet_dir/sdk" ]]; then
        dotnet_root="$dotnet_dir"
    fi
fi

if [[ -z "$dotnet_root" ]]; then
    echo "Could not locate the .NET SDK directory. Expected '$PREFIX/lib/dotnet/sdk'." >&2
    exit 1
fi

export DOTNET_ROOT="$dotnet_root"
export DOTNET_ROLL_FORWARD=LatestMinor

omnisharp_home="$PREFIX/lib/omnisharp"
wrapper_path="$PREFIX/bin/omnisharp-termux"
rm -rf -- "$omnisharp_home"
mkdir -p -- "$omnisharp_home"

publish_args=(
    publish
    src/OmniSharp.Stdio.Driver/OmniSharp.Stdio.Driver.csproj
    --configuration Release
    --framework net8.0
    --self-contained false
    -p:PublishReadyToRun=false
    -p:UseAppHost=false
    -p:RollForward=LatestMinor
    -p:NuGetAudit=false
    --output "$omnisharp_home"
)

case "$(uname -m)" in
    aarch64|arm64)
        echo "Publishing linux-bionic-arm64..."
        if ! dotnet "${publish_args[@]:0:4}" --runtime linux-bionic-arm64 "${publish_args[@]:4}"; then
            echo "Bionic runtime assets were unavailable; retrying without a runtime identifier..."
            rm -rf -- "$omnisharp_home"
            mkdir -p -- "$omnisharp_home"
            dotnet "${publish_args[@]}"
        fi
        ;;
    *)
        echo "Architecture $(uname -m) has no configured Bionic RID; publishing a portable build."
        dotnet "${publish_args[@]}"
        ;;
esac

if [[ ! -f "$omnisharp_home/OmniSharp.dll" ]]; then
    echo "Publish completed without OmniSharp.dll." >&2
    exit 1
fi

cat > "$wrapper_path" <<EOF
#!/usr/bin/env bash
export DOTNET_ROOT="$dotnet_root"
export DOTNET_ROLL_FORWARD=LatestMinor
exec "$dotnet_real" "$omnisharp_home/OmniSharp.dll" "\$@"
EOF
chmod +x -- "$wrapper_path"

echo "Installed OmniSharp at $omnisharp_home"
echo "Launcher: $wrapper_path"
echo
"$wrapper_path" --help >/dev/null
echo "Smoke test passed. Add this to ~/.vimrc:"
echo "let g:OmniSharp_server_path = '$wrapper_path'"
echo "let g:OmniSharp_server_stdio = 1"
echo
echo "Open a project from its directory, then open a .cs file in Vim."
