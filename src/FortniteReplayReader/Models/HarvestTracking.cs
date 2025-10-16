using System;
using System.Collections.Generic;
using System.Linq;

namespace FortniteReplayReader.Models
{
    public class HarvestEvent
    {
        public string PlayerId { get; set; }
        public string MaterialType { get; set; }
        public int Count { get; set; }
        public float GameTime { get; set; }
        public bool HitWeakspot { get; set; }
    }

    public class HarvestStats
    {
        public int TotalMaterialsHarvested { get; set; }
        public int WoodHarvested { get; set; }
        public int StoneHarvested { get; set; }
        public int MetalHarvested { get; set; }
        public int HarvestActions { get; set; }
    }

    public class HarvestTracker
    {
        private Dictionary<string, List<HarvestEvent>> playerHarvests = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, HarvestStats> playerHarvestStats = new(StringComparer.OrdinalIgnoreCase);

        public void RecordDirectHarvest(string playerId, string materialType, int amount, bool hitWeakspot, float gameTime)
        {
            if (string.IsNullOrEmpty(playerId) || playerId == "Unknown") return;

            // Initialize if needed
            if (!playerHarvestStats.ContainsKey(playerId))
            {
                playerHarvests[playerId] = new List<HarvestEvent>();
                playerHarvestStats[playerId] = new HarvestStats();
            }

            // Record the event
            var harvestEvent = new HarvestEvent
            {
                PlayerId = playerId,
                MaterialType = materialType,
                Count = amount,
                GameTime = gameTime,
                HitWeakspot = hitWeakspot
            };

            playerHarvests[playerId].Add(harvestEvent);

            // Update stats
            var stats = playerHarvestStats[playerId];
            stats.TotalMaterialsHarvested += amount;
            stats.HarvestActions++;

            switch (materialType)
            {
                case "Wood": 
                    stats.WoodHarvested += amount; 
                    break;
                case "Stone": 
                    stats.StoneHarvested += amount; 
                    break;
                case "Metal": 
                    stats.MetalHarvested += amount; 
                    break;
            }
        }

        public HarvestStats GetPlayerStats(string playerId)
        {
            return playerHarvestStats.ContainsKey(playerId) 
                ? playerHarvestStats[playerId] 
                : new HarvestStats();
        }

        public Dictionary<string, HarvestStats> GetAllPlayerStats()
        {
            return playerHarvestStats;
        }

        public void PrintHarvestSummary()
        {
            Console.WriteLine("\n=== HARVEST TRACKING SUMMARY ===");
            Console.WriteLine($"Players with harvest data: {playerHarvestStats.Count}");

            if (playerHarvestStats.Count == 0)
            {
                Console.WriteLine("No harvest data recorded.");
                return;
            }

            var topHarvesters = playerHarvestStats
                .OrderByDescending(x => x.Value.TotalMaterialsHarvested)
                .Take(10);

            Console.WriteLine("\nTOP 10 HARVESTERS:");
            foreach (var harvester in topHarvesters)
            {
                var stats = harvester.Value;
                Console.WriteLine($"\n{harvester.Key}:");
                Console.WriteLine($"  Total Harvested: {stats.TotalMaterialsHarvested}");
                Console.WriteLine($"  Wood: {stats.WoodHarvested}, Stone: {stats.StoneHarvested}, Metal: {stats.MetalHarvested}");
                Console.WriteLine($"  Harvest Actions: {stats.HarvestActions}");
            }

            var totalMats = playerHarvestStats.Values.Sum(x => x.TotalMaterialsHarvested);
            var totalWood = playerHarvestStats.Values.Sum(x => x.WoodHarvested);
            var totalStone = playerHarvestStats.Values.Sum(x => x.StoneHarvested);
            var totalMetal = playerHarvestStats.Values.Sum(x => x.MetalHarvested);
            var totalActions = playerHarvestStats.Values.Sum(x => x.HarvestActions);

            Console.WriteLine($"\n=== MATCH TOTALS ===");
            Console.WriteLine($"Total materials harvested: {totalMats}");
            Console.WriteLine($"Wood: {totalWood}, Stone: {totalStone}, Metal: {totalMetal}");
            Console.WriteLine($"Total harvest actions: {totalActions}");
            if (totalActions > 0)
            {
                Console.WriteLine($"Average materials per action: {(float)totalMats / totalActions:F1}");
            }
        }
    }
}