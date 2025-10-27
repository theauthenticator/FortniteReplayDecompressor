using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports;

/// <summary>
/// Reboot Game State Component - minimal implementation to capture actual replicated data
/// Path: /Reboot/B_RebootGameStateComponent.B_RebootGameStateComponent_C
/// </summary>
[NetFieldExportGroup("/Reboot/B_RebootGameStateComponent.B_RebootGameStateComponent_C", minimalParseMode: ParseMode.Minimal)]
public class B_RebootGameStateComponent : INetFieldExportGroup
{
    [NetFieldExport("EnhancedResurrectionLootItemEntries", RepLayoutCmdType.DynamicArray)]
    public object[] EnhancedResurrectionLootItemEntries { get; set; }
}