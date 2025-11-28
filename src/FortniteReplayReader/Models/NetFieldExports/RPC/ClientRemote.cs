using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports.RPC;

[NetFieldExportGroup("/Script/FortniteGame.FortBroadcastRemoteClientInfo:ClientRemotePlayerAddMapMarker", minimalParseMode: ParseMode.Full)]
public class AddMapMarker : INetFieldExportGroup
{
    [NetFieldExport("PlayerID", RepLayoutCmdType.PropertyInt)]
    public int PlayerID { get; set; }

    [NetFieldExport("InstanceID", RepLayoutCmdType.PropertyInt)]
    public int InstanceID { get; set; }

    [NetFieldExport("Owner", RepLayoutCmdType.Ignore)]
    public int Owner { get; set; }

    [NetFieldExport("WorldPosition", RepLayoutCmdType.PropertyVector)]
    public FVector WorldPosition { get; set; }

    [NetFieldExport("WorldPositionOffset", RepLayoutCmdType.PropertyVector)]
    public FVector WorldPositionOffset { get; set; }

    [NetFieldExport("WorldNormal", RepLayoutCmdType.PropertyVector)]
    public FVector WorldNormal { get; set; }

    [NetFieldExport("ItemDefinition", RepLayoutCmdType.PropertyObject)]
    public uint ItemDefinition { get; set; }

    [NetFieldExport("ItemCount", RepLayoutCmdType.PropertyInt)]
    public int ItemCount { get; set; }

    [NetFieldExport("MarkedActor", RepLayoutCmdType.PropertyObject)]
    public uint MarkedActor { get; set; }

    [NetFieldExport("bHasCustomDisplayInfo", RepLayoutCmdType.PropertyBool)]
    public bool bHasCustomDisplayInfo { get; set; }

    [NetFieldExport("DisplayName", RepLayoutCmdType.Property)]
    public FText DisplayName { get; set; }

    [NetFieldExport("Icon", RepLayoutCmdType.PropertyString)]
    public string Icon { get; set; }

    [NetFieldExport("R", RepLayoutCmdType.PropertyFloat)]
    public float R { get; set; }

    [NetFieldExport("G", RepLayoutCmdType.PropertyFloat)]
    public float G { get; set; }

    [NetFieldExport("B", RepLayoutCmdType.PropertyFloat)]
    public float B { get; set; }

    [NetFieldExport("A", RepLayoutCmdType.PropertyFloat)]
    public float A { get; set; }
}

[NetFieldExportGroup("/Script/FortniteGame.FortBroadcastRemoteClientInfo:ClientRemotePlayerRemoveMapMarker", minimalParseMode: ParseMode.Full)]
public class RemoveMapMarker : INetFieldExportGroup
{
    [NetFieldExport("PlayerID", RepLayoutCmdType.PropertyInt)]
    public int PlayerID { get; set; }

    [NetFieldExport("InstanceID", RepLayoutCmdType.PropertyInt)]
    public int InstanceID { get; set; }
}

