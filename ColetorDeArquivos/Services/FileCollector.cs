using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ColetorDeArquivos.Models;

namespace ColetorDeArquivos.Services;

public class FileCollector
{
    public IAsyncEnumerable<SearchHit> CollectAsync(IEnumerable<string> rootPaths, IReadOnlyCollection<string> extensions, bool followSymlinks, IProgress<string>? logProgress, CancellationToken cancellationToken)
    {
        var normalizedRoots = NormalizeRoots(rootPaths);
        if (normalizedRoots.Count == 0 || extensions.Count == 0)
        {
            return EmptyAsync();
        }

        var channel = Channel.CreateUnbounded<SearchHit>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _ = Task.Run(() =>
        {
            try
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
                                channel.Writer.TryWrite(hit);
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
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException ex)
            {
                logProgress?.Report("Busca cancelada pelo usuário.");
                channel.Writer.TryComplete(ex);
            }
            catch (Exception ex)
            {
                logProgress?.Report($"[ERRO] Falha na busca: {ex.Message}");
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        return channel.Reader.ReadAllAsync(cancellationToken);
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

    private static async IAsyncEnumerable<SearchHit> EmptyAsync()
    {
        await Task.CompletedTask;
        yield break;
    }
}
