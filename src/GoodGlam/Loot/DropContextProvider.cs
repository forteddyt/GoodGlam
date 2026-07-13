using System.Diagnostics.CodeAnalysis;
using GoodGlam.Glam;
using Lumina.Excel.Sheets;

namespace GoodGlam.Loot;

internal interface IDropContextProvider
{
    DropOccurrence Capture(DropItem item);
}

internal interface IDutyNameProvider
{
    string? GetCurrentDutyName();
}

internal sealed class DropContextProvider(
    TimeProvider timeProvider,
    IDutyNameProvider dutyNameProvider) : IDropContextProvider
{
    public DropOccurrence Capture(DropItem item)
        => new(item, timeProvider.GetLocalNow(), dutyNameProvider.GetCurrentDutyName());
}

[ExcludeFromCodeCoverage(Justification = "Reads the current territory and Lumina game sheets; exercised through the injected duty-name seam.")]
internal sealed class GameDutyNameProvider : IDutyNameProvider
{
    public string? GetCurrentDutyName()
    {
        var territories = Services.DataManager.GetExcelSheet<TerritoryType>();
        if (territories is null ||
            !territories.TryGetRow(Services.ClientState.TerritoryType, out var territory) ||
            !territory.ContentFinderCondition.IsValid)
        {
            return null;
        }

        var dutyName = territory.ContentFinderCondition.Value.Name.ExtractText();
        return string.IsNullOrWhiteSpace(dutyName) ? null : dutyName;
    }
}
