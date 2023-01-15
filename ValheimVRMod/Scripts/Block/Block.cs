using System.Collections.Generic;
using UnityEngine;
using ValheimVRMod.Utilities;
using Valve.VR;

namespace ValheimVRMod.Scripts.Block {
    public abstract class Block : MonoBehaviour {
        public const float BlockBoxTolerance = 0.05f;

        // CONST
        private const float cooldown = 1;
        private const int maxSnapshots = 7;
        protected const float blockTimerParry = 0.1f;
        protected const float minDist = 0.4f;
        public const float blockTimerTolerance = blockTimerParry + 0.2f;
        public const float blockTimerNonParry = 9999f;

        // VARIABLE
        private int tickCounter;
        protected bool _blocking;
        protected List<Vector3> snapshots = new List<Vector3>();
        protected List<Vector3> snapshotsLeft = new List<Vector3>();
        protected Transform hand;
        protected Transform offhand;
        protected MeshCooldown _meshCooldown;
        public float blockTimer = blockTimerNonParry;
        protected SteamVR_Input_Sources mainHandSource = VHVRConfig.LeftHanded() ? SteamVR_Input_Sources.LeftHand : SteamVR_Input_Sources.RightHand;
        protected SteamVR_Input_Sources offHandSource = VHVRConfig.LeftHanded() ? SteamVR_Input_Sources.RightHand : SteamVR_Input_Sources.LeftHand;
        protected SteamVR_Input_Sources currhand = VHVRConfig.LeftHanded() ? SteamVR_Input_Sources.RightHand : SteamVR_Input_Sources.LeftHand;
        protected bool wasParryStart = false;
        public bool wasResetTimer = false;
        public bool wasGetHit = false;

        private PhysicsEstimator physicsEstimator;

        private LineRenderer lineRenderer;

        protected Bounds blockBounds {
            get
            {
                Mesh mesh = gameObject.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                {
                    return new Bounds(Vector3.zero, Vector3.zero);
                }
                Bounds value = new Bounds(mesh.bounds.center, mesh.bounds.size);
                value.Expand(BlockBoxTolerance);
                return value;
            }
        }

        protected virtual void Awake()
        {
            physicsEstimator = gameObject.AddComponent<PhysicsEstimator>();
            physicsEstimator.refTransform = Player.m_localPlayer.transform;
        }
            
        //Currently there's 2 Blocking type 
        //"MotionControl" and "GrabButton"
        private void FixedUpdate() {
            tickCounter++;
            if (tickCounter < 5) {
                return;
            }
            
            Vector3 posStart = Player.m_localPlayer.transform.InverseTransformPoint(hand.position);
            Vector3 posEnd = posStart;
            snapshots.Add(posStart);
            Vector3 posStart2 = Player.m_localPlayer.transform.InverseTransformPoint(offhand.position);
            Vector3 posEnd2 = posStart2;
            snapshotsLeft.Add(posStart2);

            if (snapshots.Count > maxSnapshots) {
                snapshots.RemoveAt(0);
                snapshotsLeft.RemoveAt(0);
            }

            tickCounter = 0;
            var dist = 0.0f;
            var dist2 = 0.0f;

            foreach (Vector3 snapshot in snapshots) {
                var curDist = Vector3.Distance(snapshot, posEnd);
                if (curDist > dist) {
                    dist = curDist;
                    posStart = snapshot;
                }
            }
            foreach (Vector3 snapshot in snapshotsLeft)
            {
                var curDist = Vector3.Distance(snapshot, posEnd2);
                if (curDist > dist2)
                {
                    dist2 = curDist;
                    posStart2 = snapshot;
                }
            }

            if (VHVRConfig.BlockingType() == "MotionControl")
                ParryCheck(posStart, posEnd , posStart2, posEnd2);

            if(wasGetHit && !SteamVR_Actions.valheim_Grab.GetState(currhand))
            {
                _meshCooldown.tryTrigger(cooldown);
                wasGetHit = false;
            }
        }
        public abstract void setBlocking(Vector3 hitDir, Vector3 hitPoint);
        protected abstract void ParryCheck(Vector3 posStart, Vector3 posEnd, Vector3 posStart2, Vector3 posEnd2);

        public void resetBlocking() {
            if (VHVRConfig.BlockingType() == "GrabButton")
            {
                _blocking = true;
            }
            else
            {
                _blocking = false;
                blockTimer = blockTimerNonParry;
            }
        }

        public bool isBlocking() {
            if (Player.m_localPlayer.IsStaggering())
            {
                return false;
            }
            if (VHVRConfig.BlockingType() == "GrabButton")
            {
                return SteamVR_Actions.valheim_Grab.GetState(currhand) && !_meshCooldown.inCoolDown() && _blocking;
            }
            else
            {
                return _blocking && !_meshCooldown.inCoolDown();
            }
        }

        public void renderHit(Vector3 hitPoint, Vector3 hitDir)
        {
            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
                lineRenderer.useWorldSpace = true;
                lineRenderer.widthMultiplier = 0.006f;
                lineRenderer.positionCount = 2;
                lineRenderer.material.color = new Color(0.9f, 0.33f, 0.31f);
                lineRenderer.material.SetFloat("_Glossiness", 0);
                lineRenderer.material.SetFloat("_Smoothness", 0);
                lineRenderer.material.SetFloat("_Metallic", 0);
            }
            lineRenderer.SetPosition(0, hitPoint);
            lineRenderer.SetPosition(1, hitPoint + hitDir.normalized);
        }

        public void block() {
            if (VHVRConfig.BlockingType() == "MotionControl")
            {
                if (SteamVR_Actions.valheim_Grab.GetState(currhand))
                {
                    wasGetHit = true;
                }   
                else
                {
                    _meshCooldown.tryTrigger(cooldown);
                }
            }
        }

        public void UpdateGrabParry()
        {
            currhand = offHandSource;
            if (EquipScript.getLeft() != EquipType.Shield)
            {
                currhand = mainHandSource;
            }
            if (SteamVR_Actions.valheim_Grab.GetState(currhand) && !_meshCooldown.inCoolDown() && !wasParryStart)
            {
                wasParryStart = true;
                wasResetTimer = true;
            }
            else if (!SteamVR_Actions.valheim_Grab.GetState(currhand) && wasParryStart)
            {
                _meshCooldown.tryTrigger(0.4f);
                wasParryStart = false;
            }
        }
        public void resetTimer()
        {
            wasResetTimer = false;
        }

        protected bool hitIntersectsBlockBox(Vector3 hitPoint, Vector3 hitDir) {
            if (gameObject.GetComponent<MeshFilter>()?.sharedMesh == null) {
                // Cannot find mesh bounds.
                return true;
            }
            return WeaponUtils.LineIntersectWithBounds(
                blockBounds,
                physicsEstimator.lastRenderedTransform.InverseTransformPoint(hitPoint),
                physicsEstimator.lastRenderedTransform.InverseTransformDirection(hitDir));
        }
    }
}