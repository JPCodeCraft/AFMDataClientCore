using AlbionDataAvalonia.ReferenceData;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AlbionDataAvalonia.Items.Services
{
    public class AchievementsService
    {
        public readonly record struct AchievementInfo(string Id, bool IsTemplate)
        {
            public string? Category { get; init; }
            public IReadOnlyList<string> MissionTypes { get; init; } = Array.Empty<string>();
        }

        private const string XmlUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/achievements.xml";
        private List<AchievementInfo> achievements = new();

        public IReadOnlyList<AchievementInfo> Achievements => achievements;

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing Achievements service...");
                achievements = await ReferenceDataLoader.Shared.LoadAsync(XmlUrl, ParseAchievements);
                Log.Information("Achievements service initialized.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to initialize Achievements service.");
            }
        }

        private static List<AchievementInfo> ParseAchievements(string xml)
        {
            var document = XDocument.Parse(xml);
            var root = document.Root ?? throw new InvalidDataException("Achievements XML is missing its root.");
            var loadedAchievements = new List<AchievementInfo>();
            foreach (var element in root.Elements())
            {
                var name = element.Name.LocalName;
                var isAchievement = string.Equals(name, "achievement", StringComparison.OrdinalIgnoreCase);
                var isTemplate = string.Equals(name, "templateachievement", StringComparison.OrdinalIgnoreCase);
                if (!isAchievement && !isTemplate)
                {
                    continue;
                }

                var id = element.Attribute("id")?.Value;
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidDataException("Achievement data contains an entry without an ID.");
                }
                var missionTypes = element.Descendants()
                    .Where(child => string.Equals(child.Name.LocalName, "mission", StringComparison.OrdinalIgnoreCase))
                    .Select(child => child.Attribute("type")?.Value)
                    .Prepend(element.Attribute("missiontype")?.Value)
                    .Where(type => !string.IsNullOrWhiteSpace(type))
                    .Select(type => type!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                loadedAchievements.Add(new AchievementInfo(id, isTemplate)
                {
                    Category = element.Attribute("category")?.Value,
                    MissionTypes = Array.AsReadOnly(missionTypes)
                });
            }
            if (loadedAchievements.Count == 0)
            {
                throw new InvalidDataException("Achievement data contains no achievements.");
            }
            return loadedAchievements;
        }

        public AchievementInfo GetAchievementInfoByIndex(int index)
        {
            var loadedAchievements = achievements;
            if (index >= 0 && index < loadedAchievements.Count)
            {
                return loadedAchievements[index];
            }

            return new AchievementInfo($"Unknown Achievement ({index})", false);
        }
    }
}
