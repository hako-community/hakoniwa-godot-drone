using System.Collections.Generic;
using Godot;

namespace hakoniwa.objects.core
{
    public partial class DroneBatteryUI : Node3D
    {
        // BatteryBar1〜10のImageコンポーネントをリストで設定
        // Godot: Use Control or ColorRect. Modulate controls color.
        [Export]
        private int batteryValue = 0;
        public Godot.Collections.Array<Control> batteryBars; 

        [Export]
        public TextureProgressBar batteryProgressBar; // バッテリー残量を表示するプログレスバー

        private float fullVoltage = 14.8f;
        private float currentVoltage = 9.0f;
        private float temperature = 0.0f;
        private float pressure = 1.0f;
        
        [Export]
        public Label fullVoltageText;
        [Export]
        public Label currVoltageText;
        [Export]
        public Label percentageText;
        [Export]
        public Label tempText;
        [Export]
        public Label pressureText;
        
        [Export]
        public Node battery; // Node -> Node
        
        private IDroneBatteryStatus drone_battery_status;

        public override void _Ready()
        {
            if (battery != null)
                drone_battery_status = FindBatteryStatus(battery);
                
            if (drone_battery_status == null)
            {
                GD.PrintErr("DroneBatteryUI: IDroneBatteryStatus not found in battery node.");
            }
        }
        
        private IDroneBatteryStatus FindBatteryStatus(Node root)
        {
            if (root is IDroneBatteryStatus status) return status;
            foreach(Node child in root.GetChildren())
            {
                var res = FindBatteryStatus(child);
                if (res != null) return res;
            }
            return null;
        }

        public override void _Process(double delta)
        {
            if (drone_battery_status == null) return;

            fullVoltage = (float)drone_battery_status.get_full_voltage();
            currentVoltage = (float)drone_battery_status.get_curr_voltage();
            temperature = (float)drone_battery_status.get_temperature();
            pressure = (float)drone_battery_status.get_atmospheric_pressure();
            float batteryPercentage = currentVoltage / fullVoltage;
            float percentValue = batteryPercentage * 100.0f;
            batteryValue = (int)(batteryPercentage * 100);
            batteryProgressBar.Value = percentValue;
            uint battery_status_level = drone_battery_status.get_status();
            
            if (fullVoltageText != null) fullVoltageText.Text = fullVoltage.ToString("F1");
            if (currVoltageText != null) currVoltageText.Text = currentVoltage.ToString("F1");
            if (tempText != null) tempText.Text = temperature.ToString("F1");
            if (pressureText != null)
            {
                pressureText.Text = pressure.ToString("F1");
                //GD.Print("preassure: " + pressure);
            }
            if (percentageText != null) percentageText.Text = percentValue.ToString("F1");
            
            Color color = Colors.White;
            // 温度に応じた色を設定
            if (tempText != null)
            {
                if (temperature < 20.0f)
                {
                    tempText.Modulate = Colors.Blue; // 低温（青）
                }
                else if (temperature >= 20.0f && temperature <= 50.0f)
                {
                    tempText.Modulate = Colors.White; // 通常温度（白）
                }
                else
                {
                    tempText.Modulate = Colors.Red; // 高温（赤）
                }
            }

            // 残量に応じた色を設定
            if (battery_status_level == 0)
                color = Colors.Green;
            else if (battery_status_level == 1)
                color = Colors.Yellow;
            else
                color = Colors.Red;

            double percent = 0;
            // 各バーの色を設定
            if (batteryBars != null)
            {
                for (int i = batteryBars.Count - 1; i >= 0; i--)
                {
                    if (batteryBars[i] == null) continue;
                    
                    if (percent <= batteryPercentage)
                    {
                        batteryBars[i].Modulate = color;
                    }
                    else
                    {
                        batteryBars[i].Modulate = Colors.White;
                    }
                    percent += 0.1;
                }
            }
        }
    }
}