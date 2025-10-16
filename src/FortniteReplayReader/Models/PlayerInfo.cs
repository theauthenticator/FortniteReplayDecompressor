namespace FortniteReplayReader.Models
{
    public class PlayerInfo
    {
        public string PlayerName { get; set; }
        public uint ChannelIndex { get; set; }
        public byte TeamIndex { get; set; } 
        public uint ActorId { get; set; }
        public string PlayerId { get; set; }

        public PlayerInfo(string playerName, uint channelIndex, uint actorId, string playerId)
        {
            PlayerName = playerName;
            ChannelIndex = channelIndex;
            ActorId = actorId;
            PlayerId = playerId;
            TeamIndex = 0;
        }
    }
}