using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Collections.Generic;
using Godot;
using System.IO;

namespace Hakoniwa.Core.Utils
{
    // ネイティブライブラリ（hakodrone・箱庭コア）の読み込み口。
    //
    // 探す順番（2026-09-29・DLL 配布 P2。計画: related/devai_dll/hakodrone_dll_distribution_20260927.md §5.4）:
    //   1. 環境変数 HAKO_NATIVE_LIB_DIR のフォルダ（; 区切りで複数可。前から順に）
    //      開発中に hakoniwa-mujoco-drone の publish（devai_drone/publish/Windows/x86_64）を
    //      コピーもコミットもせずに読むための口。未設定なら従来どおり。
    //   2. Plugins/{OS}/{arch}/（同梱物）
    //
    // ★ hakodrone.dll と mujoco.dll は必ず同じフォルダから読む。
    //   Windows は同じ名前の DLL を 1 プロセスに 1 つしか読めないので、mujoco.dll を Plugins から
    //   先に読んだあとで hakodrone.dll だけ別のフォルダから読むと、hakodrone は隣の mujoco.dll ではなく
    //   先に読んだほうに繋がる。そのため mujoco.dll の事前読み込みも hakodrone.dll と同じ規則で場所を決める。
    //
    // ★ このファイルは related の Godot リポジトリで同じ内容を保つ（計画 L-5）。リポジトリ固有のことを書かない。
    public static class HakoLibLoader
    {
        public const string NativeLibDirEnv = "HAKO_NATIVE_LIB_DIR";
        // このプロジェクトの C# 宣言（EduDroneService など）が前提にしている hakodrone の関数の形の版。
        // hakodrone.h の HD_ABI_VERSION と同じ値。hakodrone 側で上がったら、宣言を直してからここを上げる。
        public const int ExpectedHakodroneAbi = 1;

        private static bool isRegistered = false;
        private static readonly Dictionary<string, IntPtr> loadedHandles = new Dictionary<string, IntPtr>();

        /// 読んだ hakodrone の版（hd_version()）。読む前・版を持たない古い DLL では null。
        public static string HakodroneVersion { get; private set; }
        /// 読んだ hakodrone の関数の形の版（hd_abi_version()）。読む前・古い DLL では -1。
        public static int HakodroneAbi { get; private set; } = -1;
        /// hakodrone.dll を読んだフォルダ。
        public static string HakodroneDir { get; private set; }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr HdVersionFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int HdAbiVersionFn();

