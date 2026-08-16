#!/usr/bin/env bash
# tools_check_scenes.bash -- 全シーンを headless で開いて、読み込みエラーを機械判定する。
#
#   bash tools_check_scenes.bash
#
# ★ 判定を機械化する（ArmPi_Ultra の教訓「グレー画面でないことまで自動判定する」）。
#   箱庭コアが無い状態で起動するので **アセット登録の失敗は正常**（それは無視する）。
#   見るのは「リソースが読めない」「スクリプトが無い」「ノードが見つからない」の 3 種。
set -u
cd "$(dirname "${BASH_SOURCE[0]}")" || exit 1
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="${DOTNET_ROOT}:${PATH}"
export LD_LIBRARY_PATH="$PWD/Plugins/Linux/x86_64:${LD_LIBRARY_PATH:-}"
GODOT="${GODOT:-/usr/local/bin/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64}"
OUT="${OUT:-/tmp/scene_check}"
mkdir -p "${OUT}"

SCENES="${*:-$(ls Scenes/*.tscn)}"
rc=0
for s in ${SCENES}; do
  name=$(basename "${s}" .tscn)
  log="${OUT}/${name}.log"
  timeout 60 "${GODOT}" --headless --path . "${s}" --quit-after 120 > "${log}" 2>&1
  # 箱庭が無いことに由来するものは除外して数える
  bad=$(grep -aE "Failed loading resource|Cannot open file|scene file .* does not exist|Can not find IHakoObject|Invalid access to property|Node not found|Attempt to open script .* failed" "${log}" \
        | grep -avE "hakoniwa|Hakoniwa|asset register|Can not register" | sort -u)
  n=$(printf '%s' "${bad}" | grep -c . || true)
  if [[ "${n}" == "0" ]]; then
    echo "  [OK]   ${name}"
  else
    echo "  [NG]   ${name}  （${n} 件）"
    printf '%s\n' "${bad}" | sed 's/^/         /' | head -8
    rc=1
  fi
done
echo
echo "ログ: ${OUT}/"
exit ${rc}
