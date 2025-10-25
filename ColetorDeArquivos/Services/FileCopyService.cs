using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ColetorDeArquivos.Models;

namespace ColetorDeArquivos.Services;

public class FileCopyService
{
    public async Task CopyAsync(IReadOnlyList<SearchHit> hits, string destinationRoot, bool overwrite, bool dryRun, IProgress<string>? logProgress, CancellationToken cancellationToken)
    {
        if (hits.Count == 0)
        {
            return;
        }

        await Task.Run(() =>
        {
            foreach (var hit in hits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var message = CopyPreservingStructure(hit, destinationRoot, overwrite, dryRun);
                    logProgress?.Report(message);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logProgress?.Report($"[ERRO] Falha ao copiar {hit.FullPath}: {ex.Message}");
                }
            }
        }, cancellationToken);
    }

    private static string CopyPreservingStructure(SearchHit hit, string destinationRoot, bool overwrite, bool dryRun)
    {
        var relativePath = GetRelativePath(hit.RootPath, hit.FullPath);
        var destinationPath = Path.Combine(destinationRoot, relativePath);
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (destinationDirectory is null)
        {
            throw new InvalidOperationException("Não foi possível determinar a pasta de destino.");
        }

        if (dryRun)
        {
            return $"[DRY-RUN] {hit.FullPath} -> {destinationPath}";
        }

        Directory.CreateDirectory(destinationDirectory);
        if (!overwrite && File.Exists(destinationPath))
        {
            return $"[PULADO - existe] {destinationPath}";
        }

        File.Copy(hit.FullPath, destinationPath, overwrite);
        File.SetLastWriteTime(destinationPath, hit.LastModified);
        return $"[OK] {hit.FullPath} -> {destinationPath}";
    }

    private static string GetRelativePath(string rootPath, string filePath)
    {
        try
        {
            var relative = Path.GetRelativePath(rootPath, filePath);
            if (!relative.StartsWith(".."))
            {
                return relative;
            }
        }
        catch (Exception)
        {
            // Ignorar e tentar pelo volume
        }

        var driveRoot = Path.GetPathRoot(filePath) ?? string.Empty;
        return Path.GetRelativePath(driveRoot, filePath);
    }
}