        public static void Register()
        {
            if (isRegistered) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string winPluginDir = PluginDir();
                if (winPluginDir != null && Directory.Exists(winPluginDir))
                {
                    // 箱庭コアなどの依存 DLL の探索先（従来どおり Plugins）
                    SetDllDirectory(winPluginDir);
                }

                // mujoco.dll / glfw3.dll の事前読み込みは、hakodrone.dll を読むフォルダから行う（上の ★）
                string hdDir = FindDirWith(NativeFileName("hakodrone"));
                if (hdDir != null)
                {
                    foreach (string dep in new[] { "mujoco.dll", "glfw3.dll" })
                    {
                        string p = Path.Combine(hdDir, dep);
                        if (File.Exists(p))
                        {
                            try { NativeLibrary.Load(p); }
                            catch (Exception e) { GD.PrintErr($"HakoLibLoader: {p} を読めない: {e.Message}"); }
                        }
                        else if (dep == "mujoco.dll")
                        {
                            GD.PrintErr($"HakoLibLoader: {hdDir} に hakodrone.dll はあるが mujoco.dll が無い（組で置くこと）");
                        }
                    }
                }
            }

            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ResolveDllImport);
            isRegistered = true;
            string env = System.Environment.GetEnvironmentVariable(NativeLibDirEnv);
            GD.Print(string.IsNullOrEmpty(env)
                ? "HakoLibLoader: DllImportResolver registered."
                : $"HakoLibLoader: DllImportResolver registered ({NativeLibDirEnv}={env}).");
        }

        private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (loadedHandles.TryGetValue(libraryName, out IntPtr cachedHandle))
            {
                return cachedHandle;
            }

            // 対応するライブラリ名かチェック
            if (IsHakoLibrary(libraryName))
            {
                // Android: APK のネイティブライブラリの場所（lib/arm64-v8a）から標準の名前で読む
                // （dlopen はそこしか探さない。Plugins/Android/ARM64 は APK を作るときに詰めるだけ）。
                // hakodrone は libmujoco.so に依存するので、先に libmujoco.so を読んでおく（hakoniwa-godot-drone-courses から取り込んだ枝）。
                if (OperatingSystem.IsAndroid())
                {
                    string soName = NativeFileName(libraryName);
                    try
                    {
                        if (libraryName.Contains("hakodrone"))
                        {
                            try { NativeLibrary.Load("libmujoco.so"); }
                            catch (Exception e) { GD.PrintErr($"HakoLibLoader: libmujoco.so を読めない: {e.Message}"); }
                        }
                        IntPtr ah = NativeLibrary.Load(soName);
                        GD.Print($"HakoLibLoader: Loaded {libraryName} on Android ({soName})");
                        loadedHandles[libraryName] = ah;
                        if (libraryName.Contains("hakodrone")) ReportHakodroneVersion(ah, soName);
                        return ah;
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"HakoLibLoader: Failed to load {libraryName} on Android ({soName}): {ex.Message}");
                        return IntPtr.Zero;
                    }
                }

                string path = GetLibraryPath(libraryName);
                if (path != null && File.Exists(path))
                {
                    GD.Print($"HakoLibLoader: Loading {libraryName} from {path}");
                    IntPtr handle = NativeLibrary.Load(path);
                    if (handle != IntPtr.Zero)
                    {
                        loadedHandles[libraryName] = handle;
                        if (libraryName.Contains("hakodrone"))
                        {
                            HakodroneDir = Path.GetDirectoryName(path);
                            ReportHakodroneVersion(handle, path);
                        }
                        return handle;
                    }
                }
                GD.PrintErr($"HakoLibLoader: Could not find library {libraryName} at {path}");
            }
            return IntPtr.Zero;
        }

        // 読んだ直後に版をログへ出す（計画 L-4）。「古い DLL が黙って動く」を見えるようにする。
        // ★ hd_version / hd_abi_version は 2026-09-29 に足した関数なので、それより前の DLL には無い。
        //   無くても読み込みは止めない（今の同梱物がまさにそれ）。関数の形の版が合わないときだけ強く警告する。
        private static void ReportHakodroneVersion(IntPtr handle, string path)
        {
            HakodroneVersion = null;
            HakodroneAbi = -1;
            try
            {
                if (NativeLibrary.TryGetExport(handle, "hd_version", out IntPtr pv))
                {
                    var fn = Marshal.GetDelegateForFunctionPointer<HdVersionFn>(pv);
                    HakodroneVersion = Marshal.PtrToStringUTF8(fn());
                }
                if (NativeLibrary.TryGetExport(handle, "hd_abi_version", out IntPtr pa))
                {
                    var fn = Marshal.GetDelegateForFunctionPointer<HdAbiVersionFn>(pa);
                    HakodroneAbi = fn();
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"HakoLibLoader: hakodrone の版を読めない: {e.Message}");
            }

            if (HakodroneVersion == null)
            {
                GD.PushWarning($"HakoLibLoader: hakodrone {path} は版を持たない（2026-09-29 より前の DLL）。" +
                               "物理・FC の中身がどの版か分からない。native.lock.json があれば、ビルドし直すと取り直す。");
                return;
            }
            GD.Print($"HakoLibLoader: hakodrone {HakodroneVersion}（ABI {HakodroneAbi}）を {Path.GetDirectoryName(path)} から読んだ");
            if (HakodroneAbi != ExpectedHakodroneAbi)
            {
                GD.PushError($"HakoLibLoader: hakodrone の関数の形の版が違う（DLL {HakodroneAbi} ／ このプロジェクト {ExpectedHakodroneAbi}）。" +
                             "C# の宣言と DLL がずれているので、値が化けるか落ちる。DLL か宣言を合わせること。");
            }

            // ロック（native.lock.json）の版と比べる（計画 L-4）。ロックが無い（エクスポート後など）ときは比べない。
            string locked = LockedVersion("hakodrone");
            if (locked == null) return;
            string loaded = HakodroneVersion.Split('+')[0].Split('-')[0];   // "1.0.0-dev+abc" → "1.0.0"
            string pluginDir = PluginDir();
            // ★ Android は名前だけで読む（フォルダが無い）＝ 同梱物を読んでいる
            bool fromPlugins = string.IsNullOrEmpty(Path.GetDirectoryName(path)) || pluginDir != null &&
                               string.Equals(Path.GetFullPath(Path.GetDirectoryName(path)).TrimEnd('\\', '/'),
                                             Path.GetFullPath(pluginDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            if (!fromPlugins)
            {
                GD.Print($"HakoLibLoader: 開発中の hakodrone を読んでいる（ロックは {locked}）");
            }
            else if (loaded != locked)
            {
                GD.PushWarning($"HakoLibLoader: Plugins の hakodrone（{HakodroneVersion}）がロックの版（{locked}）と違う。" +
                               "ビルドし直すと tools/fetch_native.ps1 が取り直す。");
            }
        }

        // native.lock.json（プロジェクトのルート）に書いた版。無ければ null。
        private static string LockedVersion(string name)
        {
            try
            {
                string lockPath = Path.Combine(Directory.GetCurrentDirectory(), "native.lock.json");
                if (!File.Exists(lockPath)) return null;
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockPath));
                if (doc.RootElement.TryGetProperty(name, out var e) && e.TryGetProperty("version", out var v))
                {
                    return v.GetString();
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"HakoLibLoader: native.lock.json を読めない: {e.Message}");
            }
            return null;
        }

        private static bool IsHakoLibrary(string libraryName)
        {
            // ★★★★ EDU7（2026-08-30）: **内蔵 FC（hakodrone）を足す。**
            //   ★★★ Windows では `SetDllDirectory` のおかげで既定の探索に引っかかって
            //     動いてしまうが、**Linux / macOS には同じ助けが無い** ——
            //     ここに名前が無いと `Plugins/{OS}/{arch}/` を見に行かず、
            //     .NET の実行時ディレクトリを探して「共有ライブラリを読めない」になる。
            //     ★★ 実際に Linux の headless 実行で捕まえた（Windows では露呈しない形）。
            // ★ C6（2026-10-03）: hako_service_c は消した（オフラインの物理は hakodrone だけ）。
            return libraryName.Contains("hakodrone") ||
                   libraryName.Contains("shakoc") ||
                   libraryName.Contains("conductor");
        }

        private static string GetLibraryPath(string libraryName)
        {
            string fileName = NativeFileName(libraryName);
            if (fileName == null) return null;

            // HAKO_NATIVE_LIB_DIR を先に見る。そこに無いものは従来どおり Plugins から読む。
            string dir = FindDirWith(fileName);
            if (dir != null) return Path.Combine(dir, fileName);
            string plugin = PluginDir();
            return plugin == null ? null : Path.Combine(plugin, fileName);
        }

        // HAKO_NATIVE_LIB_DIR（; 区切り）→ Plugins の順に、fileName があるフォルダを返す。どこにも無ければ null。
        private static string FindDirWith(string fileName)
        {
            if (fileName == null) return null;
            string env = System.Environment.GetEnvironmentVariable(NativeLibDirEnv);
            if (!string.IsNullOrEmpty(env))
            {
                foreach (string raw in env.Split(';'))
                {
                    string d = raw.Trim();
                    if (d.Length == 0) continue;
                    if (File.Exists(Path.Combine(d, fileName))) return d;
                }
            }
            string plugin = PluginDir();
            if (plugin != null && File.Exists(Path.Combine(plugin, fileName))) return plugin;
            return null;
        }

        // Plugins/{OS}/{arch}/（Godot プロジェクトの実行時のカレントディレクトリが基準）
        private static string PluginDir()
        {
            string osDir;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) osDir = "Windows";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) osDir = "Linux";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) osDir = "macOS";
            else return null;

            string archDir;
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X64: archDir = "x86_64"; break;
                case Architecture.Arm64: archDir = "ARM64"; break;
                default: return null;
            }
            return Path.Combine(Directory.GetCurrentDirectory(), "Plugins", osDir, archDir);
        }

        // ライブラリ名 → この OS でのファイル名（hakodrone → hakodrone.dll ／ libhakodrone.so ／ libhakodrone.dylib）
        private static string NativeFileName(string libraryName)
        {
            string prefix = "lib";
            string extension = ".so";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { prefix = ""; extension = ".dll"; }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) { extension = ".dylib"; }
            else if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;

            // ライブラリ名の正規化（プレフィックスや拡張子を除去）
            string baseName = libraryName;
            if (baseName.StartsWith("lib")) baseName = baseName.Substring(3);
            if (baseName.EndsWith(".so") || baseName.EndsWith(".dll") || baseName.EndsWith(".dylib"))
            {
                baseName = Path.GetFileNameWithoutExtension(baseName);
            }
            return $"{prefix}{baseName}{extension}";
        }
    }
}
