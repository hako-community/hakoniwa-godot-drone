#if !NO_USE_UNITY
using Godot;
using System.IO;
using hakoniwa.environment.interfaces;

namespace hakoniwa.environment.impl.unity
{
    public class ResourcesFileLoader: IFileLoader
    {
        public ResourcesFileLoader()
        {
        }

        public string LoadText(string filePath, string extension = null)
        {
            // ./や先頭の/を削除した形でfullPathを作成
            //string fullPath = filePath.StartsWith("./") ? filePath.Substring(2) : filePath.TrimStart('/');
            string fullPath = filePath.StartsWith("." + Path.DirectorySeparatorChar.ToString())
                ? filePath.Substring(2)
                : filePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            TextAsset textAsset = Resources.Load<TextAsset>(fullPath);
            if (textAsset == null)
            {
                throw new FileNotFoundException($"File '{fullPath}' not found in Resources.");
            }
            return textAsset.text;
        }
    }
}
#endif