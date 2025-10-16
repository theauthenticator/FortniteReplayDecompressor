using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports.Buildings
{
    // Use a single attribute with partial path matching
    [NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/", minimalParseMode: ParseMode.Normal)]
    public class PlayerBuild : INetFieldExportGroup
    {
        [NetFieldExport("bDestroyed", RepLayoutCmdType.Property)]
        public bool? bDestroyed { get; set; }

        [NetFieldExport("bPlayerPlaced", RepLayoutCmdType.Property)]
        public bool? bPlayerPlaced { get; set; }

        [NetFieldExport("bCollisionBlockedByPawns", RepLayoutCmdType.Property)]
        public bool? bCollisionBlockedByPawns { get; set; }

        [NetFieldExport("TeamIndex", RepLayoutCmdType.Property)]
        public byte? TeamIndex { get; set; }

        [NetFieldExport("Health", RepLayoutCmdType.Property)]
        public short? Health { get; set; }

        [NetFieldExport("EditingPlayer", RepLayoutCmdType.Property)]
        public uint? EditingPlayer { get; set; }

        [NetFieldExport("BuildingType", RepLayoutCmdType.Ignore)]
        public BuildingType? BuildingType { get; set; }

        [NetFieldExport("Owner", RepLayoutCmdType.Property)]
        public uint? Owner { get; set; }

        [NetFieldExport("InitialLocation", RepLayoutCmdType.Property)]
        public FVector? InitialLocation { get; set; }
    }

    public enum BuildingType
    {
        Wall,
        Floor,
        Stairs,
        Roof
    }
}