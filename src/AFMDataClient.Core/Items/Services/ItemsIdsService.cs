using AlbionDataAvalonia.ReferenceData;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services
{
    public class ItemsIdsService
    {
        private class ItemIdEntry
        {
            public string UniqueName { get; set; } = string.Empty;
            public string UsName { get; set; } = string.Empty;
        }

        private class ItemJsonEntry
        {
            public string Index { get; set; } = string.Empty;
            public string UniqueName { get; set; } = string.Empty;
            public Dictionary<string, string>? LocalizedNames { get; set; }
        }

        private const string JsonUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/formatted/items.json";
        private Dictionary<int, ItemIdEntry> itemMappings = new();
        public bool IsInitialized => itemMappings.Count > 0;
        private Dictionary<string, string> itemNamesByUniqueName = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> itemIdsByUniqueName = new(StringComparer.OrdinalIgnoreCase);

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing ItemsIds service...");
                var (mappings, names, ids) = await ReferenceDataLoader.Shared.LoadAsync(JsonUrl, ParseItems);
                itemMappings = mappings;
                itemNamesByUniqueName = names;
                itemIdsByUniqueName = ids;
                Log.Information("ItemsIds service initialized.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to initialize ItemsIds service.");
            }
        }

        private static (Dictionary<int, ItemIdEntry>, Dictionary<string, string>, Dictionary<string, int>) ParseItems(string json)
        {
            var items = JsonSerializer.Deserialize<List<ItemJsonEntry>>(json)
                ?? throw new InvalidDataException("Item data is null.");
            var mappings = new Dictionary<int, ItemIdEntry>();
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (!int.TryParse(item.Index, out var id) || string.IsNullOrWhiteSpace(item.UniqueName))
                {
                    throw new InvalidDataException("Item data contains an invalid index or unique name.");
                }

                var usName = item.LocalizedNames is not null
                    && item.LocalizedNames.TryGetValue("EN-US", out var localizedUsName)
                    ? localizedUsName
                    : string.Empty;
                var resolvedUsName = string.IsNullOrWhiteSpace(usName)
                    ? item.UniqueName
                    : ItemNameFormatter.FormatUsName(item.UniqueName, usName);
                mappings.Add(id, new ItemIdEntry { UniqueName = item.UniqueName, UsName = resolvedUsName });
                names[item.UniqueName] = resolvedUsName;
                ids.TryAdd(item.UniqueName, id);
            }
            if (mappings.Count == 0)
            {
                throw new InvalidDataException("Item data contains no item mappings.");
            }
            return (mappings, names, ids);
        }

        public (string UniqueName, string UsName) GetItemById(int itemId)
        {
            if (itemMappings.TryGetValue(itemId, out var itemEntry))
            {
                return (itemEntry.UniqueName, itemEntry.UsName);
            }

            return ("Unknown Item", $"Unknown Item ({itemId})");
        }

        public int? GetItemIdByUniqueName(string uniqueName) =>
            itemIdsByUniqueName.TryGetValue(uniqueName, out var id) ? id : null;

        public string GetUsNameByUniqueName(string uniqueName)
        {
            if (itemNamesByUniqueName.TryGetValue(uniqueName, out var usName))
            {
                return usName;
            }

            return uniqueName;
        }
    }
}
