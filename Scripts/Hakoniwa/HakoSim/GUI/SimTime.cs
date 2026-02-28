using Godot;
using System;
using hakoniwa.sim.core;

namespace hakoniwa.gui.sim
{
    public partial class SimTime : Label
    {
//        private Label simTimeText;

        public override void _Ready()
        {
//            simTimeText = GetNode<Label>("Label_SimTimeH");
        }

        public override void _Process(double delta)
        {
            var simulator = HakoAsset.GetHakoControl();
//            if (simulator == null || simTimeText == null) return;
            if (simulator == null) return;

            long simtime = simulator.GetWorldTime();
            double t = ((double)simtime) / 1000000.0f;
//            simTimeText.Text = "Simulation Time [Sec] : ";
            this.Text = "Simulation Time [Sec] : ";
            if (simtime <= 1)
            {
//                simTimeText.Text += "0.000";
                this.Text += "0.000";
            }
            else
            {
                // Formatted to 3 decimal places
//                simTimeText.Text = t.ToString("F3");
                this.Text += t.ToString("F3");
            }
        }
    }
}