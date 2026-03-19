using Baballonia.Contracts;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Baballonia.Services;

public class FileService : IFileService
{
    public T Read<T>(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json)!;
        }

        return default!;
    }

    public async Task Save<T>(string folderPath, string fileName, T content)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var filePath = Path.Combine(folderPath, fileName);

        // Check if file is being used by another process
        if (File.Exists(filePath) && IsFileInUse(filePath))
        {
            return;
        }

        try
        {
            var fileContent = JsonConvert.SerializeObject(content, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, fileContent, Encoding.UTF8);
        }
        catch (IOException ex) when (IsFileLockException(ex))
        {
            return;
        }
    }

    private static bool IsFileInUse(string filePath)
    {
        try
        {
            using (File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // If we can open the file exclusively, it's not in use
                return false;
            }
        }
        catch (IOException)
        {
            // If we can't open the file exclusively, it's likely in use
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // File might be read-only or we don't have permissions
            return true;
        }
    }

    private static bool IsFileLockException(IOException ex)
    {
        // Common error codes for file lock issues
        const int ERROR_SHARING_VIOLATION = 32;
        const int ERROR_LOCK_VIOLATION = 33;

        var errorCode = ex.HResult & 0xFFFF;
        return errorCode == ERROR_SHARING_VIOLATION || errorCode == ERROR_LOCK_VIOLATION;
    }

    public void Delete(string folderPath, string fileName)
    {
        if (fileName != null && File.Exists(Path.Combine(folderPath, fileName)))
        {
            File.Delete(Path.Combine(folderPath, fileName));
        }
    }
}
