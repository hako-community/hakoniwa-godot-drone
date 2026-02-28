using System;
namespace hakoniwa.environment.interfaces
{
    public interface IFileLoader
    {
        string LoadText(string filePath, string extension = null);
    }
}
