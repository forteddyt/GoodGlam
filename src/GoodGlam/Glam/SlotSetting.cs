namespace GoodGlam.Glam;

/// <summary>
/// Per-slot analysis settings for a single Eorzea Collection <see cref="GlamSlot"/>, keyed by
/// <see cref="GlamSlot.Key"/> in <see cref="Configuration.Slots"/>. A slot with no entry behaves as
/// "enabled, using the master threshold", so a defaulted-empty collection is fully back-compatible
/// with an older <c>config.json</c>.
/// </summary>
[Serializable]
public sealed class SlotSetting
{
    /// <summary>
    /// Whether drops in this slot are analysed at all. When false the drop is skipped in
    /// <see cref="Loot.LootWatcher"/> before any Eorzea Collection lookup and is never logged.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// This slot's own loves threshold, used only when
    /// <see cref="Configuration.PerSlotThresholds"/> is on. <c>null</c> means "not overridden yet",
    /// so the slot falls back to the master <see cref="Configuration.LovesThreshold"/> until the
    /// user edits it — making the advanced toggle a no-op change until a per-slot value is chosen.
    /// </summary>
    public int? LovesThreshold { get; set; }
}
