using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimVRMod.VRCore;
using ValheimVRMod.Scripts;
using ValheimVRMod.Scripts.Block;
using ValheimVRMod.Utilities;

namespace ValheimVRMod.Patches {

     [HarmonyPatch(typeof(VisEquipment), "SetRightHandEquiped")]
     class PatchSetRightHandEquiped {
        static void Postfix(bool __result, string ___m_rightItem, ref GameObject ___m_rightItemInstance) {
            if (!__result || ___m_rightItemInstance == null) {
                return;
            }

            Player player = ___m_rightItemInstance.GetComponentInParent<Player>();
            
            if (player == null) {
                return;
            }

            if (player == Player.m_localPlayer && VHVRConfig.UseVrControls())
            {
                EquipBoundingBoxFix.GetInstanceForPlayer(player)?.RequestBoundingBoxFix(___m_rightItem, ___m_rightItemInstance);
            }

            MeshFilter meshFilter = ___m_rightItemInstance.GetComponentInChildren<MeshFilter>();
            if (meshFilter == null)
            {
                return;
            }

            var vrPlayerSync = player.GetComponent<VRPlayerSync>();
            
            if (vrPlayerSync != null && meshFilter != null) {
                if (VHVRConfig.LeftHanded()) {
                    player.GetComponent<VRPlayerSync>().currentLeftWeapon = meshFilter.gameObject;
                    player.GetComponent<VRPlayerSync>().currentLeftWeapon.name = ___m_rightItem;    
                }
                else {
                    player.GetComponent<VRPlayerSync>().currentRightWeapon = meshFilter.gameObject;
                    player.GetComponent<VRPlayerSync>().currentRightWeapon.name = ___m_rightItem;
                }
                
                VrikCreator.resetVrikHandTransform(player);   
            }

            if (Player.m_localPlayer != player || !VHVRConfig.UseVrControls()) {
                return;
            }

            if (StaticObjects.rightHandQuickMenu != null) {
                StaticObjects.rightHandQuickMenu.GetComponent<RightHandQuickMenu>().refreshItems();
                StaticObjects.leftHandQuickMenu.GetComponent<LeftHandQuickMenu>().refreshItems();
            }

            switch (EquipScript.getRight()) {
                case EquipType.Hammer:
                    meshFilter.gameObject.AddComponent<BuildingManager>();
                    return;
                case EquipType.Fishing:
                    meshFilter.gameObject.transform.localPosition = new Vector3(0, 0, -0.4f);
                    meshFilter.gameObject.AddComponent<FishingManager>();
                    break;
            }
            LocalWeaponWield weaponWield = EquipScript.isSpearEquipped() ? ___m_rightItemInstance.AddComponent<SpearWield>() : ___m_rightItemInstance.AddComponent<LocalWeaponWield>();
            weaponWield.itemName = ___m_rightItem;
            weaponWield.Initialize(false);

            if (MagicWeaponManager.IsSwingLaunchEnabled())
            {
                meshFilter.gameObject.AddComponent<SwingLaunchManager>();
            }

            if (EquipScript.isThrowable(player.GetRightItem()) || EquipScript.isSpearEquipped() || EquipScript.getRight() == EquipType.ThrowObject)
            {
                // TODO: rename this to ThrowableManager
                (meshFilter.gameObject.AddComponent<ThrowableManager>()).weaponWield = weaponWield;
            }

            var weaponCol = StaticObjects.rightWeaponCollider().GetComponent<WeaponCollision>();
            weaponCol.setColliderParent(meshFilter.transform, ___m_rightItem, true);
            weaponCol.weaponWield = weaponWield;
            meshFilter.gameObject.AddComponent<ButtonSecondaryAttackManager>().Initialize(meshFilter.transform, ___m_rightItem, true);
            meshFilter.gameObject.AddComponent<WeaponBlock>().weaponWield = weaponWield;

            ParticleFix.maybeFix(___m_rightItemInstance);
        }
    }

