#!/usr/bin/env bash
# fetch_native.sh -- native.lock.json に書いた版のネイティブ一式（libhakodrone.so など）を releases-channel から取ってきて置く。
#                    tools/fetch_native.ps1 の Linux 版（同じロック・同じ目印・同じ置き方）。
#
#   bash tools/fetch_native.sh [--force]
#
# ビルドの前に自動で呼ばれる（.csproj の FetchNativeLinux）。手で呼んでもよい。
#   1. 置き場の目印（<dest>/.native.lock.<名前>.json）がロックと同じで、ファイルが揃っていれば何もしない
#   2. gh release download <tag> -R <repo> -p <asset>（非公開なので gh auth login 済みが前提）
#   3. zip の sha256 をロックと照合。違えば止める
#   4. 展開して置く（既定は dest。place にファイルごとの置き場を書ける）。目印を書く
#
# ロックの項目は "os" でどの OS 向けかを決める（"windows" ／ "linux"。無ければ "windows"）。
# この OS の項目だけを扱う。"_" で始まる名前は注記として読み飛ばす。
#
#   HAKO_FETCH_NATIVE_ZIP=<zip>  公開の前に手元の zip で試す（取りに行かず、sha256 の照合はする）
#   HAKO_SKIP_FETCH_NATIVE=1     取得を飛ばす（認証の無い CI など）
set -euo pipefail

say() { echo "[fetch_native] $*"; }
if [[ "${HAKO_SKIP_FETCH_NATIVE:-}" == 1 ]]; then say 'HAKO_SKIP_FETCH_NATIVE=1 なので取得を飛ばす'; exit 0; fi

FORCE=0
[[ "${1:-}" == --force ]] && FORCE=1
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(git -C "${HERE}" rev-parse --show-toplevel 2>/dev/null || (cd "${HERE}/.." && pwd))"
case "$(uname -s)" in
  Linux) OS=linux ;;
  Darwin) OS=macos ;;
  *) say "この OS（$(uname -s)）は fetch_native.ps1 を使う"; exit 0 ;;
esac
command -v python3 >/dev/null || { say 'NG: python3 が無い'; exit 1; }

exec python3 -B - "${ROOT}" "${OS}" "${FORCE}" <<'PY'
import hashlib, json, os, shutil, subprocess, sys, tempfile, zipfile
from pathlib import Path

root, osname, force = Path(sys.argv[1]).resolve(), sys.argv[2], sys.argv[3] == "1"
def say(m): print(f"[fetch_native] {m}", flush=True)
def fail(m): print(f"[fetch_native] NG: {m}", flush=True); sys.exit(1)

lock_path = root / "native.lock.json"
if not lock_path.exists():
    say(f"ロックが無い（{lock_path}）。何もしない"); sys.exit(0)
lock = json.loads(lock_path.read_text(encoding="utf-8-sig"))

n = 0
for name, e in lock.items():
    if name.startswith("_"):
        continue
    if (e.get("os") or "windows").lower() != osname:
        continue
    n += 1
    for k in ("repo", "tag", "asset", "sha256", "dest"):
        if not e.get(k):
            fail(f"{name} に {k} が無い（{lock_path}）")
    sha = e["sha256"].lower()
    dest = root / e["dest"]
    place = dict(e.get("place") or {})
    marker = dest / f".native.lock.{name}.json"
    place_key = ";".join(f"{k}={place[k]}" for k in sorted(place))   # fetch_native.ps1 と同じ形

    # ---- 1. 目印が合っていてファイルが揃っていれば何もしない
    if not force and marker.exists():
        try:
            have = json.loads(marker.read_text(encoding="utf-8-sig"))
        except ValueError:
            have = {}
        same = have.get("tag") == e["tag"] and have.get("sha256") == sha and have.get("place", "") == place_key
        files = have.get("files") or []
        if isinstance(files, str):
            files = [files]
        if same and all((root / f).exists() for f in files):
            say(f"{name} {e['tag']} は取得済み")
            continue

    tmp = Path(tempfile.mkdtemp(prefix="fetch_native_"))
    try:
        # ---- 2. 取ってくる
        zp = tmp / e["asset"]
        local = os.environ.get("HAKO_FETCH_NATIVE_ZIP")
        if local:
            say(f"{name} を手元の zip から置く（HAKO_FETCH_NATIVE_ZIP={local}）")
            shutil.copyfile(local, zp)
        else:
            if not shutil.which("gh"):
                fail("gh（GitHub CLI）が無い。入れて認証してから建て直す: sudo apt install gh → gh auth login")
            say(f"{name} {e['tag']} を {e['repo']} から取ってくる")
            rc = subprocess.call(["gh", "release", "download", e["tag"], "-R", e["repo"], "-p", e["asset"], "-D", str(tmp)])
            if rc != 0:
                fail(f"取得に失敗（gh の終了コード {rc}）。gh auth login 済みか、{e['repo']} を読む権限があるかを確かめる。"
                     "取らずに建てるなら HAKO_SKIP_FETCH_NATIVE=1")

        # ---- 3. sha256 を照合
        got = hashlib.sha256(zp.read_bytes()).hexdigest()
        if got != sha:
            fail(f"sha256 がロックと違う（取れたもの {got} ／ ロック {sha}）。止める")

        # ---- 4. 展開して置く（置き方は fetch_native.ps1 と同じ）
        #   ・place に「ファイル名」があれば、そのフォルダ
        #   ・place に「フォルダ/」があれば、その下のファイルを指定のフォルダへ（中の階層は保つ）
        #   ・zip の直下のファイルは dest。どの place にも当たらないフォルダの中身は置かない
        prefixes = sorted((p for p in place if p.endswith("/")), key=len, reverse=True)
        dest.mkdir(parents=True, exist_ok=True)
        placed = []
        version = ""
        with zipfile.ZipFile(zp) as z:
            for info in z.infolist():
                if info.is_dir():
                    continue
                in_zip = info.filename.replace("\\", "/").lstrip("/")
                to_dir = None
                if in_zip in place:
                    to_dir = root / place[in_zip]
                else:
                    for p in prefixes:
                        if in_zip.startswith(p):
                            sub = os.path.dirname(in_zip[len(p):])
                            to_dir = root / place[p] / sub if sub else root / place[p]
                            break
                    if to_dir is None and "/" not in in_zip:
                        to_dir = dest
                if in_zip == "manifest.json":
                    version = json.loads(z.read(info)).get("version", "")
                if to_dir is None:
                    continue
                to_dir.mkdir(parents=True, exist_ok=True)
                out = to_dir / os.path.basename(in_zip)
                with z.open(info) as src, open(out, "wb") as dst:
                    shutil.copyfileobj(src, dst)
                mode = (info.external_attr >> 16) & 0o777
                if mode:
                    out.chmod(mode | 0o600)
                placed.append(out.relative_to(root).as_posix())
        marker.write_text(json.dumps({"tag": e["tag"], "sha256": sha, "place": place_key, "files": placed},
                                     ensure_ascii=False, indent=2), encoding="utf-8")
        say(f"{name} {e['tag']} を置いた（{len(placed)} ファイル{' ・版 ' + version if version else ''}）")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

if n == 0:
    say(f"ロックに {osname} の項目が無い。何もしない")
PY
