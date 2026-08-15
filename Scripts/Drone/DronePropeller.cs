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
		private Node droneSoundNode;
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
			audioSource = NodeUtil.FindNodeByInterface<AudioStreamPlayer3D>(this);
			if (audioSource == null && enableAudio)
			{
				audioSource = new AudioStreamPlayer3D();
				AddChild(audioSource);
			}
			
			if (enableAudio)
			{
				LoadAudio();
			}

			if (GetParent() != null)
			{
				droneSoundNode = GetParent().FindChild("DroneSound", true, false);
			}
			if (droneSoundNode == null)
			{
				droneSoundNode = FindChild("DroneSound", true, false);
			}
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

		private float current_c1 = 0;
		private float current_c2 = 0;
		private float current_c3 = 0;
		private float current_c4 = 0;

		public override void _Process(double delta)
		{
			float dt = (float)delta;
			RotatePropeller(propeller1, current_c1, dt);
			RotatePropeller(propeller2, -current_c2, dt);
			if (propeller3 != null)
			{
				RotatePropeller(propeller3, current_c3, dt);
			}
			if (propeller4 != null)
			{
				RotatePropeller(propeller4, -current_c4, dt);
			}
			if (propeller5 != null)
			{
				RotatePropeller(propeller5, current_c1, dt);
			}
			if (propeller6 != null)
			{
				RotatePropeller(propeller6, current_c2, dt);
			}

			if (enableAudio)
			{
				PlayAudio(current_c1);
			}
		}

		private void PlayAudio(float my_controls)
		{
			if (audioSource == null || target_camera == null) return;

			float distance = (target_camera.GlobalPosition - GlobalPosition).Length();

			if (audioSource.Playing == false && my_controls > 0)
			{
				audioSource.Play();
			}
			else if (audioSource.Playing == true && my_controls == 0)
			{
				audioSource.Stop();
			}
		}

		private void RotatePropeller(Node3D propeller, float dutyRate, float dt)
		{
			if (propeller == null || Mathf.Abs(dutyRate) < 0.0001f) return;
			float rotationSpeed = maxRotationSpeed * dutyRate;
			propeller.RotateY(rotationSpeed * dt);
		}

		public void Rotate(float c1, float c2, float c3, float c4)
		{
			current_c1 = c1;
			current_c2 = c2;
			current_c3 = c3;
			current_c4 = c4;

			if (droneSoundNode != null)
			{
				droneSoundNode.Call("set_controls", c1, c2, c3, c4);
			}
		}
	}
}