    [HarmonyPatch(typeof(VisEquipment), "SetLeftHandEquiped")]
    class PatchSetLeftHandEquiped {
        static void Postfix(bool __result, string ___m_leftItem, GameObject ___m_leftItemInstance) {
            if (!__result || ___m_leftItemInstance == null) {
                return;
            }

            Player player = ___m_leftItemInstance.GetComponentInParent<Player>();
            
            if (player == null) {
                return;
            }

            if (player == Player.m_localPlayer && VHVRConfig.UseVrControls())
            {
                EquipBoundingBoxFix.GetInstanceForPlayer(player)?.RequestBoundingBoxFix(___m_leftItem, ___m_leftItemInstance);
            }

            MeshFilter meshFilter = ___m_leftItemInstance.GetComponentInChildren<MeshFilter>();
            if (meshFilter == null)
            {
                return;
            }

            var vrPlayerSync = player.GetComponent<VRPlayerSync>();

            if (vrPlayerSync != null) {
                if (VHVRConfig.LeftHanded()) {
                    player.GetComponent<VRPlayerSync>().currentRightWeapon = meshFilter.gameObject;    
                }
                else {
                    player.GetComponent<VRPlayerSync>().currentLeftWeapon = meshFilter.gameObject;
                }
                
                VrikCreator.resetVrikHandTransform(player);
            }

            if (Player.m_localPlayer != player || !VHVRConfig.UseVrControls()) {
                return;
            }

            if (StaticObjects.rightHandQuickMenu != null) {
                StaticObjects.rightHandQuickMenu.GetComponent<RightHandQuickMenu>().refreshItems();
                StaticObjects.leftHandQuickMenu.GetComponent<LeftHandQuickMenu>().refreshItems();
            }

            LocalWeaponWield weaponWield;
            switch (EquipScript.getLeft()) {
                
                case EquipType.Bow:
                    meshFilter.gameObject.AddComponent<BowLocalManager>();
                    var bow = Player.m_localPlayer.GetLeftItem();
                    if (!Attack.HaveAmmo(Player.m_localPlayer, bow))
                    {
                        return;
                    }
                    Attack.EquipAmmoItem(Player.m_localPlayer, bow);
                    
                    return;
                case EquipType.Crossbow:
                    CrossbowManager crossbowManager = ___m_leftItemInstance.AddComponent<CrossbowManager>();
                    crossbowManager.Initialize(true);
                    crossbowManager.itemName = ___m_leftItem;
                    crossbowManager.gameObject.AddComponent<WeaponBlock>().weaponWield = crossbowManager;
                    return;
                case EquipType.Lantern:
                    weaponWield = ___m_leftItemInstance.AddComponent<LocalWeaponWield>().Initialize(true);
                    weaponWield.itemName = ___m_leftItem;
                    break;
                case EquipType.Shield:
                    meshFilter.gameObject.AddComponent<ShieldBlock>().itemName = ___m_leftItem;
                    return;
            }

            StaticObjects.leftWeaponCollider().GetComponent<WeaponCollision>().setColliderParent(meshFilter.transform, ___m_leftItem, false);
            meshFilter.gameObject.AddComponent<ButtonSecondaryAttackManager>().Initialize(meshFilter.transform, ___m_leftItem, false);
            ParticleFix.maybeFix(___m_leftItemInstance);
        }
    }
    
    [HarmonyPatch(typeof(VisEquipment), "SetHelmetEquiped")]
    class PatchHelmet {
        static void Postfix(bool __result, GameObject ___m_helmetItemInstance) {

            if (!__result || !VHVRConfig.UseVrControls()) {
                return;
            }

            ___m_helmetItemInstance.AddComponent<HeadEquipVisibiltiyUpdater>();
        }
    }
    
    [HarmonyPatch(typeof(VisEquipment), "SetHairEquiped")]
    class PatchHair {
        static void Postfix(bool __result, GameObject ___m_hairItemInstance) {
            
            if (!__result || !VHVRConfig.UseVrControls()) {
                return;
            }
            
            ___m_hairItemInstance.AddComponent<HeadEquipVisibiltiyUpdater>();
        }
    }
    
    [HarmonyPatch(typeof(VisEquipment), "SetBeardEquiped")]
    class PatchBeard {
        static void Postfix(bool __result, GameObject ___m_beardItemInstance) {
            
            if (!__result || !VHVRConfig.UseVrControls()) {
                return;
            }
            
            ___m_beardItemInstance.AddComponent<HeadEquipVisibiltiyUpdater>();
        }
    }

