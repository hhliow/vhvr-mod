using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimVRMod.VRCore;
using ValheimVRMod.Utilities;

namespace ValheimVRMod.Scripts
{
    // Component for fixing the undersized bounds of skinned mesh renderers of equipments so that they do not disappear while on-screen.
    public class EquipBoundingBoxFix : MonoBehaviour
    {
        // Equipments whose skinned mesh renderer's unmodded bounding box is too small that we need to expand it so that they do not disappear.
        private readonly static HashSet<string> EquipItemNames = new HashSet<string>(new string[] { "ArmorFenringChest" });
        private readonly static HashSet<string> EquipGameObjectNames = new HashSet<string>(new string[] { "FenringPants" });

        private SkinnedMeshRenderer playerBodyMeshRenderer;
        private bool pendingBoundingBoxFix = false;

        public static EquipBoundingBoxFix GetInstanceForPlayer(Player player)
        {
            if (player == null)
            {
                return null;
            }
            return player.gameObject.GetComponent<EquipBoundingBoxFix>() ?? player.gameObject.AddComponent<EquipBoundingBoxFix>();
        }

        void Update()
        {
            if (pendingBoundingBoxFix)
            {
                FixSkinnedMeshRendererBounds();
            }
        }

        public void RequestFixBoundingBox(String name)
        {
            if (!EquipItemNames.Contains(name))
            {
                return;
            }

            pendingBoundingBoxFix = true;
        }

        private void FixSkinnedMeshRendererBounds()
        {
            if (!VRPlayer.inFirstPerson || !EnsureBodyRenderer())
            {
                return;
            }

            // The body has bounds big enough that we can use it to calculate desired bounds of the equipments.
            Vector3 center = playerBodyMeshRenderer.bounds.center;
            Vector3 extents = playerBodyMeshRenderer.bounds.extents;
            Vector3[] playerBoundVertices = new Vector3[] {
                    center + extents,
                    center - extents,
                    center + Vector3.Reflect(extents, Vector3.right),
                    center - Vector3.Reflect(extents, Vector3.right),
                    center + Vector3.Reflect(extents, Vector3.up),
                    center - Vector3.Reflect(extents, Vector3.up),
                    center + Vector3.Reflect(extents, Vector3.forward),
                    center - Vector3.Reflect(extents, Vector3.forward)};

            SkinnedMeshRenderer[] playerSkinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer renderer in playerSkinnedMeshRenderers)
            {
                if (!EquipGameObjectNames.Contains(renderer.gameObject.name))
                {
                    continue;
                }

                Bounds localBounds = renderer.localBounds;
                // Expand the bounds of the equipment to encapsulate the bounds of the player body.
                foreach (Vector3 p in playerBoundVertices)
                {
                    localBounds.Encapsulate(renderer.transform.InverseTransformPoint(p));
                }
                renderer.localBounds = localBounds;
            }

            pendingBoundingBoxFix = false;
        }

        private bool EnsureBodyRenderer()
        {
            if (playerBodyMeshRenderer != null)
            {
                return true;
            }

            SkinnedMeshRenderer[] playerSkinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer renderer in playerSkinnedMeshRenderers)
            {
                if (renderer.gameObject.name == "body")
                {
                    playerBodyMeshRenderer = renderer;
                    return true;
                }
            }

            return false;
        }
    }
}