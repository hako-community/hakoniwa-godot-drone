using Godot;

namespace hakoniwa.objects.core
{
    /// <summary>
    /// Baggageクラス
    /// ドローンや他のオブジェクトに運搬される荷物を模したオブジェクトの挙動を管理します。
    /// </summary>
    public partial class Baggage : Node3D
    {
        [Export]
        public Node3D parentNode = null; // 現在の親オブジェクト（運搬元）
        [Export]
        public float speed = 10f; // 移動速度（Lerpの補間速度）
        private Node initial_parent; // 初期の親オブジェクト（元の状態を戻すため）
        private RigidBody3D rd; // Rigidbodyコンポーネントへの参照

        /// <summary>
        /// 初期化処理
        /// </summary>
        public override void _Ready()
        {
            // 初期の親オブジェクトを保存
            initial_parent = GetParent();

            // Rigidbodyコンポーネントを取得
            rd = NodeUtil.FindNodeByInterface<RigidBody3D>(this);
            if (rd == null)
            {
                GD.Print($"Not found RigidBody3D on {this.Name}");
            }
        }

        /// <summary>
        /// 他のオブジェクトに掴まれる（親として登録される）
        /// </summary>
        /// <param name="grab_parent">掴むオブジェクト</param>
        public void Grab(Node3D grab_parent)
        {
            this.parentNode = grab_parent; // 親オブジェクトを設定
        }

        /// <summary>
        /// 現在の親オブジェクトからリリースされる（自由な状態になる）
        /// </summary>
        public void Release()
        {
            this.parentNode = null; // 親オブジェクトをリセット
        }

        /// <summary>
        /// 自由な状態かどうかを確認
        /// </summary>
        /// <returns>自由であればtrue、掴まれていればfalse</returns>
        public bool IsFree()
        {
            return this.parentNode == null;
        }

        /// <summary>
        /// 毎フレームの物理更新処理
        /// </summary>
        public override void _PhysicsProcess(double delta)
        {
            if (parentNode != null)
            {
                // 親が設定されている場合（掴まれている状態）
                if (GetParent() != parentNode)
                {
                    // Node.Reparent を使用してノードツリー上の親を変更（グローバル座標を維持）
                    CallDeferred(MethodName.Reparent, parentNode);
                }

                if (rd != null)
                {
                    // 物理挙動を無効化（Freeze = true）
                    rd.Freeze = true;
                }

                float lerpFactor = (float)(delta * speed);

                // 親オブジェクトの位置・回転に向かって補間で移動
                GlobalPosition = GlobalPosition.Lerp(parentNode.GlobalPosition, lerpFactor);
                
                // 回転の補間（Slerp）
                Quaternion currentRot = GlobalBasis.GetRotationQuaternion();
                Quaternion targetRot = parentNode.GlobalBasis.GetRotationQuaternion();
                Quaternion nextRot = currentRot.Slerp(targetRot, lerpFactor);
                GlobalBasis = new Basis(nextRot);
            }
            else
            {
                // 親が設定されていない場合（自由な状態）
                if (GetParent() != initial_parent && initial_parent != null)
                {
                    CallDeferred(MethodName.Reparent, initial_parent);
                }

                if (rd != null)
                {
                    // 物理挙動を再度有効化
                    rd.Freeze = false;
                }
            }
        }
    }
}