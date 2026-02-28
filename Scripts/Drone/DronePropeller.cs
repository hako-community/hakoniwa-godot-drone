using System;
using Godot;

namespace hakoniwa.drone
{
	public partial class DronePropeller : Node3D
	{
		[Export]
		public Node3D propeller1;
		[Export]
		public Node3D propeller2;
		[Export]
		public Node3D propeller3;
		[Export]
		public Node3D propeller4;
		[Export]
		public Node3D propeller5;
		[Export]
		public Node3D propeller6;

		[Export]
		public bool enableAudio = true;
		[Export]
		public float maxRotationSpeed = 1f;
		
		private AudioStreamPlayer3D audioSource;
		[Export]
		public string audio_path;
		[Export]
		public Camera3D target_camera;
		[Export]
		public float maxDistance = 5.0f;
		[Export]
		public float minDistance = 0.0f;

		public override void _Ready()
		{
//			audioSource = FindComponent<AudioStreamPlayer3D>(this);
			audioSource = FindNodeByInterface<AudioStreamPlayer3D>(this);
			if (audioSource == null && enableAudio)
			{
				audioSource = new AudioStreamPlayer3D();
				AddChild(audioSource);
			}
			
			if (enableAudio)
			{
				LoadAudio();
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

		private void LoadAudio()
		{
			if (string.IsNullOrEmpty(audio_path)) return;
			
			// Assume audio_path is res://...
			string path = audio_path;
			if (!path.StartsWith("res://"))
			{
				path = "res://" + path;
			}
			
			AudioStream stream = GD.Load<AudioStream>(path);
			if (stream != null)
			{
				GD.Print("audio found: " + path);
				audioSource.Stream = stream;
				audioSource.Stop();
			}
			else
			{
				GD.PushWarning("audio not found: " + path);
			}
		}

		private void PlayAudio(float my_controls)
		{
			if (audioSource == null || target_camera == null) return;

			float distance = (target_camera.GlobalPosition - GlobalPosition).Length();
			// Godot's AudioStreamPlayer3D handles attenuation automatically based on unit size and max distance.
			// But we can manually adjust volume if needed.
			// float volume = 1.0f - Mathf.Clamp((distance - minDistance) / (maxDistance - minDistance), 0, 1);

			if (audioSource.Playing == false && my_controls > 0)
			{
				audioSource.Play();
			}
			else if (audioSource.Playing == true && my_controls == 0)
			{
				audioSource.Stop();
			}
		}

		private void RotatePropeller(Node3D propeller, float dutyRate)
		{
			if (propeller == null) return;
			float rotationSpeed = maxRotationSpeed * dutyRate;
			// Assuming Y-axis is rotation axis. RotateY takes radians.
			propeller.RotateY(rotationSpeed * (float)GetProcessDeltaTime());
		}

		public void Rotate(float c1, float c2, float c3, float c4)
		{
			RotatePropeller(propeller1, c1);
			RotatePropeller(propeller2, -c2);
			if (propeller3 != null)
			{
				RotatePropeller(propeller3, c3);
			}
			if (propeller4 != null)
			{
				RotatePropeller(propeller4, -c4);
			}
			if (propeller5 != null)
			{
				RotatePropeller(propeller5, c1);
			}
			if (propeller6 != null)
			{
				RotatePropeller(propeller6, c2);
			}
			
			if (enableAudio)
			{
				PlayAudio(c1);
			}
		}
	}
}
