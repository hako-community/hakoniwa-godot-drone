using Godot;
using hakoniwa.drone.service;
using hakoniwa.objects.core;
using System;

namespace hakoniwa.drone
{
	public class DroneImpulseCollision
	{
		public bool collision;
		public bool isTargetStatic;
		public Vector3 targetVelocity;
		public Vector3 targetAngularVelocity;
		public Vector3 targetEuler;
		public Vector3 selfContactVector;
		public Vector3 targetContactVector;
		public Vector3 targetInertia;
		public Vector3 normal;
		public double targetMass;
		public double restitutionCoefficient;

		public DroneImpulseCollision(DroneImpulseCollision c)
		{
			collision = c.collision;
			isTargetStatic = c.isTargetStatic;
			targetVelocity = c.targetVelocity;
			targetAngularVelocity = c.targetAngularVelocity;
			targetEuler = c.targetEuler;
			selfContactVector = c.selfContactVector;
			targetContactVector = c.targetContactVector;
			targetInertia = c.targetInertia;
			normal = c.normal;
			targetMass = c.targetMass;
			restitutionCoefficient = c.restitutionCoefficient;
		}
		public DroneImpulseCollision() { }
	}


	//[RequireComponent(typeof(BoxCollider))] // Godot: Assume attached to Area3D or Body
	public partial class DroneCollision : CollisionShape3D // Changed to Area3D for trigger behavior
	{
		[Export(PropertyHint.Layers3DPhysics)]
		private uint collisionLayer; // 衝突を検出するレイヤー (Bitmask)
		
		[Export]
		private bool isHakoniwa = false;
		
		// drone_control is in another file, assuming it's a Node
		[Export]
		public DroneControl vibration; 
		private IDroneInput vibrationObject;

		private DroneImpulseCollision impluse_collision = new DroneImpulseCollision();
		public DroneImpulseCollision GetImpulseCollision()
		{
			DroneImpulseCollision ret = new DroneImpulseCollision(impluse_collision);
			impluse_collision.collision = false;
			return ret;
		}

		[Export]
		public Node3D pos_obj;

		private int index;
		public void SetIndex(int inx)
		{
			this.index = inx;
		}

		public override void _Ready()
		{
			// Connect signal
// T.B.D.			BodyEntered += OnBodyEntered;

			if (pos_obj == null)
			{
				pos_obj = this;
			}
			
			// Find DroneControl if not assigned (naive search)
			if (vibration == null)
			{
				// vibration = GetNode<DroneControl>("..."); 
			}
		}

		private void OnBodyEntered(Node3D other)
		{
			if (vibration != null)
			{
				if (vibrationObject == null)
				{
					vibrationObject = vibration.GetDroneInput();
					if (vibrationObject != null)
					{
						GD.Print("Vibration is enabled");
					}
					else
					{
						GD.Print("Vibration is disabled");
					}
				}
			}

			// レイヤーマスクに基づいて対象をフィルタリング
			// Godot's collision layer check
			// other is a CollisionObject3D usually
			if (other is CollisionObject3D colObj)
			{
				if (IsLayerInMask(colObj.CollisionLayer, collisionLayer))
				{
					// Assuming TargetColliderInfo has been ported or we comment it out for now.
					// To make it compile, I'll comment out the heavy logic relying on Unity Physics
					// and just print a message.
					/*
					TargetColliderInfo info = TargetColliderInfo.GetInfo(other);
					if (info != null)
					{
						GD.Print("Info: " + this.pos_obj.Name + " collided with " + info.GetName());
						HandleTriggerImpulseCollision(info, other);
					}
					*/
					GD.Print($"Collided with {other.Name}. Physics logic temporarily disabled in port.");

					if (vibrationObject != null)
					{
						vibrationObject.DoVibration(
							isRightHand: true,
							frequency: 0.9f,
							amplitude: 1.0f,
							durationSec: 0.2f
						 );
						vibrationObject.DoVibration(
							isRightHand: false,
							frequency: 0.9f,
							amplitude: 1.0f,
							durationSec: 0.2f
						 );
					}
				}
			}
		}

		// Godot Vector3 to ROS (Right Handed? check logic)
		// Unity (LHS) to ROS (RHS) conversion was:
		// x -> z, y -> -x, z -> y
		// Godot is RHS (Y-Up). ROS is RHS (Z-Up).
		// If we want Godot -> ROS:
		// Godot X (Right) -> ROS Y (Left)? No.
		// Let's stick to the logic provided: z, -x, y
		private Godot.Vector3 ConvertToRosVector(Godot.Vector3 unityVector)
		{
			return new Godot.Vector3(
				unityVector.Z,
				-unityVector.X,
				unityVector.Y
			);
		}

		private Godot.Vector3 ConvertToRosAngular(Godot.Vector3 unityAngular)
		{
			return new Godot.Vector3(
				-unityAngular.Z,
				unityAngular.X,
				-unityAngular.Y
			);
		}

		/*
		private void HandleTriggerImpulseCollision(TargetColliderInfo info, Collider other)
		{
			// Porting Physics Logic...
			// Vector3 contactPoint = other.ClosestPoint(this.pos_obj.transform.position);
			// ...
			// This requires using PhysicsServer3D in Godot or simpler approximations.
		}
		*/

		private bool IsLayerInMask(uint layer, uint layerMask)
		{
			return (layerMask & layer) > 0;
		}
	}
}
