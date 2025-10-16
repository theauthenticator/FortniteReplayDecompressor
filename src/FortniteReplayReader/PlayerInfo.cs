namespace FortniteReplayReader;

public class PlayerInfo
{
    public string PlayerId { get; set; }
    public uint ChannelIndex { get; set; }
    public uint ActorId { get; set; }
    public string DisplayName { get; set; }
    public byte TeamIndex { get; set; } = 0;

    public PlayerInfo(string playerId, uint channelIndex, uint actorId, string displayName)
    {
        PlayerId = playerId;
        ChannelIndex = channelIndex;
        ActorId = actorId;
        DisplayName = displayName;
        TeamIndex = 0;
    }
}