using Godot;
using System.Collections.Generic;


public static class NodeUtil
{
    public static T FindNodeByInterface<T>(Node root) where T : class
    {
        if (root is T found) return found;

        foreach (Node child in root.GetChildren())
        {
            var result = FindNodeByInterface<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    public static void _FindComponentsRecursive<T>(Node node, List<T> results) where T : class
    {
        if (node == null) return;

        // 1. 自分自身が型 T にキャストできるかチェック
        if (node is T found)
        {
            results.Add(found);
        }

        // 2. 子ノードに対して再帰的に処理  
        foreach (Node child in node.GetChildren())
        {
            _FindComponentsRecursive(child, results);
        }
    }
}
