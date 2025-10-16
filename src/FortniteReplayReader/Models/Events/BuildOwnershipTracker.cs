using System;
using System.Collections.Generic;
using System.Linq;
using FortniteReplayReader.Models.NetFieldExports;

namespace FortniteReplayReader.Models.Events;

public class BuildOwnershipTracker
{
    private readonly Dictionary<uint, BuildPlacement> _buildPlacements = new();
    private readonly Dictionary<uint, PlayerInfo> _players = new();
    private readonly List<BuildActionEvent> _buildActions = new();
    
    // Track when building channels open
    public void OnChannelOpened(uint channelId, string actorPath, float gameTime)
    {
        if (IsBuildingActor(actorPath))
        {
            _buildPlacements[channelId] = new BuildPlacement
            {
                ChannelId = channelId,
                ActorPath = actorPath,
                SpawnTime = gameTime,
                BuildType = ExtractBuildType(actorPath)
            };
            
            Console.WriteLine($"Build spawned: Channel {channelId}, Type: {ExtractBuildType(actorPath)}, Time: {gameTime:F2}");
        }
    }
    
    // Track player positions and actions
    public void OnPlayerUpdate(uint playerId, string playerName, int teamIndex, Vector3 position, float gameTime)
    {
        if (!_players.ContainsKey(playerId))
        {
            _players[playerId] = new PlayerInfo 
            { 
                PlayerId = playerId, 
                PlayerName = playerName,
                TeamIndex = teamIndex 
            };
        }
        
        _players[playerId].Position = position;
        _players[playerId].LastUpdateTime = gameTime;
        _players[playerId].TeamIndex = teamIndex; // Update in case it changes
    }
    
    // Track build actions from RPC events (if available)
    public void OnBuildActionRPC(uint playerId, string actionType, Vector3 position, float gameTime)
    {
        _buildActions.Add(new BuildActionEvent
        {
            PlayerId = playerId,
            ActionType = actionType,
            Position = position,
            GameTime = gameTime
        });
        
        Console.WriteLine($"Build action: Player {playerId}, Action: {actionType}, Time: {gameTime:F2}");
    }
    
    // Main method to determine build ownership
    public uint? DetermineBuildOwner(uint channelId, int teamIndex, Vector3? buildPosition = null)
    {
        if (!_buildPlacements.TryGetValue(channelId, out var build))
            return null;
            
        // Method 1: Match with RPC events (most accurate)
        var matchingAction = _buildActions
            .Where(a => Math.Abs(a.GameTime - build.SpawnTime) <= 0.1f) // Within 100ms
            .Where(a => buildPosition == null || Vector3.Distance(a.Position, buildPosition.Value) <= 5.0f)
            .OrderBy(a => Math.Abs(a.GameTime - build.SpawnTime))
            .FirstOrDefault();
            
        if (matchingAction != null)
        {
            Console.WriteLine($"Build owner found via RPC: Channel {channelId} -> Player {matchingAction.PlayerId}");
            return matchingAction.PlayerId;
        }
        
        // Method 2: Proximity + team matching (fallback)
        if (buildPosition.HasValue)
        {
            var candidates = _players.Values
                .Where(p => p.TeamIndex == teamIndex)
                .Where(p => Math.Abs(p.LastUpdateTime - build.SpawnTime) <= 1.0f) // Within 1 second
                .Where(p => Vector3.Distance(p.Position, buildPosition.Value) <= 10.0f) // Within 10 units
                .OrderBy(p => Vector3.Distance(p.Position, buildPosition.Value))
                .ToList();
                
            if (candidates.Count == 1)
            {
                Console.WriteLine($"Build owner found via proximity: Channel {channelId} -> Player {candidates[0].PlayerId}");
                return candidates[0].PlayerId;
            }
            else if (candidates.Count > 1)
            {
                Console.WriteLine($"Multiple candidates for Channel {channelId}: {string.Join(", ", candidates.Select(c => c.PlayerId))}");
                return candidates[0].PlayerId; // Return closest
            }
        }
        
        // Method 3: Team-based guess (last resort)
        var teamPlayers = _players.Values
            .Where(p => p.TeamIndex == teamIndex)
            .Where(p => Math.Abs(p.LastUpdateTime - build.SpawnTime) <= 2.0f)
            .ToList();
            
        if (teamPlayers.Count == 1)
        {
            Console.WriteLine($"Build owner guessed by team: Channel {channelId} -> Player {teamPlayers[0].PlayerId}");
            return teamPlayers[0].PlayerId;
        }
        
        Console.WriteLine($"Could not determine owner for Channel {channelId}");
        return null;
    }
    
    // Get statistics
    public void PrintStatistics()
    {
        Console.WriteLine($"\n=== BUILD OWNERSHIP STATISTICS ===");
        Console.WriteLine($"Total builds tracked: {_buildPlacements.Count}");
        Console.WriteLine($"Total players tracked: {_players.Count}");
        Console.WriteLine($"Total build actions: {_buildActions.Count}");
        
        var resolved = _buildPlacements.Values
            .Select(b => DetermineBuildOwner(b.ChannelId, 
                _players.Values.FirstOrDefault(p => p.TeamIndex != -1)?.TeamIndex ?? 0))
            .Count(owner => owner.HasValue);
            
        Console.WriteLine($"Successfully resolved: {resolved}/{_buildPlacements.Count} ({resolved * 100.0 / _buildPlacements.Count:F1}%)");
        Console.WriteLine("=====================================\n");
    }
    
    private bool IsBuildingActor(string actorPath)
    {
        return actorPath.Contains("/Building/ActorBlueprints/Player/") ||
               actorPath.Contains("PBWA_") || // Wood walls
               actorPath.Contains("PBGA_") || // Metal walls  
               actorPath.Contains("PBSA_");   // Stone walls
    }
    
    private string ExtractBuildType(string actorPath)
    {
        if (actorPath.Contains("Wall")) return "Wall";
        if (actorPath.Contains("Floor")) return "Floor";
        if (actorPath.Contains("Stair")) return "Stair";
        if (actorPath.Contains("Roof")) return "Roof";
        return "Unknown";
    }
}

public class BuildPlacement
{
    public uint ChannelId { get; set; }
    public string ActorPath { get; set; }
    public float SpawnTime { get; set; }
    public string BuildType { get; set; }
}

public class PlayerInfo
{
    public uint PlayerId { get; set; }
    public string PlayerName { get; set; }
    public int TeamIndex { get; set; } = -1;
    public Vector3 Position { get; set; }
    public float LastUpdateTime { get; set; }
}

public class BuildActionEvent
{
    public uint PlayerId { get; set; }
    public string ActionType { get; set; }
    public Vector3 Position { get; set; }
    public float GameTime { get; set; }
}

// Helper struct for positions
public struct Vector3
{
    public float X, Y, Z;
    
    public Vector3(float x, float y, float z)
    {
        X = x; Y = y; Z = z;
    }
    
    public static float Distance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y; 
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}