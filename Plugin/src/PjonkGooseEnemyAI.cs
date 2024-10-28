using Unity.Netcode;
using System.Diagnostics;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

namespace PjonkGooseEnemy.src.EnemyStuff;
public abstract class PjonkGooseEnemyEnemyAI : EnemyAI
{
    public EnemyAI targetEnemy;
    public bool usingElevator = false;
    public Vector3 positionOfPlayerBeforeTeleport = Vector3.zero;
    public EntranceTeleport lastUsedEntranceTeleport = null!;
    public Dictionary<EntranceTeleport, Transform[]> exitPoints = new();
    public MineshaftElevatorController? elevatorScript = null;


    public override void Start()
    {
        base.Start();
        LogIfDebugBuild(enemyType.enemyName + " Spawned.");
    }
    
    [Conditional("DEBUG")]
    public void LogIfDebugBuild(object text)
    {
        Plugin.Logger.LogInfo(text);
    }

    [ClientRpc]
    public void SetFloatAnimationClientRpc(string name, float value)
    {
        SetFloatAnimationOnLocalClient(name, value);
    }

    public void SetFloatAnimationOnLocalClient(string name, float value)
    {
        LogIfDebugBuild(name + " " + value);
        creatureAnimator.SetFloat(name, value);
    }

    [ClientRpc]
    public void SetBoolAnimationClientRpc(string name, bool active)
    {
        SetBoolAnimationOnLocalClient(name, active);
    }

    public void SetBoolAnimationOnLocalClient(string name, bool active)
    {
        LogIfDebugBuild(name + " " + active);
        creatureAnimator.SetBool(name, active);
    }

    [ServerRpc(RequireOwnership = false)]
    public void TriggerAnimationServerRpc(string triggerName)
    {
        TriggerAnimationClientRpc(triggerName);
    }

    [ClientRpc]
    public void TriggerAnimationClientRpc(string triggerName)
    {
        TriggerAnimationOnLocalClient(triggerName);
    }

    public void TriggerAnimationOnLocalClient(string triggerName)
    {
        LogIfDebugBuild(triggerName);
        creatureAnimator.SetTrigger(triggerName);
    }

    public void ToggleEnemySounds(bool toggle)
    {
        creatureSFX.enabled = toggle;
        creatureVoice.enabled = toggle;
    }
    [ClientRpc]
    public void ChangeSpeedClientRpc(float speed)
    {
        ChangeSpeedOnLocalClient(speed);
    }

    public void ChangeSpeedOnLocalClient(float speed)
    {
        agent.speed = speed;
    }
    public bool FindClosestPlayerInRange(float range) {
        PlayerControllerB closestPlayer = null;
        float minDistance = float.MaxValue;

        foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts) {
            bool onSight = player.IsSpawned && player.isPlayerControlled && !player.isPlayerDead && !player.isInHangarShipRoom && EnemyHasLineOfSightToPosition(player.transform.position, 60f, range);
            if (!onSight) continue;

            float distance = Vector3.Distance(transform.position, player.transform.position);
            bool closer = distance < minDistance;
            if (!closer) continue;

            minDistance = distance;
            closestPlayer = player;
        }
        if (closestPlayer == null) return false;

