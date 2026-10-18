using Godot;
using System;
using System.Collections.Generic;

namespace hakoniwa.objects.core
{
    /// <summary>
    /// Stick conditioning (deadzone + response curve + per-axis sensitivity) for BOTH input paths.
    /// The gamepad matches a profile by joypad name (<see cref="Load(string)"/>); the XR path reads
    /// the "xr" section (<see cref="LoadSection"/>). Same maths, separate numbers.
    ///
    /// The gamepad path shipped with none of this while the XR path had a 0.15 deadzone and a 1.5
    /// exponent, which is why the pad felt twitchy and the drone drifted with the sticks centred:
    /// resting offsets of 0.04-0.06 went straight through, and left-stick X is multiplied by
    /// DroneControl.stick_yaw_strength (100), so a centred stick still commanded ~5 yaw.
    ///
    /// Values come from config/controller/gamepad_tuning.json, matched on the joypad NAME.
    /// GUID matching is not possible here: on Quest/Android GetJoyGuid() returns the ASCII of the
    /// name and GetJoyInfo() carries no vendor/product id.
    /// </summary>
    public class GamepadTuning
    {
        private const string ResourcePath = "res://config/controller/gamepad_tuning.json";
        private const string AndroidPackageName = "com.hakoniwa.dronesimulator";
        private const string OverrideFileName = "gamepad_tuning.json";

        public string SourceName = "built-in";
        public string ProfileName = "default";

        public float Deadzone = 0.15f;
        public float Exponent = 1.5f;
        public float YawSensitivity = 0.5f;
        public float ThrottleSensitivity = 1.0f;
        public float RollSensitivity = 1.0f;
        public float PitchSensitivity = 1.0f;

        /*
         * Yaw gets its own deadzone and curve, because on the Touch controllers it is the one axis
         * with a large mechanical cross-talk: run17 measured the LEFT thumbstick's X at a median
         * 0.34 (max 0.60) while it was being held fully up for throttle, over 117 samples. That is
         * the thumb's geometry, not drift.
         *
         * The shared 0.15 / 2.5 pair had to serve both jobs at once and could only do it by being
         * very soft everywhere -- half stick produced ~13 deg/s, which is what "yaw feels bad" is.
         * Splitting them lets the deadzone absorb the cross-talk while the curve stays usable:
         * a wider yaw deadzone kills the leak outright, so the exponent no longer has to.
         *
         * NaN (the default) means "inherit the shared value", so existing files keep working.
         */
        public float YawDeadzone = float.NaN;
        public float YawExponent = float.NaN;

        public float EffectiveYawDeadzone => float.IsNaN(YawDeadzone) ? Deadzone : YawDeadzone;
        public float EffectiveYawExponent => float.IsNaN(YawExponent) ? Exponent : YawExponent;

        /// <summary>
        /// Deadzone, then curve, then sensitivity. Sign is always preserved, so the
        /// "up = +1 / right = +1" contract of IDroneInput is unaffected.
        /// </summary>
        public float Apply(float raw, float sensitivity)
        {
            return Apply(raw, sensitivity, Deadzone, Exponent);
        }

        /// <summary>Same maths with an explicit deadzone/curve, for the yaw axis.</summary>
        public float ApplyYaw(float raw)
        {
            return Apply(raw, YawSensitivity, EffectiveYawDeadzone, EffectiveYawExponent);
        }

        private static float Apply(float raw, float sensitivity, float deadzone, float exponent)
        {
            float abs = Math.Abs(raw);
            if (abs < deadzone) return 0f;

            // Rescale so the value just outside the deadzone starts from 0 instead of jumping.
            // Guard the divisor: a deadzone of 1.0 in a hand-edited file would otherwise divide
            // by zero and send a NaN all the way to PutHeading.
            float span = Math.Max(1.0f - deadzone, 0.0001f);
            float scaled = Math.Min((abs - deadzone) / span, 1.0f);
            float curved = (float)Math.Pow(scaled, exponent);
            return Math.Sign(raw) * curved * sensitivity;
        }

        public override string ToString()
        {
            return $"profile=\"{ProfileName}\" src={SourceName} deadzone={Deadzone:F2} exp={Exponent:F2} " +
                   $"yaw={YawSensitivity:F3} yawdz={EffectiveYawDeadzone:F2} yawexp={EffectiveYawExponent:F2} " +
                   $"thr={ThrottleSensitivity:F2} roll={RollSensitivity:F2} pitch={PitchSensitivity:F2}";
        }

