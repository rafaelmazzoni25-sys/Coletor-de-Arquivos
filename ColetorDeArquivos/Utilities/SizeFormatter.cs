namespace ColetorDeArquivos.Utilities;

public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string FormatBytes(long size)
    {
        if (size < 0)
        {
            size = 0;
        }

        double formatted = size;
        var unitIndex = 0;

        while (formatted >= 1024 && unitIndex < Units.Length - 1)
        {
            formatted /= 1024;
            unitIndex++;
        }

        return $"{formatted:0.##} {Units[unitIndex]}";
    }
}
