using hakoniwa.sim;
using hakoniwa.sim.core;
using Godot;
using System;

namespace hakoniwa.gui.sim
{
    public partial class SimStart : Node
    {
        [Export]
        public Label myText;
        [Export]
        public Button myButton;

        private enum SimCommandStatus
        {
            Start,
            Stop,
            Reset,
            Resetting
        }

        private SimCommandStatus cmdStatus = SimCommandStatus.Start;

        private float resetStartTime = 0f; // リセット開始時間
        private float resetDuration = 1f; // リセットの表示時間（秒）

        public override void _Ready()
        {
            if (myButton == null)
            {
                // Try to find if not assigned
                var btn = GetTree().Root.FindChild("Button_Start", true, false);
                if (btn != null)
                {
                    myButton = btn as Button;
                }
            }

            if (myButton != null)
            {
                myButton.Disabled = false;
                myButton.Pressed += OnButtonClick;
                if (myText == null)
                {
                    // If no label assigned, we might want to use button's own text
                    // but myText is used in many places. Let's see if there is a Label child.
//                    myText = FindComponent<Label>(myButton);
                    myText = FindNodeByInterface<Label>(myButton);
                }
            }
            if (myText != null)
            {
                myText.Text = "START";
            }
            else if (myButton != null)
            {
                myButton.Text = "START";
            }
        }

        private T FindComponent<T>(Node node) where T : class
        {
            if (node is T found) return found;
            foreach (Node child in node.GetChildren())
            {
                var result = FindComponent<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        public T FindNodeByInterface<T>(Node root) where T : class
        {
            if (root is T found) return found;

            foreach (Node child in root.GetChildren())
            {
                var result = FindNodeByInterface<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void SetText(string text)
        {
            if (myText != null)
            {
                myText.Text = text;
            }
            else if (myButton != null)
            {
                myButton.Text = text;
            }
        }

        public override void _Process(double delta)
        {
            var simulator = HakoAsset.GetHakoControl();
            if (simulator == null)
            {
                return;
            }
            if (myButton == null) return;

            var state = simulator.GetState();

            // Disable button if the simulator is in an invalid state
            if (state != HakoSimState.Running && state != HakoSimState.Stopped)
            {
                myButton.Disabled = true;
                return;
            }

            if (cmdStatus == SimCommandStatus.Resetting)
            {
                // リセットの経過時間をチェック
                float currentTime = Time.GetTicksMsec() / 1000.0f;
                if (currentTime - resetStartTime >= resetDuration)
                {
                    FinishReset(simulator);
                }
                return; // Resetting 中は他の状態遷移を防ぐ
            }

            // 通常状態でボタンを有効化
            myButton.Disabled = false;

            // Update button text and state based on simulator state and command status
            switch (cmdStatus)
            {
                case SimCommandStatus.Start:
                    if (state == HakoSimState.Running)
                    {
                        SetText("STOP");
                        cmdStatus = SimCommandStatus.Stop;
                    }
                    break;

                case SimCommandStatus.Stop:
                    if (state == HakoSimState.Stopped)
                    {
                        SetText("RESET");
                        cmdStatus = SimCommandStatus.Reset;
                    }
                    break;
            }
        }

        public void _on_button_pressed()
        {
            OnButtonClick();
        }

        public void OnButtonClick()
        {
            GD.Print("Button clicked");
            var simulator = HakoAsset.GetHakoControl();
            if (simulator == null)
            {
                GD.PrintErr("Simulator is null!");
                return;
            }

            switch (cmdStatus)
            {
                case SimCommandStatus.Start:
                    simulator.SimulationStart();
                    myButton.Disabled = true;
                    break;

                case SimCommandStatus.Stop:
                    simulator.SimulationStop();
                    myButton.Disabled = true;
                    break;

                case SimCommandStatus.Reset:
                    StartReset(simulator);
                    break;
            }
        }

        private void StartReset(IHakoControl simulator)
        {
            SetText("RESETTING"); // 表示をリセット中に変更
            cmdStatus = SimCommandStatus.Resetting;
            resetStartTime = Time.GetTicksMsec() / 1000.0f; // リセット開始時間を記録
            myButton.Disabled = true;
            simulator.SimulationReset(); // リセットを実行
        }

        private void FinishReset(IHakoControl simulator)
        {
            if (simulator.GetState() == HakoSimState.Stopped)
            {
                SetText("START"); // 表示を開始に戻す
                cmdStatus = SimCommandStatus.Start;
                myButton.Disabled = false;
            }
        }
    }
}