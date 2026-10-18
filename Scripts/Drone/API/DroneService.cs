using System;
using System.Runtime.InteropServices;
using Godot;

namespace hakoniwa.drone.service
{
    /// <summary>
    /// オフライン（箱庭なし・Android では常に）の機体の物理と FC。
    ///
    /// 2026-09-29: hako_service_c（hakoniwa-drone-core）から **hakodrone（内蔵 FC ＋ MuJoCo）** に置き換えた
    /// （related/devai_dll/hakodrone_courses_migration_20260929.md の C2）。
    /// 呼び手（DroneAvatar・DroneControlRc）が使う関数の名前と、値の向き・単位は従来のまま:
    ///   * 位置: x = 前（北）・**y = 左**・z = 上 [m]（drone-core の約束。hakodrone は y = 東なのでここで符号を反転する）
    ///   * 姿勢: roll・pitch・yaw [rad]（drone-core の約束。hakodrone の度から直し、向きを合わせる）
    ///   * 操作（Put*）: drone-core の約束 **正が「後退・右・右ヨー・下降」**、値は物理量
    ///     （水平・前後 [m/s]・上下 [m/s]・ヨー [単位 ≒ 87 度/秒]。DroneControl.cs の run9 の実測）。
    ///     hakodrone の軸は [-1, 1]（前・右・右ヨー・上が +）なので、RC の上限で割って向きを合わせる
    /// 土台は education-godot-drone の EduDroneService（同じ置き換えを先に行ったもの）。
    ///
    /// 機体は hakoniwa-mujoco-drone の courses_drone（いまの courses の物理を写したもの）。
    /// res://SimModels/courses_drone/ に native.lock.json の版で置かれる（tools/fetch_native.ps1）。
    /// </summary>
    public static class DroneServiceRC
    {
        private const string DllName = "hakodrone";

        static DroneServiceRC()
        {
            Hakoniwa.Core.Utils.HakoLibLoader.Register();
        }

        /// 機体の定義の置き場と名前（hd_open_mem に中身を渡す。Godot の res:// はネイティブから開けないため）
        public const string ModelDir = "res://SimModels/courses_drone";
        private static readonly string[] ModelFiles =
            { "test_world.xml", "courses_drone.mjcf.xml", "courses_drone_meta.json", "courses_drone_actuator.json" };

