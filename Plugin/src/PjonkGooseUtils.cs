using System;
using Unity.Netcode;
using UnityEngine;
using Random = System.Random;
using System.Collections.Generic;

namespace PjonkGooseEnemy.Util.Spawning;
internal class PjonkGooseEnemyUtils : NetworkBehaviour
{
    private static Random random = null!;
    internal static PjonkGooseEnemyUtils Instance { get; private set; } = null!;
    public static Dictionary<string, GameObject> Objects = new Dictionary<string, GameObject>();

    void Awake()
    {
        Instance = this;
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void SpawnScrapServerRpc(string itemName, Vector3 position, bool isQuest = false, bool defaultRotation = true, int valueIncrease = 0) {
        if (itemName == string.Empty) {
            return;
        }
        Plugin.samplePrefabs.TryGetValue(itemName, out Item item);
        if (item == null) {
            // throw for stacktrace
            throw new NullReferenceException($"'{itemName}' either isn't a PjonkGoose scrap or not registered! This method only handles PjonkGoose scrap!");
        }
        SpawnScrap(item, position, isQuest, defaultRotation, valueIncrease);
    }

    public NetworkObjectReference SpawnScrap(Item item, Vector3 position, bool isQuest, bool defaultRotation, int valueIncrease) {
        if (StartOfRound.Instance == null) {
            return default;
        }
        
        if (random == null) {
            random = new Random(StartOfRound.Instance.randomMapSeed + 85);
        }

        Transform? parent = null;
        if (parent == null) {
            parent = StartOfRound.Instance.propsContainer;
        }
        GameObject go = Instantiate(item.spawnPrefab, position + Vector3.up * 0.2f, defaultRotation == true ? Quaternion.Euler(item.restingRotation) : Quaternion.identity, parent);
        go.GetComponent<NetworkObject>().Spawn();
        int value = random.Next(item.minValue + valueIncrease, item.maxValue + valueIncrease);
        var scanNode = go.GetComponentInChildren<ScanNodeProperties>();
        scanNode.scrapValue = value;
        scanNode.subText = $"Value: ${value}";
        go.GetComponent<GrabbableObject>().scrapValue = value;
        UpdateScanNodeClientRpc(new NetworkObjectReference(go), value);
        return new NetworkObjectReference(go);
    }

    [ClientRpc]
    public void UpdateScanNodeClientRpc(NetworkObjectReference go, int value) {
        go.TryGet(out NetworkObject netObj);
        if(netObj != null)
        {
            if (netObj.gameObject.TryGetComponent(out GrabbableObject grabbableObject)) {
                grabbableObject.SetScrapValue(value);
                //Plugin.ExtendedLogging($"Scrap Value: {value}");
            }
        }
    }
}