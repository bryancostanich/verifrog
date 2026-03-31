#!/usr/bin/env bash
#
# install.sh — Make verifrog commands available on your PATH.
#
# Options:
#   --symlink    Create symlinks in /usr/local/bin (default, requires sudo on some systems)
#   --profile    Add verifrog/bin to PATH in your shell profile instead
#   --uninstall  Remove symlinks from /usr/local/bin
#
# Usage:
#   ./install.sh              # Symlink to /usr/local/bin
#   ./install.sh --profile    # Add to PATH in ~/.zshrc or ~/.bashrc
#   ./install.sh --uninstall  # Remove symlinks

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN_DIR="$SCRIPT_DIR/bin"
INSTALL_DIR="/usr/local/bin"

SCRIPTS=("verifrog" "verifrog-vcd")

die() { echo "Error: $*" >&2; exit 1; }
info() { echo "  $*"; }

do_symlink() {
    echo "Installing verifrog to $INSTALL_DIR..."
    for script in "${SCRIPTS[@]}"; do
        local src="$BIN_DIR/$script"
        local dst="$INSTALL_DIR/$script"
        [[ -f "$src" ]] || die "Script not found: $src"
        if [[ -L "$dst" || -f "$dst" ]]; then
            info "Replacing existing $dst"
            rm -f "$dst" 2>/dev/null || sudo rm -f "$dst"
        fi
        ln -s "$src" "$dst" 2>/dev/null || sudo ln -s "$src" "$dst"
        info "Linked $dst -> $src"
    done
    echo ""
    echo "Done. Run 'verifrog --help' to verify."
}

do_profile() {
    # Detect shell profile
    local profile=""
    if [[ -n "${ZSH_VERSION:-}" ]] || [[ "$SHELL" == */zsh ]]; then
        profile="$HOME/.zshrc"
    elif [[ -n "${BASH_VERSION:-}" ]] || [[ "$SHELL" == */bash ]]; then
        profile="$HOME/.bashrc"
    fi

    if [[ -z "$profile" ]]; then
        die "Could not detect shell profile. Add this to your shell config manually:
  export PATH=\"$BIN_DIR:\$PATH\""
    fi

    local line="export PATH=\"$BIN_DIR:\$PATH\"  # verifrog"

    if grep -qF "# verifrog" "$profile" 2>/dev/null; then
        info "verifrog PATH entry already exists in $profile"
    else
        echo "" >> "$profile"
        echo "$line" >> "$profile"
        info "Added to $profile:"
        info "  $line"
        echo ""
        echo "Restart your shell or run:"
        echo "  source $profile"
    fi
}

do_uninstall() {
    echo "Removing verifrog from $INSTALL_DIR..."
    for script in "${SCRIPTS[@]}"; do
        local dst="$INSTALL_DIR/$script"
        if [[ -L "$dst" || -f "$dst" ]]; then
            rm -f "$dst" 2>/dev/null || sudo rm -f "$dst"
            info "Removed $dst"
        else
            info "$dst not found (skipping)"
        fi
    done
    echo "Done."
}

# ---- Main ----

case "${1:---symlink}" in
    --symlink)    do_symlink ;;
    --profile)    do_profile ;;
    --uninstall)  do_uninstall ;;
    -h|--help)
        echo "Usage: ./install.sh [--symlink | --profile | --uninstall]"
        echo ""
        echo "  --symlink    Symlink verifrog scripts to /usr/local/bin (default)"
        echo "  --profile    Add bin/ to PATH in shell profile"
        echo "  --uninstall  Remove symlinks from /usr/local/bin"
        ;;
    *)
        die "Unknown option: $1"
        ;;
esac
