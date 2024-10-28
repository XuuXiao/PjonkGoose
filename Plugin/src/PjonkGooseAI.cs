using System;
using System.Collections;
using System.Linq;
using PjonkGooseEnemy.ScrapStuff;
using GameNetcodeStuff;
using PjonkGooseEnemy.src.EnemyStuff;
using PjonkGooseEnemy.Util.Spawning;
using Unity.Netcode;
using UnityEngine;
using PjonkGooseEnemy.Misc;
using UnityEngine.AI;

namespace PjonkGooseEnemy.EnemyStuff;
public class PjonkGooseAI : PjonkGooseEnemyEnemyAI
{
    private PlayerControllerB? playerWhoLastHit;
    private const float WALKING_SPEED = 5f;
    private const float SPRINTING_SPEED = 20f;
    private bool isAggro = false;
    private int playerHits = 0;
    private bool carryingPlayerBody;
    private DeadBodyInfo? bodyBeingCarried;
    public AudioClip GuardHyperVentilateClip = null!;
    public AudioClip[] HonkSounds = null!;
    public AudioClip StartStunSound = null!;
    public AudioClip EndStunSound = null!;
    public AudioClip SpawnSound = null!;
    public AudioClip HissSound = null!;
    public AudioClip EnrageSound = null!;
    public AudioClip[] FootstepSounds = null!;
    public AudioClip[] ViolentFootstepSounds = null!;
    public AudioClip[] featherSounds = null!;
    public AudioClip[] hitSounds = null!;
    public AudioClip[] deathSounds = null!;
    public Transform TopOfBody = null!;
    public GameObject nest = null!;
    public ParticleSystem featherHitParticles = null!;
    public AnimationClip stunAnimation = null!;

    private bool inAggroAnimation = false;
    private bool canKillPjonk = false;
    private int hitPjonkCount = 0;
    private GoldenEgg goldenEgg = null!;
    private static int pjonkGooseCount = 0;
    private float timeSinceHittingLocalPlayer = 0f;
    private float timeSinceAction;
    private bool holdingEgg = false;
    private bool recentlyDamaged = false;
    private bool nestCreated = false;
    private bool isNestInside = true;
    private Coroutine? recentlyDamagedCoroutine;
    private float collisionThresholdVelocity = SPRINTING_SPEED - 4f;
    private System.Random enemyRandom = null!;
    private DoorLock[]? doors;
    private bool goldenEggCreated = false;
    private Vector3 lastPosition;
    private float velocity;
    private float deltaTime = 0.05f; // Adjust this value as necessary for your update rate
    [NonSerialized] public bool pushingAway = false;

    public enum State
    {
        Spawning,
        Wandering,
        Guarding,
        ChasingPlayer,
        Death,
        Stunned,
        Idle,
        DragPlayerBodyToNest,
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        pjonkGooseCount--;
    }

    public void BaseOutsideOrInsideStart()
    {

        var outsideNodePositions = new Vector3[RoundManager.Instance.outsideAINodes.Length];
        for (int i = 0; i < RoundManager.Instance.outsideAINodes.Length; i++)
        {
            outsideNodePositions[i] = RoundManager.Instance.outsideAINodes[i].transform.position;
        }

        var insideNodePositions = new Vector3[RoundManager.Instance.insideAINodes.Length];
        for (int i = 0; i < RoundManager.Instance.insideAINodes.Length; i++)
        {
            insideNodePositions[i] = RoundManager.Instance.insideAINodes[i].transform.position;
        }

        Vector3 enemyPos = this.transform.position;
        Vector3 closestOutsideNode = Vector3.positiveInfinity;
        Vector3 closestInsideNode = Vector3.positiveInfinity;

        for (int i = 0; i < outsideNodePositions.Length; i++)
        {
            if ((outsideNodePositions[i] - enemyPos).sqrMagnitude < (closestOutsideNode - enemyPos).sqrMagnitude)
            {
                closestOutsideNode = outsideNodePositions[i];
            }
        }
        for (int i = 0; i < insideNodePositions.Length; i++)
        {
            if ((insideNodePositions[i] - enemyPos).sqrMagnitude < (closestInsideNode - enemyPos).sqrMagnitude)
            {
                closestInsideNode = insideNodePositions[i];
            }
        }

        if (!this.isOutside && ((closestOutsideNode - enemyPos).sqrMagnitude < (closestInsideNode - enemyPos).sqrMagnitude))
        {
            this.SetEnemyOutsideClientRpc(true);
            this.favoriteSpot = nest.transform;
        }
        else if (this.isOutside && ((closestOutsideNode - enemyPos).sqrMagnitude > (closestInsideNode - enemyPos).sqrMagnitude))
        {
            this.SetEnemyOutsideClientRpc(false);
            this.favoriteSpot = nest.transform;
        }
    }

