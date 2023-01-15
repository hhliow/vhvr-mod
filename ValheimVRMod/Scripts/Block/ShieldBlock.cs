using UnityEngine;
using ValheimVRMod.Utilities;
using ValheimVRMod.VRCore;

namespace ValheimVRMod.Scripts.Block {
    public class ShieldBlock : Block {

        public string itemName;
        private const float maxParryAngle = 45f;

        private float scaling = 1f;
        private Vector3 posRef;
        private Vector3 scaleRef;

        public static ShieldBlock instance;

        private GameObject indicator;
        private GameObject indicatorSync;

        private void OnDisable() {
            instance = null;
        }
        
        protected override void Awake() {
            base.Awake();
            _meshCooldown = gameObject.AddComponent<MeshCooldown>();
            instance = this;
            InitShield();

            indicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
            indicatorSync = new GameObject();
            indicatorSync.transform.parent = transform;
            indicatorSync.transform.localPosition = Vector3.zero;
            indicatorSync.transform.localRotation = Quaternion.identity;
            indicator.layer = LayerUtils.getWorldspaceUiLayer();
            indicator.SetActive(false);
            indicator.GetComponent<MeshRenderer>().material.color = new Vector4(0.5f, 0.5f, 0, 0.5f);
            indicator.GetComponent<MeshRenderer>().receiveShadows = false;
            Destroy(indicator.GetComponent<Collider>());
        }
        
        private void InitShield()
        {
            posRef = _meshCooldown.transform.localPosition;
            scaleRef = _meshCooldown.transform.localScale;
            hand = VHVRConfig.LeftHanded() ? VRPlayer.rightHand.transform : VRPlayer.leftHand.transform;
            offhand = VHVRConfig.LeftHanded() ? VRPlayer.leftHand.transform : VRPlayer.rightHand.transform;
        }

        public override void setBlocking(Vector3 hitDir, Vector3 hitPoint) {
            if (blockBounds != null) {
                indicatorSync.transform.localPosition = blockBounds.center;
                indicatorSync.transform.localScale = blockBounds.size;
                indicator.SetActive(false);
                indicator.transform.parent = StaticObjects.shieldObj().transform;
                indicator.transform.localPosition = blockBounds.center;
                indicator.transform.localRotation = Quaternion.identity;
                indicator.transform.localScale = blockBounds.size;
                indicator.transform.SetParent(null, true);
            }
            _blocking = Vector3.Dot(hitDir, getForward()) > 0.5f && hitIntersectsBlockBox(hitPoint, hitDir);
        }

        private Vector3 getForward() {
            switch (itemName)
            {
                case "ShieldWood":
                case "ShieldBanded":
                    return StaticObjects.shieldObj().transform.forward;
                case "ShieldKnight":
                    return -StaticObjects.shieldObj().transform.right;
                case "ShieldBronzeBuckler":
                case "ShieldIronBuckler":
                    return VHVRConfig.LeftHanded() ? StaticObjects.shieldObj().transform.up : -StaticObjects.shieldObj().transform.up;
            }
            return -StaticObjects.shieldObj().transform.forward;
        }

        protected override void ParryCheck(Vector3 posStart, Vector3 posEnd, Vector3 posStart2, Vector3 posEnd2) {

            //var shieldSnapshot = VHVRConfig.LeftHanded() ? snapshotsLeft : snapshots;
            //if (Vector3.Distance(posEnd, posStart) > minDist) {
            //    LogUtils.LogWarning("Block speed: " + velocityEstimator.GetVelocity().magnitude);
            //    Vector3 shieldPos = shieldSnapshot[shieldSnapshot.Count - 1] + Player.m_localPlayer.transform.InverseTransformDirection(-hand.right) / 2;
            //    if (Vector3.Angle(shieldPos - shieldSnapshot[0] , shieldSnapshot[shieldSnapshot.Count - 1] - shieldSnapshot[0]) < maxParryAngle) {
            //        LogUtils.LogWarning("Block angle: " + Vector3.Angle(velocityEstimator.GetVelocity(), velocityEstimator.GetVelocity() + Player.m_localPlayer.transform.InverseTransformDirection(-hand.right) / 2));
            //        blockTimer = blockTimerParry;
            //    }
            //    
            PhysicsEstimator physicsEstimator = gameObject.GetComponent<PhysicsEstimator>();
            float parryingAngle = Vector3.Angle(physicsEstimator.GetVelocity(), physicsEstimator.GetVelocity() + Player.m_localPlayer.transform.InverseTransformDirection(-hand.right) / 2);
            if (physicsEstimator.GetVelocity().magnitude > 1.5f && parryingAngle < maxParryAngle)
            {
                blockTimer = blockTimerParry;
            } else {
                blockTimer = blockTimerNonParry;
            }
        }

        protected void OnRenderObject() {
            if (scaling != 1f)
            {
                transform.localScale = scaleRef * scaling;
                transform.localPosition = CalculatePos();
            }
            else if (transform.localPosition != posRef || transform.localScale != scaleRef)
            {
                transform.localScale = scaleRef;
                transform.localPosition = posRef;
            }
            StaticObjects.shieldObj().transform.position = transform.position;
            StaticObjects.shieldObj().transform.rotation = transform.rotation;
        }

        public void ScaleShieldSize(float scale)
        {
            scaling = scale;
        }
        private Vector3 CalculatePos()
        {
            return VRPlayer.leftHand.transform.InverseTransformDirection(hand.TransformDirection(posRef) *(scaleRef * scaling).x);
        }
    }
}
