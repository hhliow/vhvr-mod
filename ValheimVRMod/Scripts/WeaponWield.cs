using UnityEngine;
using ValheimVRMod.Scripts.Block;
using ValheimVRMod.Utilities;
using ValheimVRMod.VRCore;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace ValheimVRMod.Scripts
{
    // Manages weapon wield of the local player.
    public abstract class WeaponWield : MonoBehaviour
    {
        // TODO: move non-local-player logic from LocalWeaponWield to this class.
        protected const float HAND_CENTER_OFFSET = 0.08f;

        protected Attack attack;
        protected bool weaponSubPos;

        public string itemName;

        public Transform rearHandTransform { get; private set; }
        public Transform frontHandTransform { get; private set; }
        public Transform mainHandTransform { get { return twoHandedState != TwoHandedState.SingleHanded ? rearHandTransform : isPlayerLeftHanded ? GetLeftHandTransform() : GetRightHandTransform(); } }


        private ItemDrop.ItemData item;
        private Transform singleHandedTransform;
        private Transform originalTransform;
        private Quaternion offsetFromPointingDir; // The rotation offset of this transform relative to the direction the weapon is pointing at.
        protected TwoHandedState twoHandedState;
        private Vector3 estimatedLocalWeaponPointingDir = Vector3.forward;
        private Transform lastRenderedTransform;
        private bool isPlayerLeftHanded;
        private bool wasTwoHanded = false;
        protected bool isDominantHandBehind { get { return twoHandedState != TwoHandedState.SingleHanded && (twoHandedState == TwoHandedState.RightHandBehind ^ isPlayerLeftHanded); } }

        ParticleSystem particleSystem;
        Transform particleSystemTransformUpdater;

        public enum TwoHandedState
        {
            SingleHanded,
            RightHandBehind,
            LeftHandBehind
        }

        public WeaponWield Initialize(ItemDrop.ItemData item, bool isPlayerLeftHanded)
        {
            this.item = item;
            this.isPlayerLeftHanded = isPlayerLeftHanded;
            
            particleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
            if (particleSystem != null)
            {
                particleSystemTransformUpdater = new GameObject().transform;
                particleSystemTransformUpdater.parent = transform;
                particleSystemTransformUpdater.SetPositionAndRotation(particleSystem.transform.position, particleSystem.transform.rotation);
            }

            attack = item.m_shared.m_attack.Clone();

            originalTransform = new GameObject().transform;
            singleHandedTransform = new GameObject().transform;
            originalTransform.parent = singleHandedTransform.parent = transform.parent;
            originalTransform.position = singleHandedTransform.position = transform.position;
            originalTransform.rotation = transform.rotation;
            transform.rotation = singleHandedTransform.rotation = GetSingleHandedRotation(originalTransform.rotation);

            MeshFilter weaponMeshFilter = gameObject.GetComponentInChildren<MeshFilter>();
            if (weaponMeshFilter != null)
            {
                estimatedLocalWeaponPointingDir = transform.InverseTransformDirection(WeaponUtils.EstimateWeaponPointingDirection(weaponMeshFilter, transform.parent.position));
            }

            offsetFromPointingDir = Quaternion.Inverse(Quaternion.LookRotation(GetWeaponPointingDir(), transform.up)) * transform.rotation;

            twoHandedState = TwoHandedState.SingleHanded;

            return this;
        }

        private void OnDestroy()
        {
            ReturnToSingleHanded();
            Destroy(originalTransform.gameObject);
            Destroy(singleHandedTransform.gameObject);
            Destroy(lastRenderedTransform.gameObject);
            if (particleSystemTransformUpdater != null)
            {
                Destroy(particleSystemTransformUpdater.gameObject);
            }
        }

        protected virtual void OnRenderObject()
        {
            WieldHandle();
            if (particleSystem != null)
            {
                // The particle system on Mistwalker (as well as some modded weapons) for some reason needs it rotation updated explicitly in order to follow the sword in VR.
                particleSystem.transform.rotation = particleSystemTransformUpdater.transform.rotation;
            }
        }

        protected abstract bool TemporaryDisableTwoHandedWield();

        protected abstract Transform GetLeftHandTransform();
        protected abstract Transform GetRightHandTransform();

        protected virtual bool EquipTypeAllowsTwoHanded()
        {
            switch (itemName)
            {
                case "Hoe":
                case "Hammer":
                case "Cultivator":
                case "FishingRod":
                    return false;
            }
            switch (attack.m_attackAnimation)
            {
                case "knife_stab":
                    return false;
            }
            return true;
        }

        // Returns the direction the weapon is pointing.
        protected virtual Vector3 GetWeaponPointingDir()
        {
            return transform.TransformDirection(estimatedLocalWeaponPointingDir);
        }

        // Calculates the correct rotation of this game object for single-handed mode using the original rotation.
        // This should be the same as the original rotation in most cases but there are exceptions.
        protected virtual Quaternion GetSingleHandedRotation(Quaternion originalRotation)
        {
            switch (attack.m_attackAnimation)
            {
                case "atgeir_attack":
                    // Atgeir wield rotation fix: the tip of the atgeir is pointing at (0.328, -0.145, 0.934) in local coordinates.
                    return originalRotation * Quaternion.AngleAxis(-20, Vector3.up) * Quaternion.AngleAxis(-7, Vector3.right);
                default:
                    return originalRotation;
            }
        }

        // The preferred up direction used to determine the weapon's rotation around it longitudinal axis during two-handed wield.
        protected virtual Vector3 GetPreferredTwoHandedWeaponUp()
        {
            return singleHandedTransform.up;
        }

        // The preferred forward offset amount of the weapon's position from the rear hand during two-handed wield.
        protected virtual float GetPreferredOffsetFromRearHand(float handDist)
        {
            bool rearHandIsDominant = (isPlayerLeftHanded == (twoHandedState == TwoHandedState.LeftHandBehind));
            if (rearHandIsDominant)
            {
                return -0.1f;
            }
            else if (handDist > 0.15f)
            {
                return 0.05f;
            }
            else
            {
                // Anchor the weapon in the front/dominant hand instead.
                return handDist - 0.1f;
            }
        }

        protected virtual void WieldHandle()
        {
            if (EquipTypeAllowsTwoHanded()) {
                UpdateTwoHandedWield();
            }
        }

        protected virtual void UpdateTwoHandedWield()
        {
            if (!TemporaryDisableTwoHandedWield())
            {
                if (twoHandedState == TwoHandedState.SingleHanded)
                {
                    Vector3 rightHandToLeftHand = getHandCenter(GetLeftHandTransform()) - getHandCenter(GetRightHandTransform());
                    float wieldingAngle = Vector3.Angle(rightHandToLeftHand, GetWeaponPointingDir());

                    if (wieldingAngle < 60)
                    {
                        twoHandedState = TwoHandedState.RightHandBehind;
                    }
                    else if (wieldingAngle > 60f)
                    {
                        twoHandedState = TwoHandedState.LeftHandBehind;
                    }
                    else
                    {
                        return;
                    }
                }

                wasTwoHanded = true;

                rearHandTransform = twoHandedState == TwoHandedState.LeftHandBehind ? GetLeftHandTransform() : GetRightHandTransform();
                frontHandTransform = twoHandedState == TwoHandedState.LeftHandBehind ? GetRightHandTransform() : GetLeftHandTransform();

                Vector3 frontHandCenter = getHandCenter(rearHandTransform);
                Vector3 rearHandCenter = getHandCenter(frontHandTransform);
                var weaponPointingDir = (frontHandCenter - rearHandCenter).normalized;

                //weapon pos&rotation
                transform.position = rearHandCenter + weaponPointingDir * (HAND_CENTER_OFFSET + GetPreferredOffsetFromRearHand(Vector3.Distance(frontHandCenter, rearHandCenter)));
                transform.rotation = Quaternion.LookRotation(weaponPointingDir, GetPreferredTwoHandedWeaponUp()) * offsetFromPointingDir;

                weaponSubPos = true;
            }
            else if (wasTwoHanded)
            {
                twoHandedState = TwoHandedState.SingleHanded;
                weaponSubPos = false;
                ReturnToSingleHanded();
                wasTwoHanded = false;
            }
        }

        protected virtual void ReturnToSingleHanded()
        {
            transform.position = singleHandedTransform.position;
            transform.localRotation = singleHandedTransform.localRotation;
        }

        protected static Vector3 getHandCenter(Transform hand)
        {
            return hand.transform.position - hand.transform.forward * HAND_CENTER_OFFSET;
        }


        public bool isLeftHandWeapon()
        {
            var player = gameObject.GetComponentInParent<Player>();
            var leftHandItem = player?.m_leftItem?.m_shared.m_itemType;
            return !(leftHandItem is null) && leftHandItem != ItemDrop.ItemData.ItemType.Shield;
        }
    }
}
