﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LethalLib.Modules;
using PjonkGoose;
using PjonkGoose.Configs;
using PjonkGooseEnemy.EnemyStuff;
using UnityEngine;

namespace PjonkGooseEnemy;
[BepInPlugin(PjonkGoose.PluginInfo.PLUGIN_GUID, PjonkGoose.PluginInfo.PLUGIN_NAME, PjonkGoose.PluginInfo.PLUGIN_VERSION)]
[BepInDependency(LethalLib.Plugin.ModGUID)] 
public class Plugin : BaseUnityPlugin {
    internal static new ManualLogSource Logger;
        public static PjonkGooseConfig BoundConfig { get; private set; } // prevent from accidently overriding the config
        public static EnemyType PjonkGooseEnemyType;
        public static Item GoldenEggItem;
        public static GameObject UtilsPrefab;
        public static Dictionary<string, Item> samplePrefabs = [];
        private readonly Harmony _harmony = new Harmony(PjonkGoose.PluginInfo.PLUGIN_GUID);

    private void Awake() {
        Logger = base.Logger;

        BoundConfig = new PjonkGooseConfig(this.Config); // Create the config with the file from here.
        Assets.PopulateAssets();
        _harmony.PatchAll(Assembly.GetExecutingAssembly());

        UtilsPrefab = Assets.MainAssetBundle.LoadAsset<GameObject>("PjonkGooseEnemyUtils");

        GoldenEggItem = Assets.MainAssetBundle.LoadAsset<Item>("PjonkEggObj");
        Utilities.FixMixerGroups(GoldenEggItem.spawnPrefab);
        NetworkPrefabs.RegisterNetworkPrefab(GoldenEggItem.spawnPrefab);
        LethalLib.Modules.Items.RegisterScrap(GoldenEggItem, 0, Levels.LevelTypes.None);
        samplePrefabs.Add("GoldenEgg", GoldenEggItem);

        PjonkGooseEnemyType = Assets.MainAssetBundle.LoadAsset<EnemyType>("PjonkGooseObj");
        TerminalNode PjonkGooseTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("PjonkGooseTN");
        TerminalKeyword PjonkGooseTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("PjonkGooseTK");
        NetworkPrefabs.RegisterNetworkPrefab(PjonkGooseEnemyType.enemyPrefab);

        NetworkPrefabs.RegisterNetworkPrefab(PjonkGooseEnemyType.enemyPrefab.GetComponent<PjonkGooseAI>().nest);
        RegisterEnemyWithConfig(BoundConfig.ConfigPjonkGooseSpawnWeights.Value, PjonkGooseEnemyType, PjonkGooseTerminalNode, PjonkGooseTerminalKeyword, BoundConfig.ConfigPjonkGoosePowerLevel.Value, BoundConfig.ConfigPjonkGooseMaxCount.Value);

        InitializeNetworkBehaviours();
        Logger.LogInfo($"Plugin {PjonkGoose.PluginInfo.PLUGIN_GUID} is loaded!");
    }

    protected void RegisterEnemyWithConfig(string configMoonRarity, EnemyType enemy, TerminalNode terminalNode, TerminalKeyword terminalKeyword, float powerLevel, int spawnCount) {
        enemy.MaxCount = spawnCount;
        enemy.PowerLevel = powerLevel;
        (Dictionary<Levels.LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType) = ConfigParsing(configMoonRarity);
        Enemies.RegisterEnemy(enemy, spawnRateByLevelType, spawnRateByCustomLevelType, terminalNode, terminalKeyword);
    }

    protected (Dictionary<Levels.LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType) ConfigParsing(string configMoonRarity) {
        Dictionary<Levels.LevelTypes, int> spawnRateByLevelType = new Dictionary<Levels.LevelTypes, int>();
        Dictionary<string, int> spawnRateByCustomLevelType = new Dictionary<string, int>();

        foreach (string entry in configMoonRarity.Split(',').Select(s => s.Trim())) {
            string[] entryParts = entry.Split(':');

            if (entryParts.Length != 2) continue;

            string name = entryParts[0];
            int spawnrate;

            if (!int.TryParse(entryParts[1], out spawnrate)) continue;

            if (System.Enum.TryParse(name, true, out Levels.LevelTypes levelType))
            {
                spawnRateByLevelType[levelType] = spawnrate;
            }
            else
            {
                // Try appending "Level" to the name and re-attempt parsing
                string modifiedName = name + "Level";
                if (System.Enum.TryParse(modifiedName, true, out levelType))
                {
                    spawnRateByLevelType[levelType] = spawnrate;
                }
                else
                {
                    spawnRateByCustomLevelType[name] = spawnrate;
                }
            }
        }
        return (spawnRateByLevelType, spawnRateByCustomLevelType);
    }

    internal static void ExtendedLogging(object text) {
        if (BoundConfig.ConfigPjonkGooseExtendedLogs.Value) {
            Logger.LogInfo(text);
        }
    }

    private void InitializeNetworkBehaviours() {
        IEnumerable<Type> types;
        try
        {
            types = Assembly.GetExecutingAssembly().GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t != null);
        }
        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                if (attributes.Length > 0)
                {
                    method.Invoke(null, null);
                }
            }
        }
    }
}

public static class Assets {
    public static AssetBundle MainAssetBundle = null;
    public static void PopulateAssets() {
        string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        MainAssetBundle = AssetBundle.LoadFromFile(Path.Combine(sAssemblyLocation, "pjonkgooseassets"));
        if (MainAssetBundle == null) {
            Plugin.Logger.LogError("Failed to load custom assets.");
            return;
        }
    }
}