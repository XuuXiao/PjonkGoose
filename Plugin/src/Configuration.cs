
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace PjonkGoose.Configs {
    public class PjonkGooseConfig {
        // Enables/Disables
        public ConfigEntry<string> ConfigPjonkGooseSpawnWeights { get; private set; }
        public ConfigEntry<int> ConfigPjonkGooseMaxCount { get; private set; }
        public ConfigEntry<int> ConfigPjonkGoosePowerLevel { get; private set; }
        public ConfigEntry<int> ConfigPjonkGooseOffSpringMaxCount { get; private set; }
        public ConfigEntry<bool> ConfigPjonkGooseExtendedLogs { get; private set; }

        public PjonkGooseConfig(ConfigFile configFile) {
            ConfigPjonkGooseExtendedLogs = configFile.Bind("PjonkGoose Options",
                                                "Pjonk Goose | Extended Logs",
                                                false,
                                                "Extended Logs for the Pjonk Goose.");
            ConfigPjonkGoosePowerLevel = configFile.Bind("PjonkGoose Options",
                                                "Pjonk Goose | Power Level",
                                                3,
                                                "Power Level of the Pjonk Goose in the moon.");
            ConfigPjonkGooseSpawnWeights = configFile.Bind("PjonkGoose Options",
                                                "Pjonk Goose | Spawn Weights",
                                                "All:66,Modded:66,Vanilla:66",
                                                "Spawn Weight of the Pjonk Goose in moons.");

            ConfigPjonkGooseMaxCount = configFile.Bind("PjonkGoose Options",
                                                "Pjonk Goose | Max Count",
                                                1,
                                                "Max Count of the PjonkGoose that spawn naturally in the moon.");
            ConfigPjonkGooseOffSpringMaxCount = configFile.Bind("PjonkGoose Options",
                                                "Pjonk Goose | Off-Spring Max Count",
                                                3,
                                                "Max Count of the PjonkGoose that spawn when they are spawned through as off-spring in the moon.");
			ClearUnusedEntries(configFile);
        }
        
        private void ClearUnusedEntries(ConfigFile configFile) {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            PropertyInfo orphanedEntriesProp = configFile.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(configFile, null);
            orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
            configFile.Save(); // Save the config file to save these changes
        }
    }
}