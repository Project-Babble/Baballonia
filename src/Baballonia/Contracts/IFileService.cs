using System.Threading.Tasks;

namespace Baballonia.Contracts;

public interface IFileService
{
    T Read<T>(string folderPath, string fileName);

    Task Save<T>(string folderPath, string fileName, T content);

    void Delete(string folderPath, string fileName);
}
