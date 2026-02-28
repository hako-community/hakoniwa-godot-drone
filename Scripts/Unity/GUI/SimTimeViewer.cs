using Godot;
using System;

namespace hakoniwa.objects.core
{
    public partial class SimTimeViewer : Node
    {
        [Export]
        public Node myObject; // Expecting a Label node
        [Export]
        public Node simtime_obj; // Expecting a node that implements ISimTime (e.g. DronePlayer)

        private ISimTime isimtime;
        private Label simTimeText;

        public override void _Ready()
        {
            // Cast myObject to Label
            simTimeText = myObject as Label;
            if (simTimeText == null)
            {
                GD.PrintErr("SimTimeViewer: myObject is not a Label!");
            }

            // Find ISimTime
            if (simtime_obj != null)
            {
                // 1. Check if simtime_obj itself implements ISimTime
                isimtime = simtime_obj as ISimTime;

                // 2. If not, check its children (BFS or just direct children)
                if (isimtime == null)
                {
                    foreach (var child in simtime_obj.GetChildren())
                    {
                        if (child is ISimTime)
                        {
                            isimtime = child as ISimTime;
                            break;
                        }
                    }
                }
            }

            if (isimtime == null)
            {
                GD.PrintErr("SimTimeViewer: ISimTime interface not found on simtime_obj or its children.");
            }
        }

        public override void _Process(double delta)
        {
            if (isimtime == null || simTimeText == null)
            {
                return;
            }

            long stime = isimtime.GetWorldTime();
            double t = ((double)stime) / 1000000.0f;
            if (stime <= 1)
            {
                simTimeText.Text = "0.000";
            }
            else
            {
                long tl = (long)(t * 1000);
                t = (double)tl / 1000;
                // Formatted string to ensure 3 decimal places
                simTimeText.Text = t.ToString("F3"); 
            }
        }
    }
}