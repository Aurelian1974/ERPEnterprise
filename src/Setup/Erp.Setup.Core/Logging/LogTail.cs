namespace Erp.Setup.Core.Logging;

/// <summary>Ultimele randuri din jurnalele componentelor, afisate la un health-check esuat (8.14, punctul 5).</summary>
public static class LogTail
{
    public static IReadOnlyList<string> Read(string directory, int lines = 50)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var newest = new DirectoryInfo(directory).EnumerateFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        if (newest is null)
        {
            return [];
        }

        // Fisierul poate fi tinut deschis de componenta care scrie in el.
        using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var queue = new Queue<string>(lines);
        while (reader.ReadLine() is { } line)
        {
            if (queue.Count == lines)
            {
                queue.Dequeue();
            }

            queue.Enqueue(line);
        }

        return [.. queue];
    }
}
