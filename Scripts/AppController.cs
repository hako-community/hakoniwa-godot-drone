using Godot;
using System;

public partial class AppController : Node
{
    public override void _Ready()
    {
        // プロセス全体で Ctrl+C (SIGINT) を監視する
        Console.CancelKeyPress += (sender, e) => {
            // デフォルトの「プロセス即強制終了」を無効化（安全に閉じるため）
            e.Cancel = true;
            
            GD.Print("Ctrl+C detected. Shutting down Godot app...");
            
            // メインループ経由で安全にクリーンアップして終了
            GetTree().Quit();
        };
    }

    // 必要に応じて、ESCキーでの終了もここに入れておくと便利です
    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) // デフォルトでESCに割り当て
        {
            GetTree().Quit();
        }
    }
}