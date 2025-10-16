using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FortniteReplayReader.Models
{
    public class HarvestableData
    {
        public string objectName { get; set; }
        public string fileName { get; set; }
        public string filePath { get; set; }
        public string resourceType { get; set; }
        public string resourceTier { get; set; }
        public int materialYield { get; set; }
        public string attributeCategory { get; set; }
        public string attributeSubCategory { get; set; }
        public bool hasExactTier { get; set; }
    }

    public class HarvestableDatabase
    {
        private Dictionary<string, HarvestableData> _lookupByClass = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, HarvestableData> _lookupByFileName = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _lookupByClass.Count;

        public void Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                Console.WriteLine($"⚠️ Harvestables database not found: {jsonPath}");
                Console.WriteLine($"   Starting with empty database.");
                return;
            }

            try
            {
                var json = File.ReadAllText(jsonPath);
                var harvestables = JsonSerializer.Deserialize<List<HarvestableData>>(json);

                if (harvestables == null || harvestables.Count == 0)
                {
                    Console.WriteLine($"⚠️ No harvestables loaded from {jsonPath}");
                    return;
                }

                foreach (var h in harvestables)
                {
                    // Index by object name (strip "Default__" prefix)
                    var cleanObjectName = h.objectName.Replace("Default__", "");
                    _lookupByClass[cleanObjectName] = h;

                    // Also index by file name
                    if (!string.IsNullOrEmpty(h.fileName))
                    {
                        _lookupByFileName[h.fileName] = h;
                    }
                }

                Console.WriteLine($"✅ Loaded {_lookupByClass.Count} harvestables from database");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading harvestables: {ex.Message}");
            }
        }

        public bool TryGetHarvestable(string identifier, out HarvestableData data)
        {
            // Try exact match first
            if (_lookupByClass.TryGetValue(identifier, out data))
            {
                return true;
            }

            // Try file name match
            if (_lookupByFileName.TryGetValue(identifier, out data))
            {
                return true;
            }

            // Try partial match (for variants)
            var partialMatch = _lookupByClass.Keys
                .FirstOrDefault(k => k.Contains(identifier, StringComparison.OrdinalIgnoreCase) ||
                                    identifier.Contains(k, StringComparison.OrdinalIgnoreCase));

            if (partialMatch != null)
            {
                data = _lookupByClass[partialMatch];
                return true;
            }

            data = null;
            return false;
        }

        public bool Contains(string identifier)
        {
            return TryGetHarvestable(identifier, out _);
        }

        public void PrintStatistics()
        {
            Console.WriteLine("\n=== HARVESTABLE DATABASE STATS ===");
            Console.WriteLine($"Total entries: {_lookupByClass.Count}");

            var byType = _lookupByClass.Values
                .GroupBy(h => h.resourceType)
                .OrderByDescending(g => g.Count())
                .ToList();

            foreach (var group in byType)
            {
                Console.WriteLine($"  {group.Key}: {group.Count()}");
            }

            Console.WriteLine($"================================\n");
        }
    }
}