    public override void Start()
    {
        base.Start();
        agent.areaMask = NavMesh.AllAreas;
        agent.radius /= 2f;
        agent.acceleration = 20f;
        creatureVoice.pitch = 1.4f;
        doors = FindObjectsOfType<DoorLock>();
        enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + 323);
        timeSinceHittingLocalPlayer = 0;
        timeSinceAction = 0;
        creatureVoice.PlayOneShot(SpawnSound);
        pjonkGooseCount++;
        this.openDoorSpeedMultiplier = 0.5f;
        if (!IsHost) return;
        lastPosition = transform.position;
        StartCoroutine(CalculateVelocity());
        StartCoroutine(PushTimer());
        ControlStateSpeedAnimationServerRpc(0f, (int)State.Spawning, false, false, false, -1, true, false);
        StartCoroutine(SpawnTimer()); // 559 state is not transferring when client hits on clients end, constantly clearing target because the egg is not held but it should keep target because recently damaged
        exitPoints = new();
        foreach (var exit in FindObjectsByType<EntranceTeleport>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
        {
            exitPoints.Add(exit, [exit.entrancePoint, exit.exitPoint]);
            if (exit.isEntranceToBuilding)
            {
                lastUsedEntranceTeleport = exit;
            }
            if (!exit.FindExitPoint())
            {
                Plugin.Logger.LogError("Something went wrong in the generation of the fire exits");
            }
        }
        elevatorScript = FindObjectOfType<MineshaftElevatorController>();
    }

    private IEnumerator PushTimer()
    {
        while (true)
        {
            pushingAway = true;
            yield return new WaitForSeconds(0.5f);
            pushingAway = false;
            yield return new WaitForSeconds(3f);
        }
    }

    private IEnumerator CalculateVelocity()
    {
        while (true)
        {
            Vector3 currentPosition = transform.position;
            velocity = Vector3.Distance(currentPosition, lastPosition) / deltaTime;
            lastPosition = currentPosition;
            yield return new WaitForSeconds(deltaTime);
        }
    }