    [HarmonyPatch(typeof(VisEquipment), "SetChestEquiped")]
    class PatchSetChestEquiped
    {
        static void Postfix(bool __result, string ___m_chestItem, List<GameObject> ___m_chestItemInstances)
        {
            if (!__result || ___m_chestItemInstances == null || ___m_chestItemInstances.Count == 0 || !VHVRConfig.UseVrControls())
            {
                return;
            }

            Player player = ___m_chestItemInstances[0].GetComponentInParent<Player>();

            if (player == null || player != Player.m_localPlayer)
            {
                return;
            }
             
            foreach (GameObject itemInstance in ___m_chestItemInstances)
            {
                EquipBoundingBoxFix.GetInstanceForPlayer(player)?.RequestBoundingBoxFix(___m_chestItem, itemInstance);
            }
        }
    }

    [HarmonyPatch(typeof(VisEquipment), "AttachItem")]
    class PatchAttachItem {
        
        /// <summary>
        /// For Left Handed mode, switch left with right items
        /// </summary>
        static void Prefix(VisEquipment __instance, ref Transform joint) {

            if (joint.GetComponentInParent<Player>() != Player.m_localPlayer
                || !VHVRConfig.UseVrControls() 
                || !VHVRConfig.LeftHanded()) {
                return;
            }

            if (joint == __instance.m_rightHand) {
                joint = __instance.m_leftHand;
            }
            else if (joint == __instance.m_leftHand) {
                joint = __instance.m_rightHand;
            }
        }

        /// <summary>
        /// For Left Handed mode we need to mirror models of shields and tankard 
        /// </summary>
        static void Postfix(VisEquipment __instance, GameObject __result, int itemHash) {
            
            if (__instance.m_isPlayer && __result != null && itemHash == 703889544)
            {
                var lights = __result.GetComponentsInChildren<Light>();
                if (lights.Length > 1)
                {
                    LogUtils.LogChildTree(__result.transform);
                    lights[0].intensity = 0;
                    // lights[0].enabled = false;
                }
            }


            if (Player.m_localPlayer == null 
                || __result == null
                || __result.GetComponentInParent<Player>() != Player.m_localPlayer
                || !VHVRConfig.UseVrControls() 
                || !VHVRConfig.LeftHanded()
                || EquipScript.getLeft() != EquipType.Shield
                && EquipScript.getRight() != EquipType.Tankard) {
                return;
            }
            
            __result.transform.localScale = new Vector3 (__result.transform.localScale.x, __result.transform.localScale.y * -1 , __result.transform.localScale.z);

        }
    }

    class LightUpdater : MonoBehaviour
    {
        private bool isLocalPlayer;

        void Awake()
        {
            Player player = gameObject.GetComponentInParent<Player>();
            isLocalPlayer = (player != null && player == Player.m_localPlayer);
        }

        void Update()
        {
            if (isLocalPlayer)
            {
                var lights = gameObject.GetComponentsInChildren<Light>();
                if (lights.Length > 1)
                {
                    // LogUtils.LogChildTree(gameObject.transform);
                    LogUtils.LogWarning("Lights: " + lights.Length + " --- " + lights[0].name + " --- " + lights[1].name);
                    lights[1].intensity = 0;
                }
            }
        }
    }

    class HeadEquipVisibiltiyUpdater : MonoBehaviour
    {
        private bool isLocalPlayer;

        private bool isHidden = false;

        void Awake() {
            Player player = gameObject.GetComponentInParent<Player>();
            isLocalPlayer = (player != null && player == Player.m_localPlayer);
        }

        void OnRenderObject()
        {
            if (shouldHide())
            {
                if (!isHidden) {
                    foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>())
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    }
                    isHidden = true;
                }
            } else if (isHidden)
            {
                foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>())
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                }
                isHidden = false;
            }
        }

        private bool shouldHide() { 
            if (!isLocalPlayer || !VRPlayer.attachedToPlayer)
            {
                return false;
            }
            if (!Menu.IsVisible())
            {
                return true;
            }
            Vector3 cameraPos = CameraUtils.getCamera(CameraUtils.VR_CAMERA).transform.position;
            Vector3 characterHeadPos = Player.m_localPlayer.m_head.transform.position;
            // When the user is in the menu, show head equipments when the camera moves away from the character so that the full character is visible to the user.
            return Vector3.Distance(cameraPos, characterHeadPos) < 0.25f;
        }
    }

    [HarmonyPatch(typeof(Player),nameof(Player.ToggleEquiped))]
    class PatchEquipActionQueue
    {
        static bool Prefix(Player __instance, ref bool __result)
        {
            if(__instance != Player.m_localPlayer || !VHVRConfig.UseVrControls())
            {
                return true;
            }

            if (ButtonSecondaryAttackManager.isSecondaryAttackStarted)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
