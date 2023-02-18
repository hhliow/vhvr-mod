using UnityEngine;
using ValheimVRMod.Scripts.Block;
using ValheimVRMod.Utilities;
using ValheimVRMod.VRCore;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace ValheimVRMod.Scripts
{
    // Manages weapon wield of the local player.
    public class LocalWeaponWield : WeaponWield
    {
        private static LocalWeaponWield Instance;

        public static Vector3 weaponForward;

        public static TwoHandedState LocalPlayerTwoHandedState { get; private set; }
        private float shieldSize = 1f;
        private Transform frontHandConnector { get { return LocalPlayerTwoHandedState == TwoHandedState.LeftHandBehind ? VrikCreator.rightHandConnector : VrikCreator.leftHandConnector; } }
        private Transform rearHandConnector { get { return LocalPlayerTwoHandedState == TwoHandedState.LeftHandBehind ? VrikCreator.leftHandConnector : VrikCreator.rightHandConnector; } }
        private Transform lastRenderedTransform;
        public PhysicsEstimator physicsEstimator { get; private set; }
        public static bool IsDominantHandBehind { get { return Instance.isDominantHandBehind; } }


        protected virtual void Awake()
        {
            Instance = this;
            lastRenderedTransform = new GameObject().transform;
            physicsEstimator = lastRenderedTransform.gameObject.AddComponent<PhysicsEstimator>();
            physicsEstimator.refTransform = CameraUtils.getCamera(CameraUtils.VR_CAMERA)?.transform.parent;
        }

        public LocalWeaponWield Initialize(bool holdInNonDominantHand)
        {
            base.Initialize(holdInNonDominantHand ? Player.m_localPlayer.GetLeftItem() : Player.m_localPlayer.GetRightItem(), VHVRConfig.LeftHanded());
            return this;
        }

        protected override void OnRenderObject()
        {
            if (VRPlayer.ShouldPauseMovement)
            {
                return;
            }

            base.OnRenderObject();

            // The transform outside OnRenderObject() might be invalid or discontinuous, therefore we need to record its state within this method for physics calculation later.
            lastRenderedTransform.parent = transform;
            lastRenderedTransform.SetPositionAndRotation(transform.position, transform.rotation);
            lastRenderedTransform.localScale = Vector3.one;
            lastRenderedTransform.SetParent(null, true);
        }

        protected override bool TemporaryDisableTwoHandedWield()
        {
            return !SteamVR_Actions.valheim_Grab.GetState(SteamVR_Input_Sources.LeftHand) || !SteamVR_Actions.valheim_Grab.GetState(SteamVR_Input_Sources.RightHand);
        }

        protected override Transform GetLeftHandTransform()
        {
            return VRPlayer.leftHand.transform;
        }

        protected override Transform GetRightHandTransform()
        {
            return VRPlayer.rightHand.transform;
        }

        protected override bool EquipTypeAllowsTwoHanded() {
            if (isLeftHandWeapon() && EquipScript.getLeft() != EquipType.Crossbow)
            {
                return false;
            }
            return base.EquipTypeAllowsTwoHanded();
        }


        protected virtual void RotateHandsForTwoHandedWield(Vector3 weaponPointingDir)
        {
            Vector3 desiredFrontHandForward = Vector3.Project(frontHandTransform.forward, weaponPointingDir);
            Vector3 desiredRearHandForward = Vector3.Project(rearHandTransform.forward, Quaternion.AngleAxis(10, rearHandTransform.right) * weaponPointingDir);
            frontHandConnector.rotation = Quaternion.LookRotation(desiredFrontHandForward, frontHandTransform.up);
            rearHandConnector.rotation = Quaternion.LookRotation(desiredRearHandForward, rearHandTransform.up);
        }

        protected override void WieldHandle()
        {
            if (attack.m_attackAnimation == "knife_stab") {
                KnifeWield();
            }
            else
            {
                base.WieldHandle();
            }
            weaponForward = GetWeaponPointingDir();
        }

        private void KnifeWield()
        {
            if (LocalPlayerTwoHandedState != TwoHandedState.SingleHanded)
            {
                return;
            }

            if (SteamVR_Actions.valheim_Grab.GetState(VRPlayer.dominantHandInputSource))
            {
                ReturnToSingleHanded();
                // Reverse grip
                transform.localRotation *= Quaternion.AngleAxis(180, Vector3.right);
                weaponSubPos = true;
            }
            else if (weaponSubPos)
            {
                ReturnToSingleHanded();
                weaponSubPos = false;
            }
        }

        protected override void UpdateTwoHandedWield()
        {
            if (!VHVRConfig.TwoHandedWield())
            {
                return;
            }

            if (!VHVRConfig.TwoHandedWithShield() && EquipScript.getLeft() == EquipType.Shield)
            {
                if (weaponSubPos)
                {
                    twoHandedState = TwoHandedState.SingleHanded;
                    weaponSubPos = false;
                    ReturnToSingleHanded();
                }
                return;
            }

            base.UpdateTwoHandedWield();

            if (isCurrentlyTwoHanded()) {
                //VRIK Hand rotation
                Vector3 frontHandCenter = getHandCenter(frontHandTransform);
                Vector3 rearHandCenter = getHandCenter(rearHandTransform);
                RotateHandsForTwoHandedWield((frontHandCenter - rearHandCenter).normalized);
                // Adjust the positions so that they are rotated around the hand centers which are slightly off from their local origins.
                frontHandConnector.position = frontHandConnector.parent.position + frontHandConnector.forward * HAND_CENTER_OFFSET + (frontHandCenter - frontHandTransform.position);
                rearHandConnector.position = rearHandConnector.parent.position + rearHandConnector.forward * HAND_CENTER_OFFSET + (rearHandCenter - rearHandTransform.position);
                weaponSubPos = true;
                shieldSize = 0.4f;
            }
        }

        protected override void ReturnToSingleHanded()
        {
            VrikCreator.ResetHandConnectors();
            shieldSize = 1f;
            base.ReturnToSingleHanded();
        }

        public static bool isCurrentlyTwoHanded()
        {
            return Instance.twoHandedState != TwoHandedState.SingleHanded;
        }

        public bool allowBlocking()
        {
            switch (attack.m_attackAnimation)
            {
                case "knife_stab":
                    if (EquipScript.getLeft() == EquipType.Shield)
                        return false;
                    else
                        return weaponSubPos;
                default:
                    return VHVRConfig.BlockingType() == "Gesture" ? isCurrentlyTwoHanded() : SteamVR_Actions.valheim_Grab.GetState(VRPlayer.dominantHandInputSource);
            }
        }
    }
}
