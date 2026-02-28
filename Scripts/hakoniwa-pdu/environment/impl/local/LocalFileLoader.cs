#if !UNITY
using System.IO;
using hakoniwa.environment.interfaces;

namespace hakoniwa.environment.impl.local
{
    public class LocalFileLoader: IFileLoader
    {
        public LocalFileLoader()
        {
        }

        public string LoadText(string filePath, string extension = null)
        {
            string fullPath = extension == null ? filePath : filePath + extension;
            return File.ReadAllText(fullPath);
        }
    }
}
#endif