    public IEnumerator SpawnTimer()
    {
        yield return new WaitForSeconds(1f);
        if (!nestCreated) SpawnNestServerRpc();
        if (!goldenEggCreated) SpawnEggInNestServerRpc();
        yield return new WaitForSeconds(2f);
        if (currentBehaviourStateIndex == (int)State.Stunned || currentBehaviourStateIndex == (int)State.ChasingPlayer || currentBehaviourStateIndex == (int)State.Death) yield break;
        ControlStateSpeedAnimationServerRpc(WALKING_SPEED, (int)State.Wandering, true, false, false, -1, true, false);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SpawnNestServerRpc()
    {
        nest = Instantiate(nest, this.transform.position + Vector3.down * 0.013f, Quaternion.identity, RoundManager.Instance.mapPropsContainer.transform);
        nest.GetComponent<NetworkObject>().Spawn(true);
        BaseOutsideOrInsideStart();
        SpawnNestClientRpc();
    }

    [ClientRpc]
    public void SpawnNestClientRpc() 
    {
        nestCreated = true;
        if (!isOutside) isNestInside = true;
        else isNestInside = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SpawnEggInNestServerRpc()
    {
        NetworkObjectReference go = PjonkGooseEnemyUtils.Instance.SpawnScrap(Plugin.GoldenEggItem, nest.transform.position, false, true, 0); // todo: for some reason value doesn't sync on clients
        SpawnEggInNestClientRpc(go);
    }

    [ClientRpc]
    public void SpawnEggInNestClientRpc(NetworkObjectReference go)
    {
        goldenEggCreated = true;
        GoldenEgg _goldenEgg = ((GameObject)go).GetComponent<GoldenEgg>();
        _goldenEgg.transform.localScale *= this.transform.localScale.x;
        _goldenEgg.originalScale = _goldenEgg.transform.localScale;
        _goldenEgg.spawnedOne = pjonkGooseCount >= 3 ? true : false;
        _goldenEgg.mommyAlive = !isEnemyDead;
        StartCoroutine(DelayFindingGoldenEgg(_goldenEgg));
    }

    public IEnumerator DelayFindingGoldenEgg(GoldenEgg? _goldenEgg)
    {
        yield return new WaitForSeconds(0.5f);
        if (_goldenEgg == null)
        {
            Plugin.Logger.LogError("GoldenEgg spawned null");
            yield break;
        }
        goldenEgg = _goldenEgg;
        if (IsServer) goldenEgg.transform.SetParent(StartOfRound.Instance.propsContainer, true);
        Plugin.ExtendedLogging($"Found egg in nest: {goldenEgg.itemProperties.itemName}");
    }

    [ServerRpc(RequireOwnership = false)]
    private void ControlStateSpeedAnimationServerRpc(float speed, int state, bool startSearch, bool running, bool guarding, int playerWhoStunnedIndex, bool delaySpeed, bool _isAggro)
    {
        ControlStateSpeedAnimationClientRpc(speed, state, startSearch, running, guarding, playerWhoStunnedIndex, delaySpeed, _isAggro);
    }

    [ClientRpc]
    public void ControlStateSpeedAnimationClientRpc(float speed, int state, bool startSearch, bool running, bool guarding, int playerWhoStunnedIndex, bool delaySpeed, bool _isAggro)
    {
        isAggro = _isAggro;
        if ((state == (int)State.ChasingPlayer) && delaySpeed)
        {
            inAggroAnimation = true;
            this.ChangeSpeedOnLocalClient(0);
            this.agent.velocity = Vector3.zero;
        }
        else {
            this.ChangeSpeedOnLocalClient(speed);
            inAggroAnimation = false;
        }

        if (state == (int)State.Stunned) {
            this.SetEnemyStunned(true, stunAnimation.length, StartOfRound.Instance.allPlayerScripts[playerWhoStunnedIndex]);
            this.TriggerAnimationOnLocalClient("Stunned");
        }
        if (state == (int)State.Death) {
            this.TriggerAnimationOnLocalClient("Death");
        }
        this.SetBoolAnimationOnLocalClient("Running", running);
        this.SetBoolAnimationOnLocalClient("Guarding", guarding);
        this.SetBoolAnimationOnLocalClient("Aggro", _isAggro);
        this.SetFloatAnimationOnLocalClient("MoveZ", speed);
        this.SwitchToBehaviourStateOnLocalClient((int)state);
        if (!IsHost) return;
        if (startSearch)
        {
            StartSearch(nest.transform.position);
        }
        else
        {
            StopSearch(currentSearch);
        }
    }

    public override void Update()
    {
        base.Update();
        if (isEnemyDead) return;
        if (stunNormalizedTimer > 0 && currentBehaviourStateIndex != (int)State.Stunned && IsHost) {
            if (targetPlayer == null) {
                if (playerWhoLastHit == null) {
                    var PlayerToTakeBlame = StartOfRound.Instance.allPlayerScripts.OrderBy(player => Vector3.Distance(player.transform.position, this.transform.position)).First();
                    StunGoose(PlayerToTakeBlame, false);
                } else {
                    StunGoose(playerWhoLastHit, false);
                }
            } else {
                StunGoose(targetPlayer, false);
            }
        }

        timeSinceAction += Time.deltaTime;
        if (timeSinceHittingLocalPlayer > 0) timeSinceHittingLocalPlayer -= Time.deltaTime;

        if (creatureAnimator.GetBool("Guarding") || currentBehaviourStateIndex == (int)State.Guarding) {
            var targetRotationPlayer = StartOfRound.Instance.allPlayerScripts
                .OrderBy(player => Vector3.Distance(player.transform.position, this.transform.position))
                .First();

            Vector3 targetPosition = targetRotationPlayer.transform.position;
            Vector3 direction = (targetPosition - this.transform.position).normalized;
            direction.y = 0; // Keep the y component zero to prevent vertical rotation

            if (direction != Vector3.zero) {
                Quaternion lookRotation = Quaternion.LookRotation(direction);
                this.transform.rotation = Quaternion.Slerp(this.transform.rotation, lookRotation, Time.deltaTime * 5f);
            }
        }
        
        if (!IsHost) return;
        CheckWallCollision();
        CheckForCollidingWithDoor();
    }

    private void CheckForCollidingWithDoor() {
        if (targetPlayer == null || currentBehaviourStateIndex != (int)State.ChasingPlayer || doors == null || doors.Length == 0) return;
        foreach (DoorLock door in doors)
        {
            if (door == null) continue;
            if (velocity <= collisionThresholdVelocity-1) continue;
            if (Vector3.Distance(door.transform.position, transform.position) <= 2.5f)
            {
                Plugin.Logger.LogInfo("Exploding door: " + door.transform.parent.gameObject.name);
                ExplodeDoorClientRpc(Array.IndexOf(doors, door));
                StunGoose(targetPlayer, false);
                if (recentlyDamagedCoroutine != null)
                {
                    StopCoroutine(recentlyDamagedCoroutine);
                }
                recentlyDamagedCoroutine = StartCoroutine(RecentlyDamagedCooldown(targetPlayer));
            }
        }
    }
    private void CheckWallCollision()
    {
        if (targetPlayer == null) return;
        if (currentBehaviourStateIndex == (int)State.ChasingPlayer)
        {
            if (velocity >= 17f)
            {
                Plugin.ExtendedLogging("Velocity too high: " + velocity.ToString());
                // Check for wall collision
                RaycastHit hit;
                int layerMask = StartOfRound.Instance.collidersAndRoomMask;
                if (Physics.Raycast(transform.position, transform.forward, out hit, 1f, layerMask))
                {
                    // Calculate the angle between the surface normal and the up vector
                    float angle = Vector3.Angle(hit.normal, Vector3.up);
                    if (angle >= 75f)
                    {
                        Plugin.ExtendedLogging("Wall collision detected with slope >= 70 degrees");
                        StunGoose(targetPlayer, false);
                        if (recentlyDamagedCoroutine != null)
                        {
                            StopCoroutine(recentlyDamagedCoroutine);
                        }
                        recentlyDamagedCoroutine = StartCoroutine(RecentlyDamagedCooldown(targetPlayer));
                    }
                }
            }
        }
    }

    private void StunGoose(PlayerControllerB playerWhoStunned, bool delaySpeed = true)
    {
        ControlStateSpeedAnimationServerRpc(0f, (int)State.Stunned, false, false, false, Array.IndexOf(StartOfRound.Instance.allPlayerScripts, playerWhoStunned), delaySpeed, true);
        if (holdingEgg) {
            DropEggServerRpc();
        }
        if (carryingPlayerBody) {
            DropPlayerBodyServerRpc();
        }
        StartCoroutine(StunCooldown(playerWhoStunned, false));
    }

    private IEnumerator StunCooldown(PlayerControllerB playerWhoStunned, bool delaySpeed = true)
    {
        yield return new WaitUntil(() => this.stunNormalizedTimer <= 0);
        if (isEnemyDead) yield break;

        SetTargetServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, playerWhoStunned));
        ControlStateSpeedAnimationServerRpc(SPRINTING_SPEED, (int)State.ChasingPlayer, false, true, false, Array.IndexOf(StartOfRound.Instance.allPlayerScripts, playerWhoStunned), delaySpeed, true);
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();
        if (isEnemyDead || StartOfRound.Instance.allPlayersDead || !IsHost) return;
        
        switch (currentBehaviourStateIndex)
        {
            case (int)State.Wandering:
                DoWandering();
                break;
            case (int)State.Guarding:
                DoGuarding();
                break;
            case (int)State.ChasingPlayer:
                DoChasingPlayer();
                break;
            case (int)State.DragPlayerBodyToNest:
                DoDragPlayerBodyToNest();
                break;
            default:
                break;
        }
    }

