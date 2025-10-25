using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ColetorDeArquivos.Models;

namespace ColetorDeArquivos.Services;

public class FileCollector
{
    public async Task CollectAsync(IEnumerable<string> rootPaths, IReadOnlyCollection<string> extensions, bool followSymlinks, IProgress<SearchHit>? hitProgress, IProgress<string>? logProgress, CancellationToken cancellationToken)
    {
        var normalizedRoots = NormalizeRoots(rootPaths);
        if (normalizedRoots.Count == 0)
        {
            return;
        }

        await Task.Run(() =>
        {
            var extSet = new HashSet<string>(extensions.Select(e => e.ToLowerInvariant()));
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = followSymlinks ? FileAttributes.None : FileAttributes.ReparsePoint
            };

            int count = 0;
            foreach (var root in normalizedRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                logProgress?.Report($"Procurando em: {root}");

                try
                {
                    foreach (var file in Directory.EnumerateFiles(root, "*", options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var extension = Path.GetExtension(file).ToLowerInvariant();
                            if (!extSet.Contains(extension))
                            {
                                continue;
                            }

                            var info = new FileInfo(file);
                            var hit = new SearchHit(info.FullName, root, info.Length, info.LastWriteTime);
                            hitProgress?.Report(hit);
                            count++;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            logProgress?.Report($"[ERRO - permissão] {file}");
                        }
                        catch (IOException ex)
                        {
                            logProgress?.Report($"[ERRO - IO] {file}: {ex.Message}");
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    logProgress?.Report($"[ERRO - permissão] ao acessar {root}");
                }
                catch (IOException ex)
                {
                    logProgress?.Report($"[ERRO - IO] ao acessar {root}: {ex.Message}");
                }
            }

            logProgress?.Report($"Busca finalizada. Encontrados: {count}");
        }, cancellationToken);
    }

    private static List<string> NormalizeRoots(IEnumerable<string> rootPaths)
    {
        var normalized = new List<string>();
        foreach (var root in rootPaths)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            try
            {
                var fullPath = Path.GetFullPath(root);
                if (Directory.Exists(fullPath))
                {
                    normalized.Add(fullPath);
                }
            }
            catch (Exception)
            {
                // Ignorar entradas inválidas
            }
        }

        return normalized;
    }
}
