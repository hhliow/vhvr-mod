using UnityEngine;
using ValheimVRMod.Utilities;
using ValheimVRMod.VRCore;

namespace ValheimVRMod.Scripts.Block {
    public class WeaponBlock : Block {
        
        public WeaponWield weaponWield;
        public static WeaponBlock instance;
        private readonly Vector3 handUp = new Vector3(0, -0.15f, -0.85f);
        private GameObject indicator;

        private void OnDisable() {
            instance = null;
        }
        
        protected override void Awake() {
            base.Awake();
            _meshCooldown = gameObject.AddComponent<MeshCooldown>();
            instance = this;
            hand = VHVRConfig.LeftHanded() ? VRPlayer.leftHand.transform : VRPlayer.rightHand.transform;
            offhand = VHVRConfig.LeftHanded() ? VRPlayer.rightHand.transform : VRPlayer.leftHand.transform;

            indicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
            indicator.SetActive(false);
            indicator.GetComponent<MeshRenderer>().material.color = new Vector4(0.5f, 0.5f, 0, 0.5f);
            indicator.GetComponent<MeshRenderer>().receiveShadows = false;
            Destroy(indicator.GetComponent<Collider>());
        }

        public override void setBlocking(Vector3 hitDir, Vector3 hitPoint) {
            if (blockBounds != null)
            {
                indicator.SetActive(false);
                indicator.transform.localPosition = blockBounds.center;
                indicator.transform.localRotation = Quaternion.identity;
                indicator.transform.localScale = blockBounds.size;
                indicator.transform.SetParent(null, true);
            }
            var angle = Vector3.Dot(hitDir, WeaponWield.weaponForward);
            if (weaponWield.isLeftHandWeapon() && EquipScript.getLeft() != EquipType.Crossbow)
            {
                var leftAngle = Vector3.Dot(hitDir, offhand.TransformDirection(handUp));
                var rightAngle = Vector3.Dot(hitDir, hand.TransformDirection(handUp));
                var leftHandBlock = (leftAngle > -0.5f && leftAngle < 0.5f) ;
                var rightHandBlock = (rightAngle > -0.5f && rightAngle < 0.5f);
                _blocking = leftHandBlock && rightHandBlock && hitIntersectsBlockBox(hitPoint, hitDir);
            }
            else
            {
                if (VHVRConfig.BlockingType() == "GrabButton")
                {
                    _blocking = angle > -0.3f && angle < 0.3f;
                }
                else
                {
                    _blocking = weaponWield.allowBlocking() && angle > -0.3f && angle < 0.3f && hitIntersectsBlockBox(hitPoint, hitDir);
                }
            }
        }

        protected override void ParryCheck(Vector3 posStart, Vector3 posEnd, Vector3 posStart2, Vector3 posEnd2) {
            if (weaponWield.velocityEstimator.GetVelocity().magnitude >= 1.5f) 
            {
                LogUtils.LogWarning("Weapn parrying: " + weaponWield.velocityEstimator.GetVelocity().magnitude);
                blockTimer = blockTimerParry;
            }
            else 
            {
                blockTimer = blockTimerNonParry;
            }
        }
    }
}