    public void DoWandering()
    {
        if (!isAggro)
        {
            if (timeSinceAction >= enemyRandom.Next(10, 20)) {
                timeSinceAction = 0;
                ControlStateSpeedAnimationServerRpc(0f, (int)State.Idle, false, false, false, -1, true, false);
                TriggerAnimationClientRpc("PerformIdleAction");
                StartCoroutine(SpawnTimer());
                return;
            }
            if (UnityEngine.Random.Range(1, 101) <= 5f) {
                HonkAnimationClientRpc();
            }
            if (HandleEnemyOrPlayerGrabbingEgg()) {
                return;
            }
            if (Vector3.Distance(goldenEgg.transform.position, nest.transform.position) > 5f)
            {
                Plugin.ExtendedLogging("Egg is not near the nest, get egg back.");
                if (goldenEgg.playerHeldBy == null)
                {
                    Plugin.ExtendedLogging("Egg is not held by any player");
                    if (targetPlayer != null) SetTargetServerRpc(-1);
                    // If not recently hit and egg is not held, go to the egg
                    if (Vector3.Distance(this.transform.position, goldenEgg.transform.position) <= 3f && !holdingEgg)
                    {
                        GrabEggServerRpc();
                        PlayMiscSoundsServerRpc(0);
                        ControlStateSpeedAnimationServerRpc(WALKING_SPEED + 10f, (int)State.Guarding, false, false, true, -1, true, false);
                        return;
                    }
                    DoPathingToDestination(goldenEgg.transform.position, goldenEgg.isInFactory, true);
                    return;
                }
            }
        }
    }

    public void DoGuarding()
    {
        // Logic for hovering near nest
        if (HandleEnemyOrPlayerGrabbingEgg()) {
            Plugin.ExtendedLogging("Egg was stolen somehow");
            return;
        }
        if (Vector3.Distance(this.transform.position, nest.transform.position) < 0.75f)
        {
            DropEggServerRpc();
            ControlStateSpeedAnimationServerRpc(0f, (int)State.Idle, false, false, true, -1, true, false);
            StartCoroutine(SpawnTimer());
        }
        else
        {
            DoPathingToDestination(nest.transform.position, isNestInside, false);
            if (goldenEgg.isHeldByEnemy) goldenEgg.EnablePhysics(true);
        }
    }

