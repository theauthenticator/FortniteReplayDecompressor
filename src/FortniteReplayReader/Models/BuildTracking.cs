using System;
using System.Collections.Generic;
using System.Linq;

namespace FortniteReplayReader.Models
{
    public class PlayerBuildEvent
    {
        public uint ActorId { get; set; }
        public string PlayerId { get; set; }
        public string BuildType { get; set; }
        public string Material { get; set; }
        public float GameTime { get; set; }
        public bool IsDestroyed { get; set; }
        public bool IsPlayerPlaced { get; set; }
        public int TeamIndex { get; set; }
        public int Health { get; set; }
        public uint? EditingPlayer { get; set; }
        public string ExportPath { get; set; }
    }

    public class BuildStats
    {
        public int TotalBuildsPlaced { get; set; }
        public int WoodBuilds { get; set; }
        public int StoneBuilds { get; set; }
        public int MetalBuilds { get; set; }
        public int WallsPlaced { get; set; }
        public int FloorsPlaced { get; set; }
        public int StairsPlaced { get; set; }
        public int RoofsPlaced { get; set; }
        public int BuildsDestroyed { get; set; }
        public int BuildsEdited { get; set; }
    }

    public class BuildTracker
    {
        private Dictionary<string, List<PlayerBuildEvent>> playerBuilds = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, BuildStats> playerBuildStats = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<uint, string> actorToPlayer = new();

        // Sanity check counters
        private int totalRecordBuildCalls = 0;
        private int skippedNotPlayerPlaced = 0;
        private int unknownPlayerBuilds = 0;
        private int mappedPlayerBuilds = 0;
        private int unknownBuildTypes = 0;
        private int unknownMaterials = 0;
        private HashSet<string> uniqueExportPaths = new();

        public void MapActorToPlayer(uint actorId, string playerId)
        {
            if (actorId != 0 && !string.IsNullOrEmpty(playerId))
            {
                actorToPlayer[actorId] = playerId;
            }
        }

