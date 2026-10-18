using Godot;
using hakoniwa.drone.service;
using hakoniwa.objects.core;
using System;

namespace hakoniwa.drone
{
	/// <summary>
	/// 接触 1 回（箱庭の `hako_msgs/ImpulseCollision` と同じ中身）。★ ベクトルは **ROS**（x=北・y=西・z=上）。
	/// </summary>
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

	/// <summary>
	/// 機体の当たり判定。★★★★ 2026-10-04 に作り直した。
	///
	/// Unity から移植したときに衝突の計算がコメントアウトされたままで（"Physics logic temporarily disabled in port"）、
	/// **接触は一度もプラントへ届いていなかった**（drone-core でもオフラインでも、壁をすり抜けていた）。
	///
	/// ★ やり方: 物理フレームごとに、機体の形（この CollisionShape3D）をいまの位置に置いて
	///   Godot の物理に「どこに・どの向きで触れているか」を聞く（PhysicsDirectSpaceState3D.GetRestInfo）。
	///   機体は Godot の物理で動いているのではなく、プラント（swarm_app / hakodrone）の位置に置かれているだけなので、
	///   接触の信号（body_entered）は当てにしない。
	/// ★ 触れていれば `DroneImpulseCollision`（ROS）を用意する。受け取り方は DroneAvatar が決める:
	///   オンライン ＝ `impulse` PDU に書く（swarm_app が力積に直す）／ オフライン ＝ hakodrone の hd_apply_contact。
	///   ★ 力積の式はどちらも同じ（hakoniwa-mujoco-drone の sim/contact_impulse.hpp）。離れていく途中の接触は向こうが無視する。
	/// ★ 床（"Floor"・グループ "hako_ground"）は数えない。床はプラントの MuJoCo が持っているので、二重に跳ねる。
	/// </summary>
	public partial class DroneCollision : CollisionShape3D
	{
		[Export(PropertyHint.Layers3DPhysics)]
		private uint collisionLayer = 0xFFFFFFFF; // 衝突を検出するレイヤー（既定はすべて）

		[Export]
		private bool isHakoniwa = false;

		/// <summary>TargetColliderInfo が付いていない相手の反発係数。</summary>
		[Export]
		public double defaultRestitution = 0.5;

		/// <summary>★ プラントが既に持っている地面。名前かグループで外す。</summary>
		[Export]
		public string groundName = "Floor";
		public const string GroundGroup = "hako_ground";

		[Export]
		public bool debugLog = true;

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

		private ulong lastColliderId = 0;   // 振動とログを「触れた瞬間」だけにするため

		public override void _Ready()
		{
			if (pos_obj == null)
			{
				pos_obj = this;
			}
		}

		public override void _PhysicsProcess(double delta)
		{
			if (Shape == null) return;
			var space = GetWorld3D()?.DirectSpaceState;
			if (space == null) return;

			Transform3D xf = ShapeWorldTransform(out Vector3 scale);
			var query = new PhysicsShapeQueryParameters3D
			{
				Shape = ScaledShape(Shape, scale),
				Transform = xf,
				CollisionMask = collisionLayer,
				CollideWithAreas = false,   // ★ 採点の区域（Area3D）は物ではない
				CollideWithBodies = true,
				Exclude = OwnBodies(),
			};
			// ★ GetRestInfo は 1 つしか返さない。床が返ったら床を外して聞き直す（床と壁に同時に触れているとき）
			Godot.Collections.Dictionary info = null;
			CollisionObject3D collider = null;
			ulong colliderId = 0;
			for (int attempt = 0; attempt < 3; ++attempt)
			{
				info = space.GetRestInfo(query);
				if (info == null || info.Count == 0) { collider = null; break; }
				colliderId = info["collider_id"].AsUInt64();
				collider = GodotObject.InstanceFromId(colliderId) as CollisionObject3D;
				if (collider == null || !IsGround(collider)) break;
				var ex = query.Exclude;
				ex.Add(collider.GetRid());
				query.Exclude = ex;
				collider = null;
			}
			if (collider == null)
			{
				lastColliderId = 0;
				return;
			}

			Vector3 point = info["point"].AsVector3();
			Vector3 normal = info["normal"].AsVector3();
			Vector3 targetVel = info["linear_velocity"].AsVector3();
			Vector3 center = xf.Origin;

			var c = new DroneImpulseCollision();
			c.collision = true;
			c.normal = GodotToRos(normal);
			c.selfContactVector = GodotToRos(point - center);
			c.restitutionCoefficient = defaultRestitution;
			c.isTargetStatic = true;

			var tinfo = TargetColliderInfo.TryGetInfo(collider);
			if (tinfo != null)
			{
				c.isTargetStatic = tinfo.IsStatic;
				c.restitutionCoefficient = tinfo.RestitutionCoefficient;
				c.targetMass = tinfo.Mass;
				c.targetInertia = tinfo.Inertia;
				c.targetVelocity = GodotToRos(tinfo.Velocity);
				c.targetAngularVelocity = GodotToRos(tinfo.AngularVelocity);
			}
			else if (collider is RigidBody3D rb && !rb.Freeze)
			{
				// ★ 動く物（荷物・障害物）。慣性が自動（0）なら、大きさの分からない箱として質量から見積もる
				c.isTargetStatic = false;
				c.targetMass = rb.Mass;
				Vector3 inertia = rb.Inertia;
				if (inertia.X <= 0 || inertia.Y <= 0 || inertia.Z <= 0)
				{
					float i = rb.Mass * 0.1f;
					inertia = new Vector3(i, i, i);
				}
				c.targetInertia = inertia;   // 主慣性は軸の入れ替えだけなので大きさはそのまま
				c.targetVelocity = GodotToRos(targetVel);
				c.targetAngularVelocity = GodotToRos(rb.AngularVelocity);
			}
			if (!c.isTargetStatic)
			{
				c.targetContactVector = GodotToRos(point - collider.GlobalPosition);
			}
			impluse_collision = c;

			if (colliderId != lastColliderId)
			{
				lastColliderId = colliderId;
				if (debugLog)
				{
					GD.Print($"[DroneCollision] 接触: {collider.Name} {(c.isTargetStatic ? "静止物" : "動く物")} " +
					         $"法線(ROS)={c.normal} 接触点(ROS)={c.selfContactVector} e={c.restitutionCoefficient:F2}");
				}
				Vibrate();
			}
		}