    public void DoChasingPlayer()
    {
        if (UnityEngine.Random.Range(1, 101) <= 7f && agent.speed >= 1f) {
            HonkAnimationClientRpc();
        }
        // If the golden egg is held by the player, keep chasing the player until the egg is dropped
        if (goldenEgg == null)
        {
            Plugin.ExtendedLogging("Golden egg is null");
            ControlStateSpeedAnimationServerRpc(WALKING_SPEED, (int)State.Wandering, true, false, false, -1, true, false);
            SetTargetServerRpc(-1);
            return;
        }
        if (targetPlayer == null && recentlyDamaged)
        {
            Plugin.ExtendedLogging("Target player is null"); // playerWhoLastHit is probably being set to null or smthn idk.
            SetTargetServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, playerWhoLastHit));
        }
        // Prioritize recently damaged logic
        if (recentlyDamaged && targetPlayer != null)
        {
            Plugin.ExtendedLogging("Chasing player because recently damaged");
            if (holdingEgg)
            {
                DropEggServerRpc();
            }
            if (carryingPlayerBody)
            {
                DropPlayerBodyServerRpc();
            }
            DoPathingToDestination(targetPlayer.transform.position, targetPlayer.isInsideFactory, true);
            return;
        }

        if (targetPlayer != null && goldenEgg.playerHeldBy == targetPlayer)
        {
            Plugin.ExtendedLogging("Chasing player holding the egg");
            DoPathingToDestination(targetPlayer.transform.position, targetPlayer.isInsideFactory, true);
            return;
        }
        else if (goldenEgg.playerHeldBy != null && targetPlayer != goldenEgg.playerHeldBy)
        {
            Plugin.ExtendedLogging("Changing target to player holding the egg");
            SetTargetServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, goldenEgg.playerHeldBy));
            return;
        }
        else if (goldenEgg.playerHeldBy == null)
        {
            Plugin.ExtendedLogging("Egg is not held by any player");
            if (targetPlayer != null) SetTargetServerRpc(-1);
            // If not recently hit and egg is not held, go to the egg
            if (Vector3.Distance(this.transform.position, goldenEgg.transform.position) < 3f && !holdingEgg)
            {
                GrabEggServerRpc();
                PlayMiscSoundsServerRpc(0);
                ControlStateSpeedAnimationServerRpc(WALKING_SPEED, (int)State.Guarding, false, false, true, -1, true, false);
                return;
            }
            DoPathingToDestination(goldenEgg.transform.position, goldenEgg.isInFactory, true);
            return;
        }

        if (!recentlyDamaged && goldenEgg.playerHeldBy != null && targetPlayer != goldenEgg.playerHeldBy)
        {
            Plugin.ExtendedLogging("Switching to chase player holding the egg");
            SetTargetServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, goldenEgg.playerHeldBy));
        }
    }

    public void DoDragPlayerBodyToNest()
    {
        DoPathingToDestination(nest.transform.position, isNestInside, false);
        if (HandleEnemyOrPlayerGrabbingEgg() && carryingPlayerBody)
        {
            DropPlayerBodyServerRpc();
        }
        if (Vector3.Distance(this.transform.position, nest.transform.position) < 1f)
        {
            DropPlayerBodyServerRpc();
        }
    }

    private bool HandleEnemyOrPlayerGrabbingEgg()
    {
        if (goldenEgg.isHeld && !holdingEgg)
        {
            Plugin.ExtendedLogging("Someone grabbed the egg");
            SetTargetServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, goldenEgg.playerHeldBy));
            PlayMiscSoundsServerRpc(2);
            ControlStateSpeedAnimationServerRpc(SPRINTING_SPEED, (int)State.ChasingPlayer, false, true, false, -1, true, true);
            return true;
        }
        return false;
    }

    public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
    {
        base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        if (force == 0)
        {
            Plugin.ExtendedLogging("Hit with force 0");
            return;
        }
        else if (force > 3)
        {
            force /= (int)2;
        }
        featherHitParticles.Play();
        creatureVoice.PlayOneShot(hitSounds[enemyRandom.Next(0, hitSounds.Length)]);
        if (isEnemyDead || currentBehaviourStateIndex == (int)State.Death) return;
        enemyHP -= force;
        if (IsOwner && enemyHP <= 0 && !isEnemyDead)
        {
            KillEnemyOnOwnerClient();
            return;
        }

        if (playerWhoHit != null)
        {
            Plugin.ExtendedLogging($"Player who hit: {playerWhoHit}");
            if (playerWhoHit != null && currentBehaviourStateIndex != (int)State.Stunned)
            {
                PlayerHitEnemy(playerWhoHit);
            }
            
            if (recentlyDamagedCoroutine != null)
            {
                StopCoroutine(recentlyDamagedCoroutine);
            }
            recentlyDamagedCoroutine = StartCoroutine(RecentlyDamagedCooldown(playerWhoHit));
        }
        if (playerWhoHit == null && playerWhoLastHit != null && Vector3.Distance(this.transform.position, playerWhoLastHit.transform.position) <= 20f)
        {
            Plugin.ExtendedLogging($"Player who hit: {playerWhoLastHit}");
            if (playerWhoLastHit != null && currentBehaviourStateIndex != (int)State.Stunned)
            {
                PlayerHitEnemy(playerWhoLastHit);
            }
            
            if (recentlyDamagedCoroutine != null)
            {
                StopCoroutine(recentlyDamagedCoroutine);
            }
            recentlyDamagedCoroutine = StartCoroutine(RecentlyDamagedCooldown(playerWhoLastHit));
        }
        Plugin.ExtendedLogging($"Enemy HP: {enemyHP}");
        Plugin.ExtendedLogging($"Hit with force {force}");
    }

    public void PlayerHitEnemy(PlayerControllerB? playerWhoStunned)
    {
        playerHits += 1;
        Plugin.ExtendedLogging($"PlayerHitEnemy called. Current hits: {playerHits}, Current State: {currentBehaviourStateIndex}");
        if (playerHits >= 6 && playerWhoStunned != null && currentBehaviourStateIndex != (int)State.Stunned)
        {
            playerHits = 0;
            if (!IsHost) return;
            Plugin.ExtendedLogging("Stunning Goose");
            StunGoose(playerWhoStunned, true);
        }
        else if (currentBehaviourStateIndex != (int)State.ChasingPlayer)
        {
            Plugin.ExtendedLogging("Aggro on hit");
            if (!IsHost) return;
            AggroOnHit(playerWhoStunned);
        }
    }

    public void AggroOnHit(PlayerControllerB? playerWhoStunned)
    {
        SetTargetServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, playerWhoStunned));
        PlayMiscSoundsServerRpc(2);
        ControlStateSpeedAnimationServerRpc(SPRINTING_SPEED, (int)State.ChasingPlayer, false, true, false, -1, true, true);
    }

    public IEnumerator RecentlyDamagedCooldown(PlayerControllerB? playerWhoHit) 
    {
        recentlyDamaged = true;
        playerWhoLastHit = playerWhoHit;
        if (IsHost) SetTargetServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, playerWhoHit));
        yield return new WaitForSeconds(enemyRandom.Next(10, 15));
        if (IsHost && !(currentBehaviourStateIndex != (int)State.Death || currentBehaviourStateIndex != (int)State.Stunned)) PlayMiscSoundsServerRpc(1);
        recentlyDamaged = false;
    }

    public void KillPlayerWithEgg(int playerIndex)
    {
        Plugin.ExtendedLogging("Player killed");
        StartOfRound.Instance.allPlayerScripts[playerIndex].DamagePlayer(200, true, true, CauseOfDeath.Bludgeoning, 0, false, default);
        if (Vector3.Distance(goldenEgg.transform.position, nest.transform.position) <= 0.75f)
        {
            CarryingDeadPlayerServerRpc(playerIndex);
            ControlStateSpeedAnimationServerRpc(WALKING_SPEED, (int)State.DragPlayerBodyToNest, false, false, true, -1, true, false);
        }
        else
        {
            DoPathingToDestination(goldenEgg.transform.position, goldenEgg.isInFactory, false);
        }
        SetTargetServerRpc(-1);
    }

    public override void KillEnemy(bool destroy = false) 
    {
        base.KillEnemy(destroy);
        if (IsHost)
        {
            ControlStateSpeedAnimationServerRpc(0, (int)State.Death, false, false, false, -1, true, false);
            if (holdingEgg)
            {
                DropEggServerRpc();
            }
            if (carryingPlayerBody)
            {
                DropPlayerBodyServerRpc();
            }
        }
        if (goldenEgg != null) goldenEgg.mommyAlive = false;
        var FeatherPS = this.transform.Find("GooseRig").Find("Root").Find("Torso").Find("Torso.001").Find("Feather PS").GetComponent<ParticleSystem>().main;
        FeatherPS.loop = false;
        creatureVoice.PlayOneShot(deathSounds[enemyRandom.Next(0, deathSounds.Length)]);
    }

    [ServerRpc(RequireOwnership = false)]
    private void DropEggServerRpc()
    {
        StartCoroutine(DelayDroppingEgg());
    }

    public IEnumerator DelayDroppingEgg() 
    {
        yield return new WaitForSeconds(0.25f);
        DropEggClientRpc();
    }

    [ClientRpc]
    public void DropEggClientRpc() 
    {
        DropEgg();
    }

    public void DropEgg() 
    {
        goldenEgg.parentObject = null;
        if (IsServer) goldenEgg.transform.SetParent(StartOfRound.Instance.propsContainer, true);
        goldenEgg.EnablePhysics(true);
        goldenEgg.fallTime = 0f;
        Plugin.ExtendedLogging("Dropping Egg");
        Plugin.ExtendedLogging($"Egg Position: {goldenEgg.transform.position}");
        Plugin.ExtendedLogging($"Egg Parent: {goldenEgg.transform.parent}");
        goldenEgg.startFallingPosition = goldenEgg.transform.parent.InverseTransformPoint(goldenEgg.transform.position);
        goldenEgg.targetFloorPosition = goldenEgg.transform.parent.InverseTransformPoint(goldenEgg.GetItemFloorPosition(default)); // <-- Could be causing issues?
        goldenEgg.floorYRot = -1;
        goldenEgg.DiscardItemFromEnemy();
        goldenEgg.grabbable = true;
        goldenEgg.isHeldByEnemy = false;
        holdingEgg = false;
        goldenEgg.transform.rotation = Quaternion.Euler(goldenEgg.itemProperties.restingRotation);
    }

    [ServerRpc(RequireOwnership = false)]
    public void PlayMiscSoundsServerRpc(int soundID)
    {
        PlayMiscSoundsClientRpc(soundID);
    }

    [ClientRpc]
    public void PlayMiscSoundsClientRpc(int soundID)
    {
        switch (soundID) {
            case 0:
                creatureVoice.PlayOneShot(GuardHyperVentilateClip);
                break;
            case 1:
                creatureVoice.PlayOneShot(HissSound);
                break;
            case 2:
                creatureVoice.PlayOneShot(EnrageSound);
                break;
            default:
                Plugin.ExtendedLogging($"Invalid sound ID: {soundID}");
                break;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void GrabEggServerRpc()
    {
        GrabEggClientRpc();
    }

    [ClientRpc]
    private void GrabEggClientRpc()
    {
        holdingEgg = true;
        goldenEgg.isInElevator = false;
        goldenEgg.isInShipRoom = false;
        goldenEgg.grabbable = false;
        goldenEgg.isHeldByEnemy = true;
        goldenEgg.parentObject = this.transform;
        goldenEgg.transform.position = this.transform.position + transform.up * 4f;
    }

    public override void OnCollideWithPlayer(Collider other)
    {
        if (timeSinceHittingLocalPlayer > 0f || isEnemyDead || inAggroAnimation) return;
        PlayerControllerB player = other.GetComponent<PlayerControllerB>();
        if (player == null || player != GameNetworkManager.Instance.localPlayerController && currentBehaviourStateIndex != (int)State.ChasingPlayer)
        {
            return;
        }

        if (targetPlayer == null) return;
        if (player.playerSteamId == 76561198217661947 && hitPjonkCount < 10 && !canKillPjonk)
        {
            if (hitPjonkCount == 0) HUDManager.Instance.DisplayTip("Pjonk!", "9 more hits before the goose gets angry...", true);
            timeSinceHittingLocalPlayer = 1.5f;
            Plugin.ExtendedLogging("Hitting Pjonk");
            hitPjonkCount++;
            if (hitPjonkCount == 10) canKillPjonk = true;
        }

        if (player.currentlyHeldObjectServer == goldenEgg)
        {
            TriggerAnimationServerRpc("Attack");
            KillPlayerWithEgg(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, player));
            DisplayMessageServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, player));
        }
        else
        {
            timeSinceHittingLocalPlayer = 1.5f;
            Plugin.ExtendedLogging("Hitting player");
            TriggerAnimationServerRpc("Attack");
            player.externalForces += (player.transform.position - this.transform.position).normalized * 10f;
            player.DamagePlayer(75, true, true, CauseOfDeath.Bludgeoning, 0, false, default);
            if (player.health <= 0)
            {
                DisplayMessageServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, player));
                Plugin.ExtendedLogging("Player is dead");
                HostDecisionAfterDeathServerRpc(Array.IndexOf(StartOfRound.Instance.allPlayerScripts, player));
                SetTargetServerRpc(-1);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void DropPlayerBodyServerRpc()
    {
        DropPlayerBodyClientRpc();
    }

    [ClientRpc]
    public void DropPlayerBodyClientRpc() 
    {
        DropPlayerBody();
    }

    private void DropPlayerBody()
	{
		if (!this.carryingPlayerBody || this.bodyBeingCarried == null)
		{
			return;
		}
		this.carryingPlayerBody = false;
		this.bodyBeingCarried.matchPositionExactly = false;
		this.bodyBeingCarried.attachedTo = null;
		this.bodyBeingCarried = null;
        if (currentBehaviourStateIndex == (int)State.DragPlayerBodyToNest && IsHost)
        {
            ControlStateSpeedAnimationServerRpc(0f, (int)State.Wandering, true, false, false, -1, false, false);
        }
	}

    [ServerRpc(RequireOwnership = false)]
    private void HostDecisionAfterDeathServerRpc(int playerIndex)
    {
        if (Vector3.Distance(goldenEgg.transform.position, nest.transform.position) <= 2f)
        {
            Plugin.ExtendedLogging("Carrying dead player");
            CarryingDeadPlayerClientRpc(playerIndex);
            ControlStateSpeedAnimationClientRpc(WALKING_SPEED, (int)State.DragPlayerBodyToNest, false, false, true, -1, true, false);
        }
        else
        {
            DoPathingToDestination(goldenEgg.transform.position, goldenEgg.isInFactory, false);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void DisplayMessageServerRpc(int playerIndex)
    {
        DisplayMessageClientRpc(playerIndex);
    }

    [ClientRpc]
    public void DisplayMessageClientRpc(int playerIndex)
    {
        if (StartOfRound.Instance.allPlayerScripts[playerIndex].playerSteamId == 76561198217661947)
        {
            HUDManager.Instance.DisplayTip("Pjonk!", "PJOOOOONK!", true);
            if (GameNetworkManager.Instance.localPlayerController.isPlayerDead) StartCoroutine(HideMessageAfterDelay(2f));
        }
    }

    private IEnumerator HideMessageAfterDelay(float delay) 
    {
        float timer = 0;
        while (timer < delay)
        {
            HUDManager.Instance.HideHUD(false);
            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        HUDManager.Instance.HideHUD(true);
        yield break;
    }

    [ServerRpc(RequireOwnership = false)]
    private void CarryingDeadPlayerServerRpc(int playerIndex)
    {
        CarryingDeadPlayerClientRpc(playerIndex);
    }

    [ClientRpc]
    public void CarryingDeadPlayerClientRpc(int playerIndex)
    {
        var player = StartOfRound.Instance.allPlayerScripts[playerIndex];
        carryingPlayerBody = true;
        bodyBeingCarried = player.deadBody;
        bodyBeingCarried.attachedTo = this.TopOfBody;
        bodyBeingCarried.attachedLimb = player.deadBody.bodyParts[0];
        bodyBeingCarried.matchPositionExactly = true;
    }

    [ClientRpc]
    public void ExplodeDoorClientRpc(int DoorIndex) 
    {
        if (doors == null || DoorIndex >= doors.Length) return;
        Plugin.ExtendedLogging("Exploding door: " + DoorIndex);
        DoorLock door = doors[DoorIndex];
        Utilities.CreateExplosion(door.transform.position, true, 25, 0, 4, 0, CauseOfDeath.Blast, null);
        Destroy(door.transform.parent.gameObject);
        // remove the door from the array
        doors = doors.Where((d, i) => i != DoorIndex).ToArray();
    }

    [ClientRpc]
    public void HonkAnimationClientRpc() 
    {
        creatureVoice.PlayOneShot(HonkSounds[enemyRandom.Next(0, HonkSounds.Length)]);
        TriggerAnimationOnLocalClient("Honk");
    }

    public void PlayFootstepSound() {
        creatureSFX.PlayOneShot(FootstepSounds[enemyRandom.Next(0, FootstepSounds.Length)]);
    } // Animation Event

    public void PlayViolentFootstepSound() {
        creatureSFX.PlayOneShot(ViolentFootstepSounds[enemyRandom.Next(0, ViolentFootstepSounds.Length)]);
    } // Animation Event

    public void ApplyChasingSpeed() {
        if (isEnemyDead) return;
        this.ChangeSpeedOnLocalClient(SPRINTING_SPEED);
        inAggroAnimation = false;
    } // Animation Event

    public void PlayStartStunSound() {
        creatureSFX.PlayOneShot(StartStunSound);
    } // Animation Event

    public void PlayEndStunSound() {
        creatureSFX.PlayOneShot(EndStunSound);
    } // Animation Event

    public void PlayFeatherSound() {
        creatureVoice.PlayOneShot(featherSounds[enemyRandom.Next(0, featherSounds.Length)]);
    } // Animation Event

    private Vector3 pointToGo = Vector3.zero;
    private bool DoPathingToDestination(Vector3 destination, bool destinationIsInside, bool followingPlayer)
    {
        if (!agent.enabled)
        {
            Vector3 targetPosition = pointToGo;
            float moveSpeed = 6f;  // Increased speed for a faster approach
            float arcHeight = 10f;  // Adjusted arc height for a more pronounced arc
            float distanceToTarget = Vector3.Distance(transform.position, targetPosition);

            // Calculate the new position in an arcing motion
            float normalizedDistance = Mathf.Clamp01(Vector3.Distance(transform.position, targetPosition) / distanceToTarget);
            Vector3 newPosition = Vector3.MoveTowards(transform.position, targetPosition, Time.deltaTime * moveSpeed);
            newPosition.y += Mathf.Sin(normalizedDistance * Mathf.PI) * arcHeight;

            transform.position = newPosition;
            transform.rotation = Quaternion.LookRotation(targetPosition - transform.position);
            if (Vector3.Distance(transform.position, targetPosition) <= 1f)
            {
                agent.enabled = true;
            }
            return true;
        }

        if ((isOutside && destinationIsInside) || (!isOutside && !destinationIsInside))
        {
            GoThroughEntrance(followingPlayer);
            return true;
        }

        if (!isOutside && elevatorScript != null && !usingElevator)
        {
            bool galCloserToTop = Vector3.Distance(transform.position, elevatorScript.elevatorTopPoint.position) < Vector3.Distance(transform.position, elevatorScript.elevatorBottomPoint.position);
            bool destinationCloserToTop = Vector3.Distance(destination, elevatorScript.elevatorTopPoint.position) < Vector3.Distance(destination, elevatorScript.elevatorBottomPoint.position);
            if (galCloserToTop != destinationCloserToTop)
            {
                UseTheElevator(elevatorScript);
                return true;
            }
        }
        bool playerIsInElevator = elevatorScript != null && !elevatorScript.elevatorFinishedMoving && Vector3.Distance(destination, elevatorScript.elevatorInsidePoint.position) < 3f;
        if (!usingElevator && !playerIsInElevator && DetermineIfNeedToDisableAgent(destination))
        {
            return true;
        }
        if (!usingElevator) agent.SetDestination(destination);
        if (usingElevator && elevatorScript != null) agent.Warp(elevatorScript.elevatorInsidePoint.position);
        return false;
    }

    private bool DetermineIfNeedToDisableAgent(Vector3 destination)
    {
        NavMeshPath path = new NavMeshPath();
        if ((!agent.CalculatePath(destination, path) || path.status == NavMeshPathStatus.PathPartial) && Vector3.Distance(transform.position, destination) > 7f)
        {
            agent.SetDestination(agent.pathEndPosition);
            if (Vector3.Distance(agent.transform.position, agent.pathEndPosition) <= 2)
            {
                agent.SetDestination(destination);
                if (!agent.CalculatePath(destination, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    Vector3 nearbyPoint;
                    if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    {
                        nearbyPoint = hit.position;
                        pointToGo = nearbyPoint;
                        agent.enabled = false;
                    }
                }
            }
            return true;
        }
        return false;
    }
}