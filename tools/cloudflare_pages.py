#!/usr/bin/env python3
"""Stage a Unity WebGL build for Git-connected Cloudflare hosting, or verify it.

Unity runs locally. The Cloudflare build command runs ``verify`` against the committed
static player and refuses a stale or incomplete source/build pair.
"""

import hashlib
import json
import shutil
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
UNITY_OUTPUT = ROOT / "Build" / "Web"
PAGES_OUTPUT = ROOT / "deploy" / "site"
MANIFEST = ROOT / "deploy" / "site-manifest.json"
SOURCE_ROOTS = ("Assets", "Packages", "ProjectSettings")
REQUIRED_ASSETS = (
    "index.html",
    "_headers",
    "Build/Web.loader.js",
    "Build/Web.data.unityweb",
    "Build/Web.framework.js.unityweb",
    "Build/Web.wasm.unityweb",
)
MAX_ASSET_BYTES = 25 * 1024 * 1024


def file_hash(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def source_hash():
    files = [ROOT / name for name in SOURCE_ROOTS]
    files = [file for root in files for file in root.rglob("*") if file.is_file() and file.name != ".DS_Store"]
    files.append(ROOT / "deploy" / "_headers")
    digest = hashlib.sha256()
    for path in sorted(files):
        digest.update(path.relative_to(ROOT).as_posix().encode("utf-8"))
        digest.update(b"\0")
        digest.update(bytes.fromhex(file_hash(path)))
    return digest.hexdigest()


def assets_in(directory):
    if not directory.is_dir():
        raise ValueError(f"Missing WebGL output: {directory}")
    files = {p.relative_to(directory).as_posix(): p for p in directory.rglob("*") if p.is_file()}
    missing = set(REQUIRED_ASSETS) - set(files)
    if missing:
        raise ValueError("Missing WebGL assets: " + ", ".join(sorted(missing)))
    for name, path in files.items():
        if path.stat().st_size > MAX_ASSET_BYTES:
            raise ValueError(f"Asset exceeds Cloudflare's 25 MiB static asset limit: {name}")
    if files["_headers"].read_bytes() != (ROOT / "deploy" / "_headers").read_bytes():
        raise ValueError("Staged _headers differs from deploy/_headers")
    index = files["index.html"].read_text(encoding="utf-8")
    for name in REQUIRED_ASSETS[2:]:
        if name.rsplit("/", 1)[-1] not in index:
            raise ValueError(f"index.html does not reference {name}")
    return {name: file_hash(path) for name, path in sorted(files.items())}


def stage():
    assets_in(UNITY_OUTPUT)
    if PAGES_OUTPUT.exists():
        shutil.rmtree(PAGES_OUTPUT)
    shutil.copytree(UNITY_OUTPUT, PAGES_OUTPUT)
    manifest = {"source_sha256": source_hash(), "assets_sha256": assets_in(PAGES_OUTPUT)}
    MANIFEST.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Staged {len(manifest['assets_sha256'])} WebGL files in {PAGES_OUTPUT.relative_to(ROOT)}")


def verify():
    if not MANIFEST.is_file():
        raise ValueError("Missing deploy/site-manifest.json; run stage after the Unity Web build")
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    if manifest.get("source_sha256") != source_hash():
        raise ValueError("Unity source changed since staging; rebuild WebGL and run stage again")
    if manifest.get("assets_sha256") != assets_in(PAGES_OUTPUT):
        raise ValueError("Staged WebGL files changed or are incomplete; run stage again")
    print(f"Verified {len(manifest['assets_sha256'])} staged WebGL files for Cloudflare")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2 or sys.argv[1] not in ("stage", "verify"):
            raise ValueError("Usage: python3 tools/cloudflare_pages.py {stage|verify}")
        (stage if sys.argv[1] == "stage" else verify)()
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"Cloudflare preparation failed: {error}", file=sys.stderr)
        sys.exit(1)
