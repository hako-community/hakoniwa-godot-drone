using System;
using System.Runtime.InteropServices;
using System.Reflection;
using Godot;
using System.IO;

namespace Hakoniwa.Core.Utils
{
    public static class HakoLibLoader
    {
        private static bool isRegistered = false;

        public static void Register()
        {
            if (isRegistered) return;

            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ResolveDllImport);
            isRegistered = true;
            GD.Print("HakoLibLoader: DllImportResolver registered.");
        }

        private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            // 対応するライブラリ名かチェック
            if (IsHakoLibrary(libraryName))
            {
                string path = GetLibraryPath(libraryName);
                if (path != null && File.Exists(path))
                {
                    GD.Print($"HakoLibLoader: Loading {libraryName} from {path}");
                    return NativeLibrary.Load(path);
                }
                GD.PrintErr($"HakoLibLoader: Could not find library {libraryName} at {path}");
            }
            return IntPtr.Zero;
        }

        private static bool IsHakoLibrary(string libraryName)
        {
            return libraryName.Contains("hako_service_c") || 
                   libraryName.Contains("shakoc") || 
                   libraryName.Contains("conductor");
        }

        private static string GetLibraryPath(string libraryName)
        {
            string osDir = "";
            string archDir = "";
            string prefix = "lib";
            string extension = ".so";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                osDir = "Windows";
                prefix = "";
                extension = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                osDir = "Linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                osDir = "macOS";
                extension = ".dylib";
            }
            else
            {
                return null;
            }

            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X64:
                    archDir = "x86_64";
                    break;
                case Architecture.Arm64:
                    archDir = "ARM64";
                    break;
                default:
                    return null;
            }

            // ライブラリ名の正規化（プレフィックスや拡張子を除去）
            string baseName = libraryName;
            if (baseName.StartsWith("lib")) baseName = baseName.Substring(3);
            if (baseName.EndsWith(".so") || baseName.EndsWith(".dll") || baseName.EndsWith(".dylib"))
            {
                 baseName = Path.GetFileNameWithoutExtension(baseName);
            }

            string fileName = $"{prefix}{baseName}{extension}";

            // HAKO_NATIVE_LIB_DIR: 同梱の Plugins より先に探すフォルダ（未設定なら従来どおり）。
            // プラントと同じ版の箱庭コア（shakoc など）を読ませるための口。共有メモリの構造は
            // コアの版と上限で決まるので、プラントと違う版の shakoc で繋ぐと読めないか壊れる。
            // そのフォルダに無いライブラリ（hako_service_c など）は従来どおり Plugins から読む。
            string overrideDir = System.Environment.GetEnvironmentVariable("HAKO_NATIVE_LIB_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
            {
                string overridePath = Path.Combine(overrideDir, fileName);
                if (File.Exists(overridePath)) return overridePath;
                GD.Print($"HakoLibLoader: {fileName} is not in HAKO_NATIVE_LIB_DIR ({overrideDir}); using Plugins");
            }

            // Godotプロジェクトの Plugins ディレクトリ配下を探索
            // 実行環境（エディタ/エクスポート）に応じてカレントディレクトリを基準にする
            string projectRoot = Directory.GetCurrentDirectory();
            return Path.Combine(projectRoot, "Plugins", osDir, archDir, fileName);
        }
    }
}
