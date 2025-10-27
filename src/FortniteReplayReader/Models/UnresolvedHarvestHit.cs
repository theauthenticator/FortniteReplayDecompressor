namespace FortniteReplayReader.Models
{
    public class UnresolvedHarvestHit
    {
        public uint OwnerActorGuid { get; set; }
        public uint ObjectGuid { get; set; }
        public string ObjectPath { get; set; }
        public string MaterialType { get; set; }
        public float GameTime { get; set; }
        public uint ChannelIndex { get; set; }
    }
}