using System;
using System.Collections.Generic;
using System.Linq;
using Unreal.Core.Models;
using FortniteReplayReader;

namespace FortniteReplayReader;

public class BuildAttributionTracker
{
    // Material tracking
    private class MaterialSnapshot
    {
        public Dictionary<string, int> Materials { get; set; } = new();
        public float Timestamp { get; set; }
        public FVector? Location { get; set; }
    }

    private Dictionary<string, List<MaterialSnapshot>> _playerMaterialHistory = new();
    private Dictionary<string, Dictionary<string, int>> _currentMaterials = new();

    // Build costs (you can expand this)
    private readonly Dictionary<string, int> _buildCosts = new()
    {
        { "Wood_Wall", 10 },
        { "Wood_Floor", 10 },
        { "Wood_Stair", 10 },
        { "Wood_Roof", 10 },
        { "Stone_Wall", 10 },
        { "Stone_Floor", 10 },
        { "Stone_Stair", 10 },
        { "Stone_Roof", 10 },
        { "Metal_Wall", 10 },
        { "Metal_Floor", 10 },
        { "Metal_Stair", 10 },
        { "Metal_Roof", 10 },
    };

    // Track recent builds for pattern analysis
    private Dictionary<string, Queue<(float time, string buildType)>> _recentBuilds = new();

    // Player locations (you'll populate this from PlayerPawn updates)
    private Dictionary<string, (FVector location, float timestamp)> _playerLocations = new();

    public void UpdatePlayerMaterials(string playerId, uint? wood, uint? stone, uint? metal, float timestamp, FVector? location)
    {
        var currentMaterials = new Dictionary<string, int>();
        if (wood.HasValue) currentMaterials["Wood"] = (int)wood.Value;
        if (stone.HasValue) currentMaterials["Stone"] = (int)stone.Value;
        if (metal.HasValue) currentMaterials["Metal"] = (int)metal.Value;

        // Store current materials
        _currentMaterials[playerId] = currentMaterials;

        // Store in history
        if (!_playerMaterialHistory.ContainsKey(playerId))
        {
            _playerMaterialHistory[playerId] = new List<MaterialSnapshot>();
        }

        _playerMaterialHistory[playerId].Add(new MaterialSnapshot
        {
            Materials = new Dictionary<string, int>(currentMaterials),
            Timestamp = timestamp,
            Location = location
        });

        // Keep only last 5 seconds of history
        _playerMaterialHistory[playerId] = _playerMaterialHistory[playerId]
            .Where(s => timestamp - s.Timestamp < 5.0f)
            .ToList();
    }

    public void UpdatePlayerLocation(string playerId, FVector location, float timestamp)
    {
        _playerLocations[playerId] = (location, timestamp);
    }

    public string? DetermineBuilder(string buildType, string material, List<PlayerInfo> teamPlayers, float currentTime, FVector? buildLocation)
    {
        if (teamPlayers.Count == 1)
        {
            // Solo - easy
            return teamPlayers[0].PlayerId;
        }

        var buildKey = $"{material}_{buildType}";
        var cost = _buildCosts.GetValueOrDefault(buildKey, 10);

        var candidates = new Dictionary<string, float>(); // playerId -> confidence score

        foreach (var player in teamPlayers)
        {
            candidates[player.PlayerId] = 0f;
        }

        // Factor 1: Material Delta (50 points) - MOST IMPORTANT
        foreach (var player in teamPlayers)
        {
            var delta = GetMaterialDelta(player.PlayerId, material, cost, currentTime);
            if (delta == cost)
            {
                candidates[player.PlayerId] += 50f;
                Console.WriteLine($"  {player.PlayerId}: +50 (exact material match: -{cost} {material})");
            }
            else if (delta > 0 && delta <= cost + 2) // Close enough (accounting for slight timing issues)
            {
                candidates[player.PlayerId] += 30f;
                Console.WriteLine($"  {player.PlayerId}: +30 (close material match: -{delta} {material})");
            }
        }

        // Factor 2: Proximity (30 points)
        if (buildLocation != null)
        {
            var closest = FindClosestPlayer(buildLocation, teamPlayers.Select(p => p.PlayerId).ToList(), currentTime);
            if (closest != null)
            {
                candidates[closest] += 30f;
                Console.WriteLine($"  {closest}: +30 (closest to build location)");
            }
        }

        // Factor 3: Build Pattern (10 points)
        foreach (var player in teamPlayers)
        {
            if (_recentBuilds.ContainsKey(player.PlayerId))
            {
                var recentBuildCount = _recentBuilds[player.PlayerId]
                    .Count(b => currentTime - b.time < 2.0f);

                if (recentBuildCount > 0)
                {
                    var score = Math.Min(recentBuildCount * 3f, 10f);
                    candidates[player.PlayerId] += score;
                    Console.WriteLine($"  {player.PlayerId}: +{score} (recent building activity)");
                }
            }
        }

        var winner = candidates.OrderByDescending(x => x.Value).First();

        // Require minimum confidence threshold
        if (winner.Value >= 50f) // 50+ points = confident
        {
            Console.WriteLine($"✓ Builder determined: {winner.Key} (confidence: {winner.Value}/100)");

            // Track this build for pattern analysis
            if (!_recentBuilds.ContainsKey(winner.Key))
            {
                _recentBuilds[winner.Key] = new Queue<(float, string)>();
            }
            _recentBuilds[winner.Key].Enqueue((currentTime, buildKey));

            // Keep only last 10 builds
            while (_recentBuilds[winner.Key].Count > 10)
            {
                _recentBuilds[winner.Key].Dequeue();
            }

            return winner.Key;
        }

        Console.WriteLine($"⚠️ Low confidence ({winner.Value}/100), cannot determine builder");
        return null;
    }

    private int GetMaterialDelta(string playerId, string materialType, int expectedCost, float currentTime)
    {
        if (!_playerMaterialHistory.ContainsKey(playerId))
            return 0;

        var history = _playerMaterialHistory[playerId]
            .Where(s => currentTime - s.Timestamp < 0.5f) // Last 0.5 seconds
            .OrderBy(s => s.Timestamp)
            .ToList();

        if (history.Count < 2)
            return 0;

        var before = history[^2].Materials.GetValueOrDefault(materialType, 0);
        var after = history[^1].Materials.GetValueOrDefault(materialType, 0);

        return before - after; // Positive if materials decreased
    }

    private string? FindClosestPlayer(FVector buildLocation, List<string> candidates, float currentTime)
    {
        string? closest = null;
        float minDist = float.MaxValue;

        foreach (var playerId in candidates)
        {
            if (!_playerLocations.ContainsKey(playerId))
                continue;

            var (location, timestamp) = _playerLocations[playerId];

            // Only consider recent locations (within 1 second)
            if (currentTime - timestamp > 1.0f)
                continue;

            var dist = Vector3Distance(buildLocation, location);
            if (dist < minDist && dist < 10.0f) // Max reasonable build distance
            {
                minDist = dist;
                closest = playerId;
            }
        }

        return closest;
    }

    private float Vector3Distance(FVector a, FVector b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}