        public int GetBuildCount(string playerId, string material, string buildType)
        {
            if (!playerBuilds.ContainsKey(playerId))
                return 0;

            var builds = playerBuilds[playerId];

            return builds.Count(b =>
                (string.IsNullOrEmpty(material) || b.Material.Equals(material, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(buildType) || b.BuildType.Equals(buildType, StringComparison.OrdinalIgnoreCase)) &&
                b.IsPlayerPlaced && !b.IsDestroyed);
        }

        public void RecordBuild(uint actorId, string playerId, string exportPath,
            bool isPlayerPlaced, bool isDestroyed, int teamIndex, int health,
            uint? editingPlayer, float gameTime)
        {
            totalRecordBuildCalls++;

            if (!isPlayerPlaced)
            {
                skippedNotPlayerPlaced++;
                return;
            }

            var buildType = DetermineBuildType(exportPath);
            var material = DetermineMaterial(exportPath);

            // Track unique export paths
            uniqueExportPaths.Add(exportPath);

            // Sanity check: Unknown build types or materials
            if (buildType == "Unknown")
            {
                unknownBuildTypes++;
                if (unknownBuildTypes <= 3)
                {
                    Console.WriteLine($"[WARNING] Unknown build type for path: '{exportPath}'");
                }
            }

            if (material == "Unknown")
            {
                unknownMaterials++;
                if (unknownMaterials <= 3)
                {
                    Console.WriteLine($"[WARNING] Unknown material for path: '{exportPath}'");
                }
            }

            // If we don't know the player, try to find them
            if (string.IsNullOrEmpty(playerId) || playerId == "Unknown")
            {
                unknownPlayerBuilds++;
                if (actorToPlayer.TryGetValue(actorId, out var mappedPlayer))
                {
                    playerId = mappedPlayer;
                    mappedPlayerBuilds++;
                }
            }

            // Initialize player stats if needed
            if (!playerBuildStats.ContainsKey(playerId))
            {
                playerBuilds[playerId] = new List<PlayerBuildEvent>();
                playerBuildStats[playerId] = new BuildStats();
            }

            var build = new PlayerBuildEvent
            {
                ActorId = actorId,
                PlayerId = playerId,
                BuildType = buildType,
                Material = material,
                GameTime = gameTime,
                IsDestroyed = isDestroyed,
                IsPlayerPlaced = isPlayerPlaced,
                TeamIndex = teamIndex,
                Health = health,
                EditingPlayer = editingPlayer,
                ExportPath = exportPath
            };

            // Always count as a new build placement
            playerBuilds[playerId].Add(build);
            var stats = playerBuildStats[playerId];
            stats.TotalBuildsPlaced++;

            // Count by material
            switch (material.ToLower())
            {
                case "wood": stats.WoodBuilds++; break;
                case "stone": stats.StoneBuilds++; break;
                case "metal": stats.MetalBuilds++; break;
            }

            // Count by type
            switch (buildType.ToLower())
            {
                case "wall": stats.WallsPlaced++; break;
                case "floor": stats.FloorsPlaced++; break;
                case "stairs": stats.StairsPlaced++; break;
                case "roof": stats.RoofsPlaced++; break;
            }

            // Track edits and destroys
            if (editingPlayer.HasValue && editingPlayer.Value != 0)
            {
                stats.BuildsEdited++;
            }
            if (isDestroyed)
            {
                stats.BuildsDestroyed++;
            }
        }

        private string DetermineBuildType(string path)
        {
            var pathLower = path.ToLower();
            if (pathLower.Contains("wall")) return "Wall";
            if (pathLower.Contains("floor")) return "Floor";
            if (pathLower.Contains("stair") || pathLower.Contains("ramp")) return "Stairs";
            if (pathLower.Contains("roof") || pathLower.Contains("cone")) return "Roof";
            return "Unknown";
        }

        private string DetermineMaterial(string path)
        {
            var pathLower = path.ToLower();
            if (pathLower.Contains("wood")) return "Wood";
            if (pathLower.Contains("stone") || pathLower.Contains("brick")) return "Stone";
            if (pathLower.Contains("metal")) return "Metal";
            return "Unknown";
        }

        public BuildStats GetPlayerStats(string playerId)
        {
            return playerBuildStats.ContainsKey(playerId) ? playerBuildStats[playerId] : new BuildStats();
        }

        public Dictionary<string, BuildStats> GetAllPlayerStats()
        {
            return playerBuildStats;
        }

        public void PrintSanityChecks()
        {
            Console.WriteLine("\n=== SANITY CHECK REPORT ===");
            Console.WriteLine($"Total RecordBuild calls: {totalRecordBuildCalls}");
            Console.WriteLine($"Skipped (not player-placed): {skippedNotPlayerPlaced}");
            Console.WriteLine($"Builds with unknown player: {unknownPlayerBuilds}");
            Console.WriteLine($"Successfully mapped via actor: {mappedPlayerBuilds}");
            Console.WriteLine($"Unknown build types: {unknownBuildTypes}");
            Console.WriteLine($"Unknown materials: {unknownMaterials}");
            Console.WriteLine($"Unique export paths seen: {uniqueExportPaths.Count}");
            Console.WriteLine($"Total actor mappings: {actorToPlayer.Count}");
            Console.WriteLine($"Players with builds: {playerBuildStats.Count}");

            var suspiciousPlayers = playerBuildStats.Where(p => p.Value.TotalBuildsPlaced < 3).ToList();
            if (suspiciousPlayers.Any())
            {
                Console.WriteLine($"\n[INFO] {suspiciousPlayers.Count} players with <3 builds");
            }

            if (uniqueExportPaths.Any())
            {
                Console.WriteLine($"\nSample export paths ({Math.Min(5, uniqueExportPaths.Count)} of {uniqueExportPaths.Count}):");
                foreach (var path in uniqueExportPaths.Take(5))
                {
                    Console.WriteLine($"  {path}");
                }
            }
        }

        public void PrintBuildSummary()
        {
            Console.WriteLine("\n=== BUILD TRACKING SUMMARY ===");
            Console.WriteLine($"Players with builds: {playerBuildStats.Count}");

            var topBuilders = playerBuildStats
                .OrderByDescending(x => x.Value.TotalBuildsPlaced)
                .Take(10);

            Console.WriteLine("\nTOP 10 BUILDERS:");
            foreach (var builder in topBuilders)
            {
                var stats = builder.Value;
                Console.WriteLine($"\n{builder.Key}:");
                Console.WriteLine($"  Total: {stats.TotalBuildsPlaced} builds");
                Console.WriteLine($"  Materials: Wood={stats.WoodBuilds}, Stone={stats.StoneBuilds}, Metal={stats.MetalBuilds}");
                Console.WriteLine($"  Types: Walls={stats.WallsPlaced}, Floors={stats.FloorsPlaced}, Stairs={stats.StairsPlaced}, Roofs={stats.RoofsPlaced}");
                Console.WriteLine($"  Edits: {stats.BuildsEdited}, Destroyed: {stats.BuildsDestroyed}");
            }

            var totalBuilds = playerBuildStats.Values.Sum(x => x.TotalBuildsPlaced);
            var totalWood = playerBuildStats.Values.Sum(x => x.WoodBuilds);
            var totalStone = playerBuildStats.Values.Sum(x => x.StoneBuilds);
            var totalMetal = playerBuildStats.Values.Sum(x => x.MetalBuilds);

            Console.WriteLine($"\n=== MATCH TOTALS ===");
            Console.WriteLine($"Total builds: {totalBuilds}");
            if (totalBuilds > 0)
            {
                Console.WriteLine($"Wood: {totalWood} ({100.0 * totalWood / totalBuilds:F1}%)");
                Console.WriteLine($"Stone: {totalStone} ({100.0 * totalStone / totalBuilds:F1}%)");
                Console.WriteLine($"Metal: {totalMetal} ({100.0 * totalMetal / totalBuilds:F1}%)");
            }
        }
    }
}