        #region Structs
        [StructLayout(LayoutKind.Sequential)]
        public struct BatteryStatus
        {
            public double FullVoltage;
            public double CurrentVoltage;
            public double CurrentTemperature;   // hakodrone は温度を持たない（常に 0）
            public uint Status;                 // 0 = 使える / 1 = 使い切った
            public uint ChargeCycles;           // 持たない（常に 0）
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HdState
        {
            public double t;
            public double x, y, z;              // x = 北 / y = 東 / z = 上
            public double vx, vy, vz;
            public double roll_deg, pitch_deg, yaw_deg;
            public double battery;
            public int armed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HdBattery
        {
            public double full_voltage;
            public double voltage;
            public double nominal_voltage;
            public double remaining;
            public double soc;
            public int empty;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HdRcConfig
        {
            public double max_roll_deg, max_pitch_deg, max_yaw_rate_deg, max_vel_xy, max_climb;
            public int throttle_self_center;
            public double throttle_span, throttle_abs_max, deadzone, expo, timeout_s, lost_land_s,
                          arm_gesture_s, arm_gesture_stick, takeoff_alt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HdRcInfo
        {
            public int enabled, armed, manual, lost, mode;
            public double roll, pitch, yaw, thr;
            public double gesture;
            public int gesture_dir;
        }
        #endregion

        #region P/Invoke
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr hd_open_mem(
            IntPtr[] names, IntPtr[] texts, int n,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string world,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string meta,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string actuator,
            byte[] err, int errlen);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void hd_close(IntPtr h);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern double hd_time(IntPtr h);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_step(IntPtr h, double seconds);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_get_state(IntPtr h, int idx, out HdState st);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_get_controls(IntPtr h, int idx, double[] outv, int n);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_get_battery(IntPtr h, int idx, out HdBattery b);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_rc_enable(IntPtr h, int idx, int on);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_rc_set_mode(IntPtr h, int idx, int mode);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_rc_update(IntPtr h, int idx,
            double[] axis, int naxis, int[] button, int nbutton, int connected);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_rc_get_info(IntPtr h, int idx, out HdRcInfo info);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_rc_get_config(IntPtr h, int idx, out HdRcConfig c);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_rc_set_config(IntPtr h, int idx, ref HdRcConfig c);

        /// <summary>★ 2026-10-04（hakodrone 1.3.0）: 接触 1 回。座標は hd_state（x=北・y=東・z=上）。</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct HdContact
        {
            public double cx, cy, cz;
            public double nx, ny, nz;
            public double restitution;
            public int target_static;
            public double tvx, tvy, tvz;
            public double twx, twy, twz;
            public double tcx, tcy, tcz;
            public double tix, tiy, tiz;
            public double tmass;
        }
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hd_apply_contact(IntPtr h, int idx, ref HdContact c, double[] impulseOut);
        private static bool _contactUnsupported = false;

        /// <summary>
        /// ★★★★ 2026-10-04: オフラインでも当たり判定を効かせる（オンラインは impulse PDU で swarm_app が同じ式を解く）。
        /// `col` のベクトルは ROS（x=北・y=西・z=上）。→ hd_state の座標（y=東）に直して渡す。
        /// </summary>
        /// <returns>0 = 力積を与えた ／ 1 = 何もしなかった（離れていく途中）／ -1 = エラー・この hakodrone に口が無い</returns>
        public static int ApplyContact(int index, hakoniwa.drone.DroneImpulseCollision col)
        {
            if (_sim == IntPtr.Zero || col == null || _contactUnsupported) return -1;
            var c = new HdContact
            {
                cx = col.selfContactVector.X, cy = -col.selfContactVector.Y, cz = col.selfContactVector.Z,
                nx = col.normal.X, ny = -col.normal.Y, nz = col.normal.Z,
                restitution = col.restitutionCoefficient,
                target_static = col.isTargetStatic ? 1 : 0,
                tvx = col.targetVelocity.X, tvy = -col.targetVelocity.Y, tvz = col.targetVelocity.Z,
                twx = col.targetAngularVelocity.X, twy = -col.targetAngularVelocity.Y, twz = col.targetAngularVelocity.Z,
                tcx = col.targetContactVector.X, tcy = -col.targetContactVector.Y, tcz = col.targetContactVector.Z,
                tix = col.targetInertia.X, tiy = col.targetInertia.Y, tiz = col.targetInertia.Z,
                tmass = col.targetMass,
            };
            try
            {
                return hd_apply_contact(_sim, index, ref c, null);
            }
            catch (EntryPointNotFoundException)
            {
                // ★ hakodrone 1.2.x には無い。1 回だけ言って、以後は呼ばない（オフラインは当たり判定なしで飛ぶ）
                _contactUnsupported = true;
                GD.PushWarning("[DroneServiceRC] この hakodrone には hd_apply_contact が無い（1.3.0 から）。オフラインの当たり判定は効かない");
                return -1;
            }
            catch (Exception e) { GD.PrintErr($"[DroneServiceRC] ApplyContact: {e.Message}"); return -1; }
        }

        private static IntPtr Utf8Alloc(string s)
        {
            byte[] b = System.Text.Encoding.UTF8.GetBytes(s ?? string.Empty);
            IntPtr p = Marshal.AllocHGlobal(b.Length + 1);
            Marshal.Copy(b, 0, p, b.Length);
            Marshal.WriteByte(p, b.Length, 0);
            return p;
        }
        #endregion

        private static IntPtr _sim = IntPtr.Zero;
        public static bool IsOpen => _sim != IntPtr.Zero;

        // RC の軸（hakoniwa-mujoco-drone include/rc/rc_input.hpp）:
        //   0 = ヨー（右回りが +）／ 1 = 上下（上が +）／ 2 = 左右（右が +）／ 3 = 前後（前が +）
        // ボタン: 3 = モード切替 ／ 4 = アーム・ディスアーム（押した瞬間だけ効く）
        private const int AXIS_TURN_LR = 0, AXIS_UP_DOWN = 1, AXIS_MOVE_LR = 2, AXIS_MOVE_FB = 3;
        private const int NAXIS = 6, NBUTTON = 16;
        private const int BTN_MODE_CHANGE = 3, BTN_ARM = 4;
        private static readonly double[] _axis = new double[NAXIS];
        private static readonly int[] _button = new int[NBUTTON];
        private static readonly double[] _ctrl = new double[8];

        // ★ 手ごたえ（2026-09-30・C2）: Put* の値 1 あたり、旧版（hako_service_c）がどれだけ動いたかの**実測**。
        //   旧 DLL と hakodrone を Godot 抜きで同じ入力で比べて測った（related/devai_dll の計画 §10.2）。
        //   ★ DroneControl.cs のコメントの「87 度/秒/単位」はいまの旧 DLL と合わない（実測は約 540）。
        //   倒し切りの上限（速さ・昇降・ヨーレート）は**機体の定義（meta の fc_tuning.manual）が持つ**ので、ここでは開いたあとに読むだけ。
        public const double OldVelPerUnit = 10.8;        // 前後・左右 [m/s]／値 1（旧版は約 3.1〜3.4 m/s で頭打ち）
        public const double OldClimbPerUnit = 1.04;      // 上下 [m/s]／値 1
        public const double OldYawDegPerUnit = 540.0;    // ヨー [度/秒]／値 1
        private static double _maxVelXy = 2.9, _maxClimb = 2.8, _maxYawRateDeg = 720.0;   // Open() で機体の値に置き換わる

        #region Lifecycle
        /// <summary>
        /// 機体を開く（res://SimModels/courses_drone の中身を渡す）。RC 操縦を位置モード（GPS）で有効にする。
        /// ★ 開いた直後は FC の初期化の間アームを受け付けない（約 1 秒）。
        /// </summary>
        public static int Open()
        {
            try
            {
                Stop();
                var texts = new string[ModelFiles.Length];
                for (int i = 0; i < ModelFiles.Length; i++)
                {
                    string p = $"{ModelDir}/{ModelFiles[i]}";
                    if (!FileAccess.FileExists(p))
                    {
                        GD.PrintErr($"[DroneServiceRC] 機体の定義が無い: {p}（ビルドすると tools/fetch_native.ps1 が取ってくる）");
                        return -1;
                    }
                    using var f = FileAccess.Open(p, FileAccess.ModeFlags.Read);
                    texts[i] = f.GetAsText();
                }
                var err = new byte[256];
                var pn = new IntPtr[ModelFiles.Length];
                var pt = new IntPtr[ModelFiles.Length];
                try
                {
                    for (int i = 0; i < ModelFiles.Length; i++) { pn[i] = Utf8Alloc(ModelFiles[i]); pt[i] = Utf8Alloc(texts[i]); }
                    _sim = hd_open_mem(pn, pt, ModelFiles.Length, ModelFiles[0], ModelFiles[2], ModelFiles[3], err, err.Length);
                }
                finally
                {
                    foreach (var q in pn) if (q != IntPtr.Zero) Marshal.FreeHGlobal(q);
                    foreach (var q in pt) if (q != IntPtr.Zero) Marshal.FreeHGlobal(q);
                }
                if (_sim == IntPtr.Zero)
                {
                    GD.PrintErr("[DroneServiceRC] hd_open_mem failed: " + System.Text.Encoding.UTF8.GetString(err).TrimEnd('\0'));
                    return -1;
                }
                Array.Clear(_axis, 0, _axis.Length);
                Array.Clear(_button, 0, _button.Length);
                hd_rc_enable(_sim, 0, 1);
                hd_rc_set_mode(_sim, 0, 2);
                if (hd_rc_get_config(_sim, 0, out HdRcConfig c) == 0)
                {
                    // ★ 倒し切りの上限は機体の定義（fc_tuning.manual）の値を使う。軸への写し方（下の Put*）がそれに合わせる
                    _maxVelXy = c.max_vel_xy;
                    _maxClimb = c.max_climb;
                    _maxYawRateDeg = c.max_yaw_rate_deg;
                    c.deadzone = 0.0;   // 不感帯とカーブは courses の入力の側（gamepad_tuning.json）で掛け済み
                    c.expo = 0.0;
                    hd_rc_set_config(_sim, 0, ref c);
                }
                return 0;
            }
            catch (DllNotFoundException e) { GD.PushError($"[DroneServiceRC] DllNotFoundException: {e.Message}"); return -1; }
            catch (Exception e) { GD.PrintErr($"[DroneServiceRC] Open: {e.Message}"); return -1; }
        }

        public static int Start() => IsOpen ? 0 : -1;   // 開いた時点で走れる

        public static int Stop()
        {
            if (_sim == IntPtr.Zero) return 0;
            try { hd_close(_sim); } catch (Exception e) { GD.PrintErr($"[DroneServiceRC] Stop: {e.Message}"); }
            _sim = IntPtr.Zero;
            return 0;
        }

        /// <summary>1 ms 進める（呼び手は 1 フレームに 20 回呼ぶ）。溜めた操作をここで渡す。</summary>
        public static int Run()
        {
            if (_sim == IntPtr.Zero) return -1;
            try
            {
                hd_rc_update(_sim, 0, _axis, NAXIS, _button, NBUTTON, 1);
                return hd_step(_sim, 0.001);
            }
            catch (Exception e) { GD.PrintErr($"[DroneServiceRC] Run: {e.Message}"); return -1; }
        }

        public static ulong GetTimeUsec(int index = 0)
        {
            if (_sim == IntPtr.Zero) return 0;
            try { return (ulong)(hd_time(_sim) * 1e6); } catch { return 0; }
        }
        #endregion

        #region Telemetry
        /// <summary>位置 [m]。x = 前（北）・y = 左・z = 上（drone-core の約束）。</summary>
        public static int GetPosition(int index, out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (_sim == IntPtr.Zero) return -1;
            try
            {
                if (hd_get_state(_sim, index, out HdState s) != 0) return -1;
                x = s.x; y = -s.y; z = s.z;
                return 0;
            }
            catch { return -1; }
        }

        /// <summary>
        /// 姿勢 [rad]（drone-core の約束: x 前・y 左・z 上の右手系）。
        /// hakodrone は前・右・下（航空）の約束の度なので、ピッチ（機首上げが +）とヨー（右回りが +）の符号を反転する。
        /// </summary>
        public static int GetAttitude(int index, out double roll, out double pitch, out double yaw)
        {
            roll = pitch = yaw = 0;
            if (_sim == IntPtr.Zero) return -1;
            try
            {
                if (hd_get_state(_sim, index, out HdState s) != 0) return -1;
                const double D2R = Math.PI / 180.0;
                roll = s.roll_deg * D2R;
                pitch = -s.pitch_deg * D2R;
                yaw = -s.yaw_deg * D2R;
                return 0;
            }
            catch { return -1; }
        }

        /// <summary>機体の向きの速度 [m/s]（u = 前・v = 左・w = 上）。傾きは無視する（診断のログ用）。</summary>
        public static int GetBodyVelocity(int index, out double u, out double v, out double w)
        {
            u = v = w = 0;
            if (_sim == IntPtr.Zero) return -1;
            try
            {
                if (hd_get_state(_sim, index, out HdState s) != 0) return -1;
                double vxl = s.vx, vyl = -s.vy;              // 世界: x = 北・y = 西（左）
                double psi = -s.yaw_deg * Math.PI / 180.0;   // 左回りが +
                u = Math.Cos(psi) * vxl + Math.Sin(psi) * vyl;
                v = -Math.Sin(psi) * vxl + Math.Cos(psi) * vyl;
                w = s.vz;
                return 0;
            }
            catch { return -1; }
        }

        /// <summary>ロータへの正規化指令 [0..1]（プロペラの描画用）。4 発機なので c5〜c8 は 0。</summary>
        public static int GetControls(int index, out double c1, out double c2, out double c3, out double c4,
                                      out double c5, out double c6, out double c7, out double c8)
        {
            c1 = c2 = c3 = c4 = c5 = c6 = c7 = c8 = 0;
            if (_sim == IntPtr.Zero) return -1;
            try
            {
                int n = hd_get_controls(_sim, index, _ctrl, _ctrl.Length);
                if (n <= 0) return -1;
                c1 = _ctrl[0]; c2 = n > 1 ? _ctrl[1] : 0; c3 = n > 2 ? _ctrl[2] : 0; c4 = n > 3 ? _ctrl[3] : 0;
                c5 = n > 4 ? _ctrl[4] : 0; c6 = n > 5 ? _ctrl[5] : 0; c7 = n > 6 ? _ctrl[6] : 0; c8 = n > 7 ? _ctrl[7] : 0;
                return 0;
            }
            catch { return -1; }
        }

        /// <summary>
        /// 内部状態（LED の表示用・drone-core の番号）: 0 = 離陸（アーム済みで地上）・1 = 飛行中・3 = 停止。
        /// ★ 着陸中（2）は hakodrone からは区別しない（降りて止まれば停止になる）。
        /// </summary>
        public static int GetInternalState(int index, out int state)
        {
            state = 3;
            if (_sim == IntPtr.Zero) return -1;
            try
            {
                if (hd_get_state(_sim, index, out HdState s) != 0) return -1;
                state = s.armed == 0 ? 3 : (s.z < 0.1 ? 0 : 1);
                return 0;
            }
            catch { return -1; }
        }

        /// <summary>飛行モード: 0 = 角度（ATTI）/ 1 = 高度保持 / 2 = 位置（GPS）。courses の LED は 0 だけを ATTI と見る。</summary>
        public static int GetFlightMode(int index, out int mode)
        {
            mode = 0;
            if (_sim == IntPtr.Zero) return -1;
            try
            {
                if (hd_rc_get_info(_sim, index, out HdRcInfo i) != 0) return -1;
                mode = i.mode;
                return 0;
            }
            catch { return -1; }
        }

        public static bool TryGetBatteryStatus(int index, out BatteryStatus batteryStatus)
        {
            batteryStatus = default;
            if (_sim == IntPtr.Zero) return false;
            try
            {
                if (hd_get_battery(_sim, index, out HdBattery b) != 0) return false;
                batteryStatus.FullVoltage = b.full_voltage;
                batteryStatus.CurrentVoltage = b.voltage;
                batteryStatus.CurrentTemperature = 0.0;
                batteryStatus.Status = (uint)(b.empty != 0 ? 1 : 0);
                batteryStatus.ChargeCycles = 0;
                return true;
            }
            catch { return false; }
        }
        #endregion

        #region Control input
        private static double Clamp1(double v) => v > 1.0 ? 1.0 : (v < -1.0 ? -1.0 : v);

        // ★ 向き: drone-core の約束は **正が 後退・右・右ヨー・下降**（DroneControl.cs）。hakodrone の軸は 前・右・右ヨー・上 が +。
        //   座標系の食い違いは無い（旧版と同じ入力で同じ向きへ動くことを確かめた）。反転は前後と上下だけ。
        /// <summary>上下。正が下降。旧版と同じく値 1 で約 1.04 m/s。</summary>
        public static int PutVertical(int index, double value)   { _axis[AXIS_UP_DOWN] = Clamp1(-value * OldClimbPerUnit / _maxClimb); return 0; }
        /// <summary>左右。正が右。旧版と同じく値 1 で約 10.8 m/s（機体の上限で頭打ち）。</summary>
        public static int PutHorizontal(int index, double value) { _axis[AXIS_MOVE_LR] = Clamp1(value * OldVelPerUnit / _maxVelXy); return 0; }
        /// <summary>ヨー。正が右回り。旧版と同じく値 1 で約 540 度/秒（機体の上限で頭打ち）。</summary>
        public static int PutHeading(int index, double value)    { _axis[AXIS_TURN_LR] = Clamp1(value * OldYawDegPerUnit / _maxYawRateDeg); return 0; }
        /// <summary>前後。正が後退。旧版と同じく値 1 で約 10.8 m/s（機体の上限で頭打ち）。</summary>
        public static int PutForward(int index, double value)    { _axis[AXIS_MOVE_FB] = Clamp1(-value * OldVelPerUnit / _maxVelXy); return 0; }

        /// <summary>無線操縦ボタン → アーム・ディスアーム（押した瞬間だけ効く）。</summary>
        public static int PutRadioControlButton(int index, int value) { _button[BTN_ARM] = value != 0 ? 1 : 0; return 0; }
        public static int PutModeChangeButton(int index, int value)   { _button[BTN_MODE_CHANGE] = value != 0 ? 1 : 0; return 0; }
        #endregion
    }
}
