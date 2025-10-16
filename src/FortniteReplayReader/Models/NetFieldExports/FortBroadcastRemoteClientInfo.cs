using FortniteReplayReader.Models.NetFieldExports.RPC;
using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports;

[NetFieldExportClassNetCache("FortBroadcastRemoteClientInfo_ClassNetCache", minimalParseMode: ParseMode.Full)]
[NetFieldExportGroup("/Script/FortniteGame.FortBroadcastRemoteClientInfo", minimalParseMode: ParseMode.Full)]
public class FortBroadcastRemoteClientInfo : INetFieldExportGroup
{
    [NetFieldExport("RemoteRole", RepLayoutCmdType.Ignore)]
    public object RemoteRole { get; set; }

    [NetFieldExport("Owner", RepLayoutCmdType.PropertyObject)]
    public uint? Owner { get; set; }

    [NetFieldExport("Role", RepLayoutCmdType.Ignore)]
    public object Role { get; set; }

    [NetFieldExport("bRemoteIsInteracting", RepLayoutCmdType.PropertyBool)]
    public bool? bRemoteIsInteracting { get; set; }

    [NetFieldExport("RemoteEditActor", RepLayoutCmdType.PropertyObject)]
    public uint? RemoteEditActor { get; set; }

    [NetFieldExport("RemoteEditTileData", RepLayoutCmdType.Ignore)] // 272 bits - complex, ignore for now
    public object RemoteEditTileData { get; set; }

    [NetFieldExport("RemoteBuildableClass", RepLayoutCmdType.PropertyObject)]
    public uint? RemoteBuildableClass { get; set; }

    [NetFieldExport("RemoteBuildingMaterial", RepLayoutCmdType.Enum)]
    public int? RemoteBuildingMaterial { get; set; }

    [NetFieldExport("bRemoteIsFullScreenMapActive", RepLayoutCmdType.PropertyBool)]
    public bool? bRemoteIsFullScreenMapActive { get; set; }

    [NetFieldExport("bRemoteIsInventoryActive", RepLayoutCmdType.PropertyBool)]
    public bool? bRemoteIsInventoryActive { get; set; }

    [NetFieldExport("RemotePoiTagID", RepLayoutCmdType.PropertyUInt16)]
    public ushort? RemotePoiTagID { get; set; }

    [NetFieldExport("RemoteEventScore", RepLayoutCmdType.PropertyInt)]
    public int? RemoteEventScore { get; set; }

    [NetFieldExport("ParentObject", RepLayoutCmdType.PropertyObject)]
    public uint? ParentObject { get; set; }

    [NetFieldExport("bRemoteCanDBNORevive", RepLayoutCmdType.PropertyBool)]
    public bool? bRemoteCanDBNORevive { get; set; }

    [NetFieldExport("RemoteAugments", RepLayoutCmdType.Ignore)] // 72 bits - complex array/struct, ignore for now
    public object RemoteAugments { get; set; }

    [NetFieldExport("Normal", RepLayoutCmdType.PropertyVectorNormal)]
    public FVector? Normal { get; set; }

    [NetFieldExport("Position", RepLayoutCmdType.PropertyVector100)]
    public FVector? Position { get; set; }

    [NetFieldExport("HitCount", RepLayoutCmdType.PropertyInt)]
    public int? HitCount { get; set; }

    [NetFieldExportRPC("ClientRemotePlayerAddMapMarker", "/Script/FortniteGame.FortBroadcastRemoteClientInfo:ClientRemotePlayerAddMapMarker", isFunction: true, enablePropertyChecksum: false)]
    public AddMapMarker AddMapMarker { get; set; }

    [NetFieldExportRPC("ClientRemotePlayerRemoveMapMarker", "/Script/FortniteGame.FortBroadcastRemoteClientInfo:ClientRemotePlayerRemoveMapMarker", isFunction: true, enablePropertyChecksum: false)]
    public RemoveMapMarker RemoveMapMarker { get; set; }

    [NetFieldExportRPC("ClientRemotePlayerDamagedResourceBuilding", "/Script/FortniteGame.FortBroadcastRemoteClientInfo:ClientRemotePlayerDamagedResourceBuilding", isFunction: true, enablePropertyChecksum: false)]
    public PlayerDamagedResourceBuilding PlayerDamagedResourceBuilding { get; set; }

    [NetFieldExportRPC("", "", isFunction: true, enablePropertyChecksum: false)]
    public object AnyRpc { get; set; }


}