        /// <summary>
        /// Loads the profile matching <paramref name="joyName"/>. Never throws and never returns
        /// null: on any failure the built-in defaults are used so a bad JSON edit cannot ground
        /// the drone.
        /// </summary>
        public static GamepadTuning Load(string joyName)
        {
            return Load(joyName, null);
        }

        /// <summary>
        /// Loads the top-level section <paramref name="sectionKey"/> (merged over "default") instead
        /// of matching a joypad name. Used by the XR path, whose sticks are conditioned by the same
        /// maths but need their own numbers: the Touch thumbstick has a much shorter throw than a
        /// gamepad stick, and it feeds exactly the same DroneControl.stick_yaw_strength (100) and
        /// stick_strength (15) downstream.
        /// </summary>
        public static GamepadTuning LoadSection(string sectionKey)
        {
            return Load(null, sectionKey);
        }

        private static GamepadTuning Load(string joyName, string sectionKey)
        {
            var result = new GamepadTuning();

            string text = ReadOverrideFile(out string source) ?? ReadPackagedFile(out source);
            if (text == null)
            {
                GD.PrintErr("GamepadTuning: no tuning file found, using built-in defaults.");
                return result;
            }
            result.SourceName = source;

            var json = new Json();
            if (json.Parse(text) != Error.Ok)
            {
                GD.PrintErr($"GamepadTuning: parse error in {source} at line {json.GetErrorLine()}: {json.GetErrorMessage()}");
                return result;
            }

            if (json.Data.VariantType != Variant.Type.Dictionary)
            {
                GD.PrintErr($"GamepadTuning: {source} is not a JSON object.");
                return result;
            }

            var root = json.Data.AsGodotDictionary();

            // Defaults first, then let a matching profile override individual fields.
            if (root.ContainsKey("default"))
            {
                result.ApplyFields(root["default"], "default");
            }

            if (!string.IsNullOrEmpty(sectionKey))
            {
                if (root.ContainsKey(sectionKey))
                {
                    result.ApplyFields(root[sectionKey], sectionKey);
                }
                return result;
            }

            if (root.ContainsKey("profiles") && !string.IsNullOrEmpty(joyName))
            {
                var profiles = root["profiles"].AsGodotArray();
                foreach (var entry in profiles)
                {
                    if (entry.VariantType != Variant.Type.Dictionary) continue;
                    var dict = entry.AsGodotDictionary();
                    if (!dict.ContainsKey("match")) continue;

                    string match = dict["match"].AsString();
                    if (string.IsNullOrEmpty(match)) continue;
                    if (joyName.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    result.ApplyFields(entry, match);
                    break; // first match wins
                }
            }

            return result;
        }

        private void ApplyFields(Variant node, string profileName)
        {
            if (node.VariantType != Variant.Type.Dictionary) return;
            var d = node.AsGodotDictionary();

            ProfileName = profileName;
            Deadzone = ReadFloat(d, "deadzone", Deadzone);
            Exponent = ReadFloat(d, "exponent", Exponent);
            YawDeadzone = ReadFloat(d, "yaw_deadzone", YawDeadzone);
            YawExponent = ReadFloat(d, "yaw_exponent", YawExponent);
            YawSensitivity = ReadFloat(d, "yaw", YawSensitivity);
            ThrottleSensitivity = ReadFloat(d, "throttle", ThrottleSensitivity);
            RollSensitivity = ReadFloat(d, "roll", RollSensitivity);
            PitchSensitivity = ReadFloat(d, "pitch", PitchSensitivity);
        }

        private static float ReadFloat(Godot.Collections.Dictionary d, string key, float fallback)
        {
            if (!d.ContainsKey(key)) return fallback;
            var v = d[key];
            if (v.VariantType != Variant.Type.Float && v.VariantType != Variant.Type.Int) return fallback;
            return (float)v.AsDouble();
        }

        // Pushed with `adb push ... /sdcard/Android/data/<pkg>/files/`, so the feel can be retuned
        // on the headset without an export cycle.
        private static string ReadOverrideFile(out string source)
        {
            source = null;
            if (OS.GetName() != "Android") return null;

            string path = $"/sdcard/Android/data/{AndroidPackageName}/files/{OverrideFileName}";
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                source = "external-override";
                return System.IO.File.ReadAllText(path);
            }
            catch (Exception e)
            {
                GD.PrintErr($"GamepadTuning: could not read override {path}: {e.Message}");
                return null;
            }
        }

        private static string ReadPackagedFile(out string source)
        {
            source = null;
            using var f = FileAccess.Open(ResourcePath, FileAccess.ModeFlags.Read);
            if (f == null) return null;
            source = "packaged";
            return f.GetAsText();
        }
    }
}
