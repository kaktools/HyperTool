using System.Collections.ObjectModel;

namespace HyperTool.Models;

public sealed class HyperVCheckpointTreeItem
{
    public HyperVCheckpointInfo Checkpoint { get; set; } = new();

    public ObservableCollection<HyperVCheckpointTreeItem> Children { get; } = [];

    public bool IsLatest { get; set; }

    public bool IsCurrent => Checkpoint.IsCurrent;

    public string Name => Checkpoint.Name;

    public string Description => string.IsNullOrWhiteSpace(Checkpoint.Description)
        ? "-"
        : Checkpoint.Description;

    public DateTime Created => Checkpoint.Created;

    public string CreatedDisplay => Checkpoint.Created == default
        ? "-"
        : Checkpoint.Created.ToString("dd.MM.yyyy - HH:mm:ss");

    public string CurrentBadge => Checkpoint.IsCurrent ? "Aktuell" : string.Empty;

    public string RowBackground => Checkpoint.IsCurrent ? "#2245A6FF" : "Transparent";

    public string Type => Checkpoint.Type;
}
