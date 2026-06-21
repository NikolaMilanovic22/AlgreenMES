#!/usr/bin/env bash
# One-time setup after cloning. Points git at our tracked hooks
# directory (.githooks/) so pre-commit checks fire on every commit.
# Safe to re-run — git config is idempotent.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

git config core.hooksPath .githooks
echo "✓ git hooks → .githooks/"

# Sanity check: make sure the pre-commit hook is executable. chmod is a
# no-op on most checkouts but git on Windows can drop the executable bit.
if [ -f .githooks/pre-commit ] && [ ! -x .githooks/pre-commit ]; then
  chmod +x .githooks/pre-commit
  echo "✓ chmod +x .githooks/pre-commit"
fi

echo
echo "Done. Next commit will run BE static checks (.githooks/pre-commit)."
