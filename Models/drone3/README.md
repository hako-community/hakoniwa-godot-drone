# drone3（共同研究の物流ドローン・ロータ 8 発）

大型の物流ドローン（ロータ 8 発・差し渡し約 3.4 m）の可視化用モデルとシーン。
**ロータが 4 発でない機体**の例として、`DronePropeller` の N 発対応の動作確認にも使っている。

## モデル
- 3Dモデル：`Models/drone3/drone3.glb`
  - パラメトリックな `.blend` から生成している。同じ `.blend` から
    MuJoCo 用の MJCF も生成しており、**ch 番号とロータの対応はそちらの
    `drone3_meta.json` が唯一の正**（物理・諸元は別リポジトリで管理）
- 可視化シーン
  - `Scenes/drone3_viz.tscn`  … デモ用ドライバで回す（箱庭なしで動く）
  - `Scenes/drone3_hako.tscn` … **箱庭 PDU で駆動する**（別プロセスの物理から
    `Drone/pos`・`Drone/motor` を受け取って動かす）

### ロータ N 発への対応

`DronePropeller` はロータ本数可変で、指定方法は 3 通り（上から優先）：

| 指定方法 | プロパティ | 用途 |
|---|---|---|
| NodePath 配列 | `propellers` | 任意 |
| ノード名配列 | `propellerNodeNames` + `propellerSearchRoot` | glb 内部のノード（例 `rotor_front_inner_left_spin`）を名前で引く |
| 個別指定 | `propeller1`..`propeller6` | 既存 4/6 発シーンの後方互換 |

回転方向は `spinDirections`（`+1`=CCW / `-1`=CW、上から見て）。省略時は `+,-,+,-` の交互になるため、
**drone3 のように交互でない機体は必ず明示する**こと。制御値は `Rotate(float[] controls)` で渡す。

### 対応づけの自動検証

ch 番号 → ロータノード → 回転方向 の対応は、シーンを開かずに検証できる：

```bash
export DOTNET_ROOT=$HOME/.dotnet; export PATH=$HOME/.dotnet:$PATH
GODOT=$(ls -d /usr/local/bin/Godot_v*mono*/Godot_v*mono*.x86_64 | sort -V | tail -1)   # 4.7.1 以降

$GODOT --headless --path . Scenes/drone3_viz.tscn -- --selftest              # 8 発（drone3）
$GODOT --headless --path . Scenes/propeller_legacy_selftest.tscn -- --selftest  # 4 発（後方互換）
```

1 ch ずつ指令を入れて実際の回転量を測り、`指令した ch のロータだけが` `期待した符号で` 回ったかを判定する。

### 見た目の確認

```bash
$GODOT --path . Scenes/drone3_viz.tscn --rendering-method gl_compatibility -- \
    --top-view --screenshot=/tmp/drone3_top.png --screenshot-at=2.0 --quit-after=2.4
```

`--` の後ろの引数：`--selftest` / `--sequential`（1 本ずつ回す）/ `--top-view` / `--control=<0..1>` /
`--screenshot=<path>` / `--screenshot-at=<sec>` / `--quit-after=<sec>`。

