using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ColetorDeArquivos.Models;

public class DuplicateGroup
{
    public DuplicateGroup(IEnumerable<SearchHit> hits)
    {
        var occurrences = hits?.ToList() ?? throw new ArgumentNullException(nameof(hits));
        if (occurrences.Count == 0)
        {
            throw new ArgumentException("O grupo de duplicados precisa conter ao menos um item.", nameof(hits));
        }

        Occurrences = new ReadOnlyCollection<SearchHit>(occurrences);
        FileName = Occurrences[0].FileName;
        SizeDisplay = Occurrences[0].SizeDisplay;
        Count = Occurrences.Count;
    }

    public string FileName { get; }

    public string SizeDisplay { get; }

    public int Count { get; }

    public IReadOnlyList<SearchHit> Occurrences { get; }

    public string Description => $"{FileName} ({Count} ocorrência{(Count > 1 ? "s" : string.Empty)})";

    public string RootsDisplay => string.Join(", ", Occurrences.Select(hit => hit.RootPath).Distinct(StringComparer.CurrentCultureIgnoreCase));
}
