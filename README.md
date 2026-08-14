# hakoniwa-godot-drone
このリポジトリでは、Godot上で箱庭ドローンの物理モデルをビジュアライズ・操作できる環境を提供します。

## 使い方
箱庭コアと連携動作するライブラリ類は自動的に読まないので、hakoniwa-godot-droneディレクトリの直下に
プラグインのディレクトリからコピーしてください。

### プラグインのディレクトリ構成

クロスプラットフォーム対応は、[hakoniwa-unity-drone](https://github.com/hakoniwalab/hakoniwa-unity-drone)と同様です。ディレクトリ構成は違っています。hakoniwa-unity-droneは、一部の共有ライブラリが`Unity Package Manager`で管理されていますが、hakoniwa-godot-droneは、addonでの管理がされていないため必要なライブラリを配置しています。

```tree
Plugins/
├── Android
│   └── ARM64
├── Linux
│   └── x86_64
├── Windows
│   └── x86_64
└── macOS
    └── ARM64
```

## 箱庭ドローンシミュレータ Godot版の利用方法

インストール、操作方法は、以下のドキュメントを公開していますので、参照してください。

[箱庭ドローンシミュレータ Godot版 操作方法](https://github.com/buildko89/documents/blob/main/hakodoc/howto-doc/hakowithgodot.md)

## 動作確認Version

- Windows

  - Godot_v4.6-stable_mono

- Mac

  - Windowsと同様4.6


## ディレクトリ構成

このプロジェクトのディレクトリ構成は以下のとおりです：

```tree
├── Cat            # AI猫モジュール（後述: scenes / scripts / モデル）
├── Materials
├── Models
├── Plugins
├── Scripts
│   ├── ARBridge
│   ├── Drone
│   ├── Hakoniwa
│   │   ├── Device
│   │   ├── HakoPdu
│   │   └── HakoSim
│   ├── Interfaces
│   ├── Componets
│   ├── hakoniwa-pdu
│   └── hakoniwa-sim
├── addons
├── config
└── ros_types
[その他設定ファイル類]
```

## 利用環境

ドローンの基本的な飛行テストができる[hakoniwa-unity-drone](https://github.com/hakoniwalab/hakoniwa-unity-drone)のGodot環境への移植版になります。

# AI猫によるドローン追跡（Cat モジュール）

`drone_cat_1.tscn` は、基本シーン `drone_1.tscn` を複製し **AI制御の猫** を追加したシーンです。猫が箱庭ドローンを追跡し、間合いに入ると跳躍/前足パンチで一撃を狙います。

## 構成

| 要素 | 場所 | 役割 |
|---|---|---|
| シーン | `Scenes/drone_cat_1.tscn` | ドローン箱庭＋AI猫（`drone_1.tscn` は無改変で温存） |
| 猫本体 | `Cat/scenes/CatController.tscn`（`Cat/scripts/cat_controller.gd`） | 移動・アニメ・ジャンプ物理。入力/AIを知らない「意図API」だけを公開 |
| AIブレイン | `Cat/scripts/cat_drone_hunter.gd`（`CatHunter` ノード） | ドローンを追跡→跳躍/パンチ。意図APIを駆動 |
| モデル | `Cat/p3_koha9face.glb` ＋ テクスチャ | 猫の3Dモデル（毛シェル・アルファ） |
| デモ飛行 | `Cat/scripts/drone_demo_pilot.gd`（`DroneDemoPilot`） | コンダクタ無しでドローンを自動飛行させる（実サーバ時は `enabled=false`） |
| 単体テスト | `Cat/scenes/HuntTest.tscn` | 実ドローン/コンダクタ不要でAI挙動を確認 |

## 動作

- **STARTボタン連携**：シミュレータ開始（`HakoSimState.Running`）前は **お座り** で待機。STARTで **ゆっくり歩き出し**、その後は **近くは歩き / 遠くは走り** で追跡します。
- **攻撃**：ドローン高度が低ければ地上パンチ、跳んで届く高さなら跳躍。接触すると `drone_hit` シグナルを発火します（ドローンを落とす「撃墜」連携は今後対応）。

## レンダラ

猫の毛/アルファ表現のため、プロジェクトのレンダラを **Forward+** に設定しています（`project.godot`）。※ Quest スタンドアロン等モバイル向けに動かす場合は Mobile レンダラが別途必要です。

## 実行方法

- **実シーン**：箱庭コンダクタを起動した上で `drone_cat_1.tscn` を Play。STARTボタンで猫が動き出します。
- **AI単体確認（コンダクタ不要）**：`Cat/scenes/HuntTest.tscn` を「現在のシーンを実行(F6)」。ダミードローンを猫が追います。

## 調整

Inspector で調整できます。詳細は **[`Cat/cat_ai_tuning.md`](Cat/cat_ai_tuning.md)** を参照：

- `CatHunter`：`run_distance`（走り出す距離）/ `jump_reach`（跳ぶ高度）/ `start_walk_time`（START直後の歩き出し秒）/ `wait_for_sim_start` など
- `Cat`：`walk_speed` / `run_speed` / `walk_anim_speed`（すり足対策）など

## レーダー可視化の無効化

`DroneAvatar` の `[Export] enableRadarVisualizer` を `false` にすると、自動生成されるレーダー点群/FOVコーンの可視化を消せます（`drone_cat_1` では false 設定済み）。

## 対応しているCollider

未対応…

## AR対応

未対応…(OpenXRベースで対応予定)

