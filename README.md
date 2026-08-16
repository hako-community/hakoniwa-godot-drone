# hakoniwa-godot-drone

Godot 4 上で動作するオープンソースの**箱庭ドローンシミュレータ基盤（Godot / C# エディション）**です。

箱庭（Hakoniwa）シミュレーションフレームワークと連携し、ドローンの物理演算・モーター推力制御・各種センサー（LiDAR, Radar, カメラ）のリアルタイム可視化およびインタラクティブな操縦環境を提供します。

---

## 主な機能と収録シーン

| シーン | パス | 主な内容・機能 |
| :--- | :--- | :--- |
| **基本ドローン** | `Scenes/drone_1.tscn` | ・標準機体 `Origin-01` による飛行シミュレーション<br>・バッテリー残量・電圧、姿勢（ロール/ピッチ）、方位マップHUD<br>・インプロセス・スタンドアロン実行対応 |
| **センサー可視化** | `Scenes/sensor_viz.tscn` | ・LiDAR 点群（PointCloud）および Radar 探知範囲のリアルタイム 3D 描画<br>・障害物・環境メッシュとの干渉可視化 |
| **複数機体回避** | `Scenes/two_drone_avoid.tscn` | ・2機のドローン（自機・僚機）による近接検知・衝突回避シミュレーション |
| **AI猫インタラクション** | `Scenes/drone_cat_1.tscn` | ・ドローンを追跡・跳躍攻撃する AI 猫（Cat モジュール）とのインタラクティブ飛行 |
| **8 発機（drone3）** | `Scenes/drone3_hako.tscn`<br>`Scenes/drone3_viz.tscn` | ・**ロータ 8 発**の大型物流ドローン `drone3` の可視化（`DronePropeller` の N 発対応の実例）<br>・`drone3_hako`: 箱庭 PDU（`Drone/pos`・`Drone/motor`）で外部の物理シミュレータから駆動<br>・`drone3_viz`: 箱庭なしでデモ駆動・ch とロータの対応づけの自動検証（`-- --selftest`） |

---

## 搭載機体モデル: `Origin-01` / `drone3`

本リポジトリでは、完全オープンソースでモジュール化された標準ドローンモデル **`Origin-01`**（`Models/origin-01/`）を採用しています。

- **モジュール構造**:
  - `origin_01_body.tscn`: メインフレーム・キャノピー
  - `propeller.tscn`: 高速回転アニメーション対応の黒色プロペラ
  - `origin_01_camera.glb` / `origin_01_lidar.glb` / `origin_01_transporter.glb`: 各種拡張センサー・アタッチメント
- **物理・スケール**:
  - 実寸大スケール（1:1）および `parts_param.json` に基づく物理プロパティ定義

`Models/drone3/` には、共同研究で扱っている**ロータ 8 発の大型物流ドローン `drone3`**（差し渡し約 3.4 m）も
収録しています。**ロータが 4 発でない機体**を扱う際の実例として利用できます
（`DronePropeller` の N 発対応・回転方向の個別指定・ch とロータの対応づけの自動検証）。
詳細は `Models/drone3/README.md`。

---

## ディレクトリ構成

```tree
hakoniwa-godot-drone/
├── Cat/                     # AI猫モジュール（追跡ロジック、3Dモデル、テストシーン）
├── Materials/               # シェーダー、LEDマテリアル、テクスチャ
├── Models/                  # 機体・アタッチメントモデル (origin-01)
├── Plugins/                 # 各OS向け箱庭ネイティブ通信ライブラリ (DLL / SO / DYLIB)
│   ├── Android/ARM64
│   ├── Linux/x86_64
│   ├── Windows/x86_64
│   └── macOS/ARM64
├── Scenes/                  # メイン実行シーン群
├── Scripts/                 # C# および GDScript スクリプト
│   ├── Drone/               # 物理衝突・プロペラ・サウンド制御
│   ├── Components/          # センサー（LiDAR/Radar）、LED、UI
│   ├── hakoniwa-pdu/        # 箱庭 PDU メッセージ定義・シリアライザ
│   └── hakoniwa-sim/        # 箱庭アセット基盤 (HakoAsset)
├── config/                  # コントローラー・機体設定パラメータ
├── ros_types/               # ROS 2 / 箱庭 PDU 型定義・オフセット
├── project.godot            # Godot プロジェクト設定
└── hakoniwa_1.csproj        # .NET / C# プロジェクト定義
```

---

## 動作環境 & 必要要件

- **Godot Engine**: `Godot 4.7-stable_mono` (C# / .NET 対応版)
- **.NET SDK**: `.NET 8.0` または `.NET 9.0`
- **サポートOS**:
  - Windows 10 / 11 (64-bit)
  - macOS (Apple Silicon / Intel)
  - Linux (Ubuntu 22.04+ 等, x86_64)
  - Android (ARM64)

---

## 使い方

### 1. プロジェクトの起動

1. 本リポジトリをクローンします：
   ```bash
   git clone https://github.com/hako-community/hakoniwa-godot-drone.git
   ```
2. Godot Engine (Mono版) を起動し、本フォルダの `project.godot` を読み込みます。
3. `Scenes/drone_1.tscn` などを開いて「プロジェクトを実行（F5）」または「現在のシーンを実行（F6）」します。

### 2. 箱庭外部制御 / シミュレーション連携

箱庭コンダクタや外部コントローラ（PX4 / ROS 2 / Python API 等）と通信連携して実行する場合は、箱庭の標準ワークフローに従ってコンダクタを起動後に Godot 側の「START」ボタンを押下してシミュレーションを開始します。

---

## ライセンス

本ソフトウェアは **[PolyForm Noncommercial License 1.0.0](LICENSE)** の下で公開されています。

- **非商用目的（研究、教育、個人での評価・学習など）**: 自由にご利用、改変、再配布が可能です。
- **商用利用（有償サービス、企業研修、商業プロダクトへの組み込み等）**: 商用ライセンスの取得が必要です。商用利用をご希望の際は、箱庭コミュニティ（Hakoniwa Community）までお問い合わせください。

Copyright (c) 2026 Hakoniwa Community.
