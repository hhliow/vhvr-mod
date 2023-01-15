using UnityEngine;
using ValheimVRMod.Utilities;
using ValheimVRMod.VRCore;

namespace ValheimVRMod.Scripts.Block {
    public class FistBlock : Block {

        private const float maxParryAngle = 45f;
        private readonly Vector3 handUp = new Vector3(0, -0.15f, -0.85f);

        private GameObject leftHandBlockBox;
        private GameObject rightHandBlockBox;

        public static FistBlock instance;

        private void OnDisable() {
            instance = null;
        }
        
        protected override void Awake() {
            base.Awake();
            _meshCooldown = gameObject.AddComponent<MeshCooldown>();
            instance = this;
            hand = VHVRConfig.LeftHanded() ? VRPlayer.leftHand.transform : VRPlayer.rightHand.transform;
            offhand = VHVRConfig.LeftHanded() ? VRPlayer.rightHand.transform : VRPlayer.leftHand.transform;

            leftHandBlockBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftHandBlockBox.transform.parent = VRPlayer.leftHand.transform;
            leftHandBlockBox.transform.localRotation = Quaternion.Euler(45, 0, 0);
            leftHandBlockBox.transform.localPosition = new Vector3(0, 0.2f, -0.2f);
            leftHandBlockBox.transform.localScale = new Vector3(0.25f, 0.25f, 0.8f);
            leftHandBlockBox.GetComponent<MeshRenderer>().material.color = new Vector4(0.5f, 0.5f, 0, 0.1f);
            //leftHandBlockBox.GetComponent<MeshRenderer>().enabled = false;
            Destroy(leftHandBlockBox.GetComponent<Collider>());

            rightHandBlockBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightHandBlockBox.transform.parent = VRPlayer.rightHand.transform;
            rightHandBlockBox.transform.localRotation = Quaternion.Euler(45, 0, 0);
            rightHandBlockBox.transform.localPosition = new Vector3(0, 0.2f, -0.2f);
            rightHandBlockBox.transform.localScale = new Vector3(0.25f, 0.25f, 0.8f);
            leftHandBlockBox.GetComponent<MeshRenderer>().material.color = new Vector4(0.5f, 0.5f, 0, 0.1f);
            //leftHandBlockBox.GetComponent<MeshRenderer>().enabled = false;
            Destroy(rightHandBlockBox.GetComponent<Collider>());



        }

        public override void setBlocking(Vector3 hitDir, Vector3 hitPoint) {
            //_blocking = Vector3.Dot(hitDir, getForward()) > 0.5f;
            if (FistCollision.instance.usingDualKnives())
            {
                var leftAngle = Vector3.Dot(hitDir, offhand.TransformDirection(handUp));
                var rightAngle = Vector3.Dot(hitDir, hand.TransformDirection(handUp));
                var leftHandBlock = (leftAngle > -0.5f && leftAngle < 0.5f);
                var rightHandBlock = (rightAngle > -0.5f && rightAngle < 0.5f);
                _blocking = leftHandBlock && rightHandBlock;
            }
            else if (FistCollision.instance.usingFistWeapon())
            {
                LogUtils.LogWarning("Fist: " + leftHandBlockBox.GetComponent<MeshFilter>().sharedMesh.bounds);
                if (WeaponUtils.LineIntersectWithBounds(leftHandBlockBox.GetComponent<MeshFilter>().sharedMesh.bounds, leftHandBlockBox.transform.InverseTransformPoint(hitPoint), leftHandBlockBox.transform.InverseTransformDirection(hitDir)))
                {
                    _blocking = false;
                }
                else if (WeaponUtils.LineIntersectWithBounds(rightHandBlockBox.GetComponent<MeshFilter>().sharedMesh.bounds, rightHandBlockBox.transform.InverseTransformPoint(hitPoint), rightHandBlockBox.transform.InverseTransformPoint(hitDir)))
                {
                    _blocking = false;
                } else
                {
                    _blocking = false;
                }
            }
            else
            {
                _blocking = false;
            }
        }

        protected override void ParryCheck(Vector3 posStart, Vector3 posEnd, Vector3 posStart2, Vector3 posEnd2) {
            if (FistCollision.instance.usingFistWeapon())
            {
                if (VRPlayer.leftHand.gameObject.GetComponent<VelocityEstimator>().GetVelocity().magnitude > 1f)
                {
                    blockTimer = blockTimerParry;
                }
                else if (VRPlayer.rightHand.gameObject.GetComponent<VelocityEstimator>().GetVelocity().magnitude > 1f)
                {
                    blockTimer = blockTimerParry;
                }
                else
                {
                    blockTimer = blockTimerNonParry;
                }
            }
        }
    }
}