        targetPlayer = closestPlayer;
        return true;
    }

    public bool EnemyHasLineOfSightToPosition(Vector3 pos, float width = 60f, float range = 20f, float proximityAwareness = 5f) {
        if (eye == null) {
            _ = transform;
        } else {
            _ = eye;
        }

        if (Vector3.Distance(eye.position, pos) >= range || Physics.Linecast(eye.position, pos, StartOfRound.Instance.collidersAndRoomMaskAndDefault)) return false;

        Vector3 to = pos - eye.position;
        return Vector3.Angle(eye.forward, to) < width || Vector3.Distance(transform.position, pos) < proximityAwareness;
    }
    public bool IsPlayerReachable(PlayerControllerB PlayerToCheck) {
        Vector3 Position = RoundManager.Instance.GetNavMeshPosition(PlayerToCheck.transform.position, RoundManager.Instance.navHit, 2.7f);
        if (!RoundManager.Instance.GotNavMeshPositionResult) {
            LogIfDebugBuild("Player Reach Test: No Navmesh position");
            return false; 
        }
        agent.CalculatePath(Position, agent.path);
        bool HasPath = (agent.path.status == NavMeshPathStatus.PathComplete);
        LogIfDebugBuild($"Player Reach Test: {HasPath}");
        return HasPath;
    }

    public float PlayerDistanceFromShip(PlayerControllerB PlayerToCheck) {
        if(PlayerToCheck == null) return -1;
        float DistanceFromShip = Vector3.Distance(PlayerToCheck.transform.position, StartOfRound.Instance.shipBounds.transform.position);
        LogIfDebugBuild($"PlayerNearShip check: {DistanceFromShip}");
        return DistanceFromShip;
    }

    private float DistanceFromPlayer(PlayerControllerB player, bool IncludeYAxis) {
        if (player == null) return -1f;
        if (IncludeYAxis) {
            return Vector3.Distance(player.transform.position, this.transform.position);
        }
        Vector2 PlayerFlatLocation = new Vector2(player.transform.position.x, player.transform.position.z);
        Vector2 EnemyFlatLocation = new Vector2(transform.position.x, transform.position.z);
        return Vector2.Distance(PlayerFlatLocation, EnemyFlatLocation);
    }

    public bool AnimationIsFinished(string AnimName) {
        if (!creatureAnimator.GetCurrentAnimatorStateInfo(0).IsName(AnimName)) {
            LogIfDebugBuild(__getTypeName() + ": Checking for animation " + AnimName + ", but current animation is " + creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name);
            return true;
        }
        return (creatureAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f);
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetTargetServerRpc(int PlayerID) {
        SetTargetClientRpc(PlayerID);
    }

    [ClientRpc]
    public void SetTargetClientRpc(int PlayerID) {
        if (PlayerID == -1) {
            targetPlayer = null;
            LogIfDebugBuild($"Clearing target on {this}");
            return;
        }
        if (StartOfRound.Instance.allPlayerScripts[PlayerID] == null) {
            LogIfDebugBuild($"Index invalid! {this}");
            return;
        }
        targetPlayer = StartOfRound.Instance.allPlayerScripts[PlayerID];
        LogIfDebugBuild($"{this} setting target to: {targetPlayer.playerUsername}");
    }

    [ServerRpc]
    public void SetEnemyTargetServerRpc(int enemyID) {
        SetEnemyTargetClientRpc(enemyID);
    }
    [ClientRpc]
    public void SetEnemyTargetClientRpc(int enemyID) {
        if (enemyID == -1) {
            targetEnemy = null;
            LogIfDebugBuild($"Clearing Enemy target on {this}");
            return;
        }
        if (RoundManager.Instance.SpawnedEnemies[enemyID] == null) {
            LogIfDebugBuild($"Enemy Index invalid! {this}");
            return;
        }
        targetEnemy = RoundManager.Instance.SpawnedEnemies[enemyID];
        LogIfDebugBuild($"{this} setting target to: {targetEnemy.enemyType.enemyName}");
    }

    public void GoThroughEntrance(bool followingPlayer)
    {
        Vector3 destination = Vector3.zero;
        Vector3 destinationAfterTeleport = Vector3.zero;
        EntranceTeleport entranceTeleportToUse = null!;

        if (followingPlayer)
        {
            // Find the closest entrance to the player
            EntranceTeleport? closestExitPoint = null;
            foreach (var exitpoint in exitPoints.Keys)
            {
                if (closestExitPoint == null || Vector3.Distance(positionOfPlayerBeforeTeleport, exitpoint.transform.position) < Vector3.Distance(positionOfPlayerBeforeTeleport, closestExitPoint.transform.position))
                {
                    closestExitPoint = exitpoint;
                }
            }
            if (closestExitPoint != null)
            {
                entranceTeleportToUse = closestExitPoint;
                destination = closestExitPoint.entrancePoint.transform.position;
                destinationAfterTeleport = closestExitPoint.exitPoint.transform.position;
            }
        }
        else
        {
            entranceTeleportToUse = lastUsedEntranceTeleport;
            destination = lastUsedEntranceTeleport.exitPoint.transform.position;
            destinationAfterTeleport = lastUsedEntranceTeleport.entrancePoint.transform.position;
        }

        if (elevatorScript != null && NeedsElevator(destination, entranceTeleportToUse, elevatorScript))
        {
            UseTheElevator(elevatorScript);
            return;
        }
        if (Vector3.Distance(transform.position, destination) <= 3)
        {
            lastUsedEntranceTeleport = entranceTeleportToUse;
            agent.Warp(destinationAfterTeleport);
            SetEnemyOutsideServerRpc(!isOutside);
        }
        else
        {
            agent.SetDestination(destination);
        }
    }

    private bool NeedsElevator(Vector3 destination, EntranceTeleport entranceTeleportToUse, MineshaftElevatorController elevatorScript)
    {
        // Determine if the elevator is needed based on destination proximity and current position
        bool nearMainEntrance = Vector3.Distance(destination, RoundManager.FindMainEntrancePosition(true, false)) < Vector3.Distance(destination, entranceTeleportToUse.transform.position);
        bool closerToTop = Vector3.Distance(transform.position, elevatorScript.elevatorTopPoint.position) < Vector3.Distance(transform.position, elevatorScript.elevatorBottomPoint.position);
        return !isOutside && ((nearMainEntrance && !closerToTop) || (!nearMainEntrance && closerToTop));
    }

    public void UseTheElevator(MineshaftElevatorController elevatorScript)
    {
        // Determine if we need to go up or down based on current position and destination
        bool goUp = Vector3.Distance(transform.position, elevatorScript.elevatorBottomPoint.position) < Vector3.Distance(transform.position, elevatorScript.elevatorTopPoint.position);
        // Check if the elevator is finished moving
        if (elevatorScript.elevatorFinishedMoving)
        {
            if (elevatorScript.elevatorDoorOpen)
            {
                // If elevator is not called yet and is at the wrong level, call it
                if (NeedToCallElevator(elevatorScript, goUp))
                {
                    elevatorScript.CallElevatorOnServer(goUp);
                    MoveToWaitingPoint(elevatorScript, goUp);
                    return;
                }
                // Move to the inside point of the elevator if not already there
                if (Vector3.Distance(transform.position, elevatorScript.elevatorInsidePoint.position) > 1f)
                {
                    agent.SetDestination(elevatorScript.elevatorInsidePoint.position);
                }
                else if (!usingElevator)
                {
                    // Press the button to start moving the elevator
                    elevatorScript.PressElevatorButtonOnServer(true);
                    StartCoroutine(StopUsingElevator(elevatorScript));
                }
            }
        }
        else
        {
            MoveToWaitingPoint(elevatorScript, goUp);
        }
    }

    private IEnumerator StopUsingElevator(MineshaftElevatorController elevatorScript)
    {
        usingElevator = true;
        yield return new WaitForSeconds(2f);
        yield return new WaitUntil(() => elevatorScript.elevatorDoorOpen && elevatorScript.elevatorFinishedMoving);
        Plugin.ExtendedLogging("Stopped using elevator");
        usingElevator = false;
    }

    private bool NeedToCallElevator(MineshaftElevatorController elevatorScript, bool needToGoUp)
    {
        return !elevatorScript.elevatorCalled && ((!elevatorScript.elevatorIsAtBottom && needToGoUp) || (elevatorScript.elevatorIsAtBottom && !needToGoUp));
    }

    private void MoveToWaitingPoint(MineshaftElevatorController elevatorScript, bool needToGoUp)
    {
        // Elevator is currently moving
        // Move to the appropriate waiting point (bottom or top)
        if (Vector3.Distance(transform.position, elevatorScript.elevatorInsidePoint.position) > 1f)
        {
            agent.SetDestination(needToGoUp ? elevatorScript.elevatorBottomPoint.position : elevatorScript.elevatorTopPoint.position);
        }
        else
        {
            // Wait at the inside point for the elevator to arrive
            agent.SetDestination(elevatorScript.elevatorInsidePoint.position);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetEnemyOutsideServerRpc(bool setOutside)
    {
        SetEnemyOutsideClientRpc(setOutside);
    }

    [ClientRpc]
    public void SetEnemyOutsideClientRpc(bool setOutside)
    {
        Plugin.ExtendedLogging("Setting enemy outside: " + setOutside);
        this.SetEnemyOutside(setOutside);
    }
}