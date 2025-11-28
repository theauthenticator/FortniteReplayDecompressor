using Unreal.Core.Attributes;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;
using Unreal.Core.Contracts;

namespace FortniteReplayReader.Models.NetFieldExports.Items;

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Containers/Tiered_Chest_Athena.Tiered_Chest_Athena_C", minimalParseMode: ParseMode.Debug)]
public class Chest : BaseContainer
{
    [NetFieldExport("bDestroyOnPlayerBuildingPlacement", RepLayoutCmdType.PropertyBool)]
    public bool bDestroyOnPlayerBuildingPlacement { get; set; }

    [NetFieldExport("ResourceType", RepLayoutCmdType.Enum)]
    public int ResourceType { get; set; }

    [NetFieldExport("ProxyGameplayCueDamagePhysicalMagnitude", RepLayoutCmdType.Ignore)]
    public DebuggingObject ProxyGameplayCueDamagePhysicalMagnitude { get; set; }

    [NetFieldExport("EffectContext", RepLayoutCmdType.Ignore)]
    public DebuggingObject EffectContext { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Containers/Creative_Tiered_Chest.Creative_Tiered_Chest_C", minimalParseMode: ParseMode.Debug)]
public class CreativeChest : Chest
{
    [NetFieldExport("SpawnItems", RepLayoutCmdType.Property)]
    public DebuggingObject SpawnItems { get; set; }

    [NetFieldExport("PrimaryAssetName", RepLayoutCmdType.Property)]
    public DebuggingObject PrimaryAssetName { get; set; }

    [NetFieldExport("Quantity", RepLayoutCmdType.Property)]
    public DebuggingObject Quantity { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Containers/Tiered_Chest_Athena_FactionChest_NoLocks.Tiered_Chest_Athena_FactionChest_NoLocks_C", minimalParseMode: ParseMode.Debug)]
public class FactionChest : BaseContainer
{
    [NetFieldExport("bDestroyOnPlayerBuildingPlacement", RepLayoutCmdType.PropertyBool)]
    public bool? bDestroyOnPlayerBuildingPlacement { get; set; }

    [NetFieldExport("T_Faction", RepLayoutCmdType.Enum)]
    public int? Faction { get; set; }
}


[NetFieldExportGroup("/WildEstate/Environment/Terrain/Rocks/Blueprints/Common/WildEstate_Rock_Common_Large_A_Disp.WildEstate_Rock_Common_Large_A_Disp_C", minimalParseMode: ParseMode.Full)]
public class WildEstateRockCommonLargeA : INetFieldExportGroup
{

    [NetFieldExport("RemoteRole", RepLayoutCmdType.Ignore)]
    public object RemoteRole { get; set; }

    [NetFieldExport("Role", RepLayoutCmdType.Ignore)]
    public object Role { get; set; }

    [NetFieldExport("bDestroyed", RepLayoutCmdType.PropertyBool)]
    public bool? bDestroyed { get; set; }

    [NetFieldExport("bInstantDeath", RepLayoutCmdType.PropertyBool)]
    public bool? bInstantDeath { get; set; }

    [NetFieldExport("ReplicatedDrawScale3D", RepLayoutCmdType.PropertyVector100)]
    public FVector? ReplicatedDrawScale3D { get; set; }

    [NetFieldExport("Health", RepLayoutCmdType.PropertyUInt16)]  // Changed from PropertyInt
    public ushort? Health { get; set; }

    [NetFieldExport("MaxHealth", RepLayoutCmdType.PropertyUInt16)]  // Changed from PropertyInt
    public ushort? MaxHealth { get; set; }
}

[NetFieldExportGroup("/WildEstate/Environment/Terrain/Rocks/Blueprints/Common/WildEstate_Rock_Common_Medium_A_Disp.WildEstate_Rock_Common_Medium_A_Disp_C", minimalParseMode: ParseMode.Full)]
public class WildEstateRockCommonMediumA : INetFieldExportGroup
{

    [NetFieldExport("RemoteRole", RepLayoutCmdType.Ignore)]
    public object RemoteRole { get; set; }

    [NetFieldExport("Role", RepLayoutCmdType.Ignore)]
    public object Role { get; set; }

    [NetFieldExport("bDestroyed", RepLayoutCmdType.PropertyBool)]
    public bool? bDestroyed { get; set; }

    [NetFieldExport("bInstantDeath", RepLayoutCmdType.PropertyBool)]
    public bool? bInstantDeath { get; set; }

    [NetFieldExport("ReplicatedDrawScale3D", RepLayoutCmdType.PropertyVector100)]
    public FVector? ReplicatedDrawScale3D { get; set; }

    [NetFieldExport("Health", RepLayoutCmdType.PropertyUInt16)]  // Changed from PropertyInt
    public ushort? Health { get; set; }

    [NetFieldExport("MaxHealth", RepLayoutCmdType.PropertyUInt16)]  // Changed from PropertyInt
    public ushort? MaxHealth { get; set; }
}

[NetFieldExportGroup("/WildEstate/Environment/Terrain/Rocks/Blueprints/Common/WildEstate_Rock_Common_Small_A_Disp.WildEstate_Rock_Common_Small_A_Disp_C", minimalParseMode: ParseMode.Full)]
public class WildEstateRockCommonSmallA : INetFieldExportGroup
{

    [NetFieldExport("RemoteRole", RepLayoutCmdType.Ignore)]
    public object RemoteRole { get; set; }

    [NetFieldExport("Role", RepLayoutCmdType.Ignore)]
    public object Role { get; set; }

    [NetFieldExport("bDestroyed", RepLayoutCmdType.PropertyBool)]
    public bool? bDestroyed { get; set; }

    [NetFieldExport("bInstantDeath", RepLayoutCmdType.PropertyBool)]
    public bool? bInstantDeath { get; set; }

    [NetFieldExport("ReplicatedDrawScale3D", RepLayoutCmdType.PropertyVector100)]
    public FVector? ReplicatedDrawScale3D { get; set; }

    [NetFieldExport("Health", RepLayoutCmdType.PropertyUInt16)]  // Changed from PropertyInt
    public ushort? Health { get; set; }

    [NetFieldExport("MaxHealth", RepLayoutCmdType.PropertyUInt16)]  // Changed from PropertyInt
    public ushort? MaxHealth { get; set; }
}