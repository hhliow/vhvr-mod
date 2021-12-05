using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimVRMod.Utilities;
using ValheimVRMod.VRCore;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace ValheimVRMod.Scripts {
    public class BowLocalManager : BowManager {
        private const float attachRange = 0.2f;
        private const float incompleteDrawInaccuracyFactor = 3;

        private GameObject arrow;
        private LineRenderer predictionLine;
        private float projectileVel;
        private float projectileVelMin;
        private Outline outline;
        private Outline arrowOutline;
        private ItemDrop.ItemData item;
        private Attack attack;
        private float fullDrawDurationSecond;
        private float drawStartTimeSecond;

        // Vanilla-style restriction applied and current draw progress.
        public float drawProgressThrottle = 0;

        public static BowLocalManager instance;
        public static float attackDrawPercentage;
        public static Vector3 spawnPoint;
        public static Vector3 aimDir;

        public static bool isPulling;
        public static bool startedPulling;
        public static bool aborting;

        private readonly GameObject arrowAttach = new GameObject();
        

        private void Start() {
            instance = this;
            mainHand = getMainHand().transform;
            predictionLine = new GameObject().AddComponent<LineRenderer>();
            predictionLine.widthMultiplier = 0.03f;
            predictionLine.positionCount = 20;
            predictionLine.material.color = Color.white;
            predictionLine.enabled = false;
            predictionLine.receiveShadows = false;
            predictionLine.shadowCastingMode = ShadowCastingMode.Off;
            predictionLine.lightProbeUsage = LightProbeUsage.Off;
            predictionLine.reflectionProbeUsage = ReflectionProbeUsage.Off;
            
            arrowAttach.transform.SetParent(mainHand, false);
            
            outline = gameObject.AddComponent<Outline>();
            outline.OutlineColor = Color.red;
            outline.OutlineWidth = 10;
            outline.OutlineMode = Outline.Mode.OutlineVisible;
            outline.enabled = false;

            item = Player.m_localPlayer.GetLeftItem();
            if (item != null) {
                attack = item.m_shared.m_attack.Clone();
            }
            
        }

        protected new void OnDestroy() {
            base.OnDestroy();
            destroyArrow();
            Destroy(predictionLine);
            Destroy(arrowAttach);
        }

        private void destroyArrow() {
            if (arrow != null) {
                arrow.GetComponent<ZNetView>().Destroy();   
            }
        }

        private Hand getMainHand() {
            return VHVRConfig.LeftHanded() ? VRPlayer.leftHand : VRPlayer.rightHand;
        }        
        
        /**
     * Need to use OnRenderObject instead of Update or LateUpdate,
     * because of VRIK Bone Updates happening in LateUpdate 
     */
        protected new void OnRenderObject() {
            
            if (!initialized) {
                return;
            }
            
            base.OnRenderObject();

            var inputSource = VHVRConfig.LeftHanded() ? SteamVR_Input_Sources.LeftHand : SteamVR_Input_Sources.RightHand;
            
            if (SteamVR_Actions.valheim_Grab.GetState(inputSource)) {
                handlePulling();
            }

            if (SteamVR_Actions.valheim_Grab.GetStateUp(inputSource)) {
                releaseString();
            }

            if (predictionLine.enabled) {
                updatePredictionLine();   
            }

            updateOutline();
        }

        private void updateOutline() {
            
            if (outline.enabled && Player.m_localPlayer.HaveStamina(getStaminaUsage() + 0.1f)) {
                outline.enabled = false;
            } else if (! outline.enabled && ! Player.m_localPlayer.HaveStamina(getStaminaUsage() + 0.1f)) {
                outline.enabled = true;
            }

            if (pulling && drawProgressThrottle < 1) {
                // Use outline color to hint the draw progress throttle to the player.
                arrowOutline.OutlineColor = new Vector4(1, 0, 0, 1 - drawProgressThrottle);
                arrowOutline.enabled = true;
            } else {
                arrowOutline.enabled = false;
            }
        }
        
        private float getStaminaUsage() {
            
            if (attack.m_attackStamina <= 0.0) {
                return 0.0f;   
            }
            double attackStamina = attack.m_attackStamina;
            return (float) (attackStamina - attackStamina * 0.330000013113022 * Player.m_localPlayer.GetSkillFactor(item.m_shared.m_skillType));
        }

        private float getFullDrawDurationSecond() {
            return 0.5f + Math.Max(2.5f * (float)(1 - Player.m_localPlayer.GetSkillFactor(item.m_shared.m_skillType)), 0);
        }

        /**
     * calculate predictionline of how the arrow will fly
     */
        private void updatePredictionLine() {
            if (!predictionLine.enabled) {
                return;
            }

            Vector3 vel = aimDir * Mathf.Lerp(projectileVelMin, projectileVel, attackDrawPercentage);

            float stepLength = 0.1f;
            float stepSize = 20;
            Vector3 pos = getArrowRestPosition();
            List<Vector3> pointList = new List<Vector3>();

            for (int i = 0; i < stepSize; i++) {
                pointList.Add(pos);
                vel += Vector3.down * arrow.GetComponent<Projectile>().m_gravity * stepLength;
                pos += vel * stepLength;
            }

            predictionLine.positionCount = 20;
            predictionLine.SetPositions(pointList.ToArray());
        }

        private void handlePulling() {
            if (!pulling && !checkHandNearString()) {
                return;
            }

            if (Player.m_localPlayer.GetStamina() <= 0) {
                releaseString(true);
                return;
            }

            arrowAttach.transform.rotation = pullObj.transform.rotation;
            arrowAttach.transform.position = pullObj.transform.position;
            spawnPoint = getArrowRestPosition();
            aimDir = -transform.forward;
            var currDrawPercentage = pullPercentage();
            if (arrow != null) {
                if (currDrawPercentage > attackDrawPercentage && !VHVRConfig.RestrictBowDrawSpeed()) {
                    Player.m_localPlayer.UseStamina((currDrawPercentage - attackDrawPercentage) * 15);
                }
            }
            updateDrawProgressThrottle();
            attackDrawPercentage = currDrawPercentage;
        }

        private void updateDrawProgressThrottle() {
            float currentTimeSecond = Time.time;
            if (attackDrawPercentage <= 0 || drawStartTimeSecond > Time.time) {
                // Reset draw progress throttle.
                fullDrawDurationSecond = getFullDrawDurationSecond();
                drawStartTimeSecond = currentTimeSecond;
                return;
            }

            drawProgressThrottle = VHVRConfig.RestrictBowDrawSpeed() ? Math.Min(currentTimeSecond - drawStartTimeSecond, fullDrawDurationSecond) / fullDrawDurationSecond : 1;
        }

        private void releaseString(bool withoutShoot = false) {
            if (!pulling) {
                return;
            }

            predictionLine.enabled = false;
            pulling = isPulling = false;
            attackDrawPercentage = pullPercentage();
            spawnPoint = getArrowRestPosition();
            aimDir = -transform.forward;

            if (withoutShoot || arrow == null || attackDrawPercentage <= 0.0f) {
                if (arrow) {
                    arrowAttach.transform.localRotation = Quaternion.identity;
                    arrowAttach.transform.localPosition = Vector3.zero;
                    drawProgressThrottle = 0;
                    drawStartTimeSecond = Single.PositiveInfinity;
                    if (attackDrawPercentage <= 0.0f) {
                        aborting = true;
                    }
                }

                return;
            }

            // Add noise to the shooting direction to penalize premature release.
            float aimNoiseDegree = (1 - drawProgressThrottle) * incompleteDrawInaccuracyFactor;
            aimDir =
                Quaternion.Euler(
                    UnityEngine.Random.Range(0.0f, aimNoiseDegree),
                    UnityEngine.Random.Range(0.0f, aimNoiseDegree),
                    UnityEngine.Random.Range(0.0f, aimNoiseDegree)) * aimDir;

            // SHOOTING
            getMainHand().hapticAction.Execute(0, 0.2f, 100, 0.3f,
                VHVRConfig.LeftHanded() ? SteamVR_Input_Sources.LeftHand : SteamVR_Input_Sources.RightHand);
            destroyArrow();
        }

        private float pullPercentage() {
            return Math.Max(pullObj.transform.localPosition.z - pullStart.z, 0) / (maxPullLength - pullStart.z);
        }

        private bool checkHandNearString() {
            if (Vector3.Distance(mainHand.position, transform.TransformPoint(pullStart)) >
                attachRange) {
                return false;
            }

            if (arrow != null) {
                startedPulling = true;
                isPulling = true;
                predictionLine.enabled = VHVRConfig.UseArrowPredictionGraphic();
                attackDrawPercentage = 0;
            }

            return pulling = true;
        }

        public void toggleArrow() {
            if (arrow != null) {
                destroyArrow();
                return;
            }
            
            var ammoItem = Player.m_localPlayer.GetAmmoItem();
            
            if (ammoItem == null || ammoItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo) {
                // out of ammo
                return;
            }

            try {
                arrow = Instantiate(ammoItem.m_shared.m_attack.m_attackProjectile, arrowAttach.transform);
            } catch {
                return;
            }

            arrowOutline = arrow.AddComponent<Outline>();
            arrowOutline.OutlineColor = Color.red;
            arrowOutline.OutlineWidth = 10;
            arrowOutline.OutlineMode = Outline.Mode.OutlineVisible;
            arrowOutline.enabled = false;

            // we need to disable the Projectile Component, else the arrow will shoot out of the hands like a New Year rocket
            arrow.GetComponent<Projectile>().enabled = false;
            // also Destroy the Trail, as this produces particles when moving with arrow in hand
            Destroy(findTrail(arrow.transform));
            Destroy(arrow.GetComponentInChildren<Collider>());
            arrow.transform.localRotation = Quaternion.identity;
            arrow.transform.localPosition = new Vector3(0, 0, 1.25f);
            foreach (ParticleSystem particleSystem in arrow.GetComponentsInChildren<ParticleSystem>()) {
                particleSystem.transform.localScale *= VHVRConfig.ArrowParticleSize();
            }
            arrowAttach.transform.localRotation = Quaternion.identity;
            arrowAttach.transform.localPosition = Vector3.zero;
            drawProgressThrottle = 0;
            drawStartTimeSecond = Single.PositiveInfinity;

            var currentAttack = Player.m_localPlayer.GetCurrentWeapon().m_shared.m_attack;
            projectileVel = currentAttack.m_projectileVel + ammoItem.m_shared.m_attack.m_projectileVel;
            projectileVelMin = currentAttack.m_projectileVelMin + ammoItem.m_shared.m_attack.m_projectileVelMin;
            
        }

        private GameObject findTrail(Transform transform) {

            foreach (ParticleSystem p in transform.GetComponentsInChildren<ParticleSystem>()) {
                var go = p.gameObject;
                if (go.name == "trail") {
                    return go;
                }
            }

            return null;
        }

        private Vector3 getArrowRestPosition()
        {
            return transform.position - transform.up * VHVRConfig.ArrowRestElevation();
        }

        public bool isHoldingArrow() {
            return arrow != null;
        }

    }
}
