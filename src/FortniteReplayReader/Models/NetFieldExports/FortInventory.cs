using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports;

[NetFieldExportClassNetCache("FortInventory_ClassNetCache")]
public class FortInventoryCache
{
    [NetFieldExportRPC("Inventory", "/Script/FortniteGame.FortInventory")]
    public FortInventory Inventory { get; set; }
}

[NetFieldExportGroup("/Script/FortniteGame.FortInventory")]
public class FortInventory : INetFieldExportGroup
{
    [NetFieldExport("RemoteRole", RepLayoutCmdType.Ignore)]
    public object RemoteRole { get; set; }

    [NetFieldExport("Owner", RepLayoutCmdType.Ignore)]
    public object Owner { get; set; }

    [NetFieldExport("Role", RepLayoutCmdType.Ignore)]
    public object Role { get; set; }

    [NetFieldExport("Count", RepLayoutCmdType.PropertyInt)]
    public int? Count { get; set; }

    [NetFieldExport("ItemDefinition", RepLayoutCmdType.Property)]
    public ItemDefinition ItemDefinition { get; set; }

    [NetFieldExport("OrderIndex", RepLayoutCmdType.PropertyUInt16)]
    public ushort? OrderIndex { get; set; }

    [NetFieldExport("Durability", RepLayoutCmdType.PropertyFloat)]
    public float? Durability { get; set; }

    [NetFieldExport("Level", RepLayoutCmdType.PropertyInt)]
    public int? Level { get; set; }

    [NetFieldExport("LoadedAmmo", RepLayoutCmdType.PropertyInt)]
    public int? LoadedAmmo { get; set; }

    [NetFieldExport("A", RepLayoutCmdType.PropertyUInt32)]
    public uint? A { get; set; }

    [NetFieldExport("B", RepLayoutCmdType.PropertyUInt32)]
    public uint? B { get; set; }

    [NetFieldExport("C", RepLayoutCmdType.PropertyUInt32)]
    public uint? C { get; set; }

    [NetFieldExport("D", RepLayoutCmdType.PropertyUInt32)]
    public uint? D { get; set; }

    [NetFieldExport("inventory_overflow_date", RepLayoutCmdType.PropertyBool)]
    public bool? InventoryOverflowDate { get; set; }

    [NetFieldExport("bWasGifted", RepLayoutCmdType.PropertyBool)]
    public bool? bWasGifted { get; set; }

    [NetFieldExport("bIsReplicatedCopy", RepLayoutCmdType.PropertyBool)]
    public bool? bIsReplicatedCopy { get; set; }

    [NetFieldExport("bIsDirty", RepLayoutCmdType.PropertyBool)]
    public bool? bIsDirty { get; set; }

    [NetFieldExport("bUpdateStatsOnCollection", RepLayoutCmdType.PropertyBool)]
    public bool? bUpdateStatsOnCollection { get; set; }

    [NetFieldExport("StateValues", RepLayoutCmdType.DynamicArray)]
    public object StateValues { get; set; }

    [NetFieldExport("ParentInventory", RepLayoutCmdType.PropertyObject)]
    public uint? ParentInventory { get; set; }

    [NetFieldExport("Handle", RepLayoutCmdType.PropertyInt)]
    public int? Handle { get; set; }

    [NetFieldExport("WrapOverride", RepLayoutCmdType.PropertyUInt32)]
    public uint? WrapOverride { get; set; }

    [NetFieldExport("AlterationInstances", RepLayoutCmdType.Ignore)]
    public object AlterationInstances { get; set; }

    [NetFieldExport("GenericAttributeValues", RepLayoutCmdType.Ignore)]
    public object GenericAttributeValues { get; set; }

    [NetFieldExport("ReplayPawn", RepLayoutCmdType.PropertyObject)]
    public uint? ReplayPawn { get; set; }

    [NetFieldExport("DataList", RepLayoutCmdType.Ignore)]
    public object DataList { get; set; }

    [NetFieldExport("RemovedDataTypes", RepLayoutCmdType.DynamicArray)]
    public uint[] RemovedDataTypes { get; set; }

    [NetFieldExport("SlotNumber", RepLayoutCmdType.PropertyInt)]
    public int? SlotNumber { get; set; }

    [NetFieldExport("ParentObject", RepLayoutCmdType.PropertyObject)]
    public uint? ParentObject { get; set; }
}

public class FortItemEntry
{
    public string ItemType { get; set; }
    public int Count { get; set; }
    public ItemDefinition Definition { get; set; }
}