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
			if (pos_obj == null)
			{
				pos_obj = this;
			}

			// parent RigidBody3D の BodyEntered シグナルを接続
			var parent = GetParent() as RigidBody3D;
			if (parent != null)
			{
				parent.ContactMonitor = true;
				if (parent.MaxContactsReported < 4)
				{
					parent.MaxContactsReported = 4;
				}
				parent.BodyEntered += OnBodyEntered;
				GD.Print($"[DroneCollision] Connected to parent RigidBody3D ({parent.Name}) BodyEntered signal.");
			}
			else
			{
				GD.PushWarning("[DroneCollision] Parent is not a RigidBody3D. Physical collision detection might not work.");
			}
			
			// Find DroneControl if not assigned (naive search)
			if (vibration == null)
			{
				// vibration = GetNode<DroneControl>("..."); 
			}
		}

		// 猫パンチ等、距離ベースの命中判定から直接インパルスをトリガーするメソッド
		public void TriggerCatHitImpulse(Vector3 punchDirection, double velocityMagnitude)
		{
			impluse_collision.collision = true;
			impluse_collision.isTargetStatic = false;
			impluse_collision.restitutionCoefficient = 0.15; // 反発係数を低く（ソフトな打撃）
			
			// Godot座標系での法線（猫からドローンへの方向）と速度
			Vector3 godotNormal = punchDirection.Normalized();
			Vector3 godotVelocity = punchDirection.Normalized() * (float)velocityMagnitude;
			
			// 箱庭シミュレータ（ROS座標系）用に座標変換
			impluse_collision.normal = ConvertToRosVector(godotNormal);
			impluse_collision.targetVelocity = ConvertToRosVector(godotVelocity);
			
			impluse_collision.targetContactVector = Vector3.Zero;
			impluse_collision.selfContactVector = Vector3.Zero;
			
			impluse_collision.targetMass = 0.15; // 猫の前足の質量相当（約150g）にすることで衝撃を低減
			impluse_collision.targetInertia = new Vector3(0.1f, 0.1f, 0.1f);
			impluse_collision.targetEuler = Vector3.Zero;
			impluse_collision.targetAngularVelocity = Vector3.Zero;
			
			GD.Print($"[DroneCollision] TriggerCatHitImpulse called: direction={punchDirection}, velocity={velocityMagnitude}, ROS normal={impluse_collision.normal}");
		}

		private void OnBodyEntered(Node otherNode)
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

			if (otherNode is CollisionObject3D colObj)
			{
				if (IsLayerInMask(colObj.CollisionLayer, collisionLayer))
				{
					GD.Print($"[DroneCollision] Physical collision detected with: {colObj.Name}");
					
					impluse_collision.collision = true;
					impluse_collision.isTargetStatic = colObj is StaticBody3D;
					impluse_collision.restitutionCoefficient = 0.5;
					
					Vector3 relativeVelocity = Vector3.Zero;
					if (colObj is RigidBody3D rigidBody)
					{
						relativeVelocity = rigidBody.LinearVelocity;
					}
					
					// 衝突した法線方向（相手からドローンへの方向）
					Vector3 direction = (this.GlobalPosition - colObj.GlobalPosition).Normalized();
					impluse_collision.normal = ConvertToRosVector(direction);
					impluse_collision.targetVelocity = ConvertToRosVector(relativeVelocity);
					impluse_collision.targetContactVector = Vector3.Zero;
					impluse_collision.selfContactVector = Vector3.Zero;
					
					impluse_collision.targetMass = 1.0;
					impluse_collision.targetInertia = new Vector3(0.1f, 0.1f, 0.1f);
					impluse_collision.targetEuler = Vector3.Zero;
					impluse_collision.targetAngularVelocity = Vector3.Zero;

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

		// Godot Vector3 to ROS
		// Godot: X=right, Y=up, Z=back
		// ROS: X=forward (-Z), Y=left (-X), Z=up (Y)
		private Godot.Vector3 ConvertToRosVector(Godot.Vector3 godotVector)
		{
			return new Godot.Vector3(
				-godotVector.Z,
				-godotVector.X,
				godotVector.Y
			);
		}

		private Godot.Vector3 ConvertToRosAngular(Godot.Vector3 godotAngular)
		{
			// X (Roll) -> -Z, Y (Pitch) -> X, Z (Yaw) -> -Y
			return new Godot.Vector3(
				-godotAngular.Z,
				godotAngular.X,
				-godotAngular.Y
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