		/// <summary>
		/// 機体の形の世界での置き場所。★ 機体は「プラントの位置に置かれた DroneAvatar」の子なので、
		/// Godot の物理が RigidBody を少し動かしていても、置き場所は親の Node3D から組み立てる。
		/// </summary>
		private Transform3D ShapeWorldTransform(out Vector3 scale)
		{
			Transform3D xf = GlobalTransform;
			var body = GetParent() as Node3D;
			var avatar = body?.GetParent() as Node3D;
			if (body is RigidBody3D && avatar != null)
			{
				xf = avatar.GlobalTransform * body.Transform * Transform;
			}
			scale = xf.Basis.Scale;
			// ★ 形の問い合わせは拡大縮小を扱えないので、向きと位置だけにして形の方を大きくする
			return new Transform3D(xf.Basis.Orthonormalized(), xf.Origin);
		}

		private static Shape3D ScaledShape(Shape3D shape, Vector3 s)
		{
			if (shape is BoxShape3D box)
			{
				return new BoxShape3D { Size = box.Size * s };
			}
			if (shape is SphereShape3D sphere)
			{
				return new SphereShape3D { Radius = sphere.Radius * Mathf.Max(s.X, Mathf.Max(s.Y, s.Z)) };
			}
			if (shape is CapsuleShape3D cap)
			{
				return new CapsuleShape3D { Radius = cap.Radius * Mathf.Max(s.X, s.Z), Height = cap.Height * s.Y };
			}
			return shape;
		}

		/// <summary>自分の機体（この形の持ち主と、DroneAvatar の下にある物）は数えない。</summary>
		private Godot.Collections.Array<Rid> OwnBodies()
		{
			var list = new Godot.Collections.Array<Rid>();
			Node root = GetParent()?.GetParent();
			if (root != null) AddBodies(root, list);
			else if (GetParent() is CollisionObject3D co) list.Add(co.GetRid());
			return list;
		}

		private static void AddBodies(Node n, Godot.Collections.Array<Rid> list)
		{
			if (n is CollisionObject3D co) list.Add(co.GetRid());
			foreach (Node ch in n.GetChildren()) AddBodies(ch, list);
		}

		private bool IsGround(CollisionObject3D collider)
		{
			return collider.IsInGroup(GroundGroup) ||
			       (!string.IsNullOrEmpty(groundName) && collider.Name == groundName);
		}

		private void Vibrate()
		{
			if (vibration == null) return;
			if (vibrationObject == null)
			{
				vibrationObject = vibration.GetDroneInput();
			}
			if (vibrationObject == null) return;
			vibrationObject.DoVibration(isRightHand: true, frequency: 0.9f, amplitude: 1.0f, durationSec: 0.2f);
			vibrationObject.DoVibration(isRightHand: false, frequency: 0.9f, amplitude: 1.0f, durationSec: 0.2f);
		}

		/// <summary>
		/// Godot（x=東・y=上・z=南）→ ROS（x=北・y=西・z=上）。
		/// ★ DroneAvatar が ROS → Godot を (-y, z, -x) で置いているので、その逆。回転を含む向きの変換（行列式 +1）なので、
		///   角速度も同じ式で変換できる。★ 以前のこのファイルの (z, -x, y) は Unity（左手系）のもので、Godot では符号が合わない。
		/// </summary>
		public static Vector3 GodotToRos(Vector3 g)
		{
			return new Vector3(-g.Z, -g.X, g.Y);
		}
	}
}
