namespace FortniteReplayReader;

public class PlayerInfo
{
    public string PlayerId { get; set; }
    public uint ChannelIndex { get; set; }
    public uint ActorId { get; set; }
    public string DisplayName { get; set; }
    public byte TeamIndex { get; set; } = 0;
     public uint ActorGuid { get; set; }

    public PlayerInfo(string playerId, uint channelIndex, uint actorId, string displayName, uint actorGuid)
    {
        PlayerId = playerId;
        ChannelIndex = channelIndex;
        ActorId = actorId;
        DisplayName = displayName;
        ActorGuid = actorGuid;
        TeamIndex = 0;
    }
}