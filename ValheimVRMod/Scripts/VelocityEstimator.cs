using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UnityEngine;
using ValheimVRMod.Scripts.Block;
using ValheimVRMod.Utilities;
using ValheimVRMod.VRCore;
using Valve.VR;

namespace ValheimVRMod.Scripts
{
    public class VelocityEstimator : MonoBehaviour
    {
        private const int MAX_SNAPSHOTS = 8;

        private List<Vector3> snapshots = new List<Vector3>();
        private List<Quaternion> rotationSnapshots = new List<Quaternion>();
        private List<Vector3> velocitySnapshots = new List<Vector3>();
        private Transform transformSync;
        private LineRenderer debugVelocityLine;

        private Transform _refTransform;
        public Transform refTransform {
            get
            {
                return _refTransform;
            }
            set
            {
                EnsureTransformSync();
                if (_refTransform == value)
                {
                    return;
                }
                snapshots.Clear();
                rotationSnapshots.Clear();
                velocitySnapshots.Clear();
                _refTransform = value;
                transformSync.SetParent( _refTransform, true);
            }
        }

        public bool renderDebugVelocityLine = false;

        private void Awake()
        {
            EnsureTransformSync();
            transformSync.SetParent(refTransform, true);
            transformSync.SetPositionAndRotation(transform.position, transform.rotation);
            // CreateDebugVelocityLine();
        }

        void FixedUpdate()
        {
            snapshots.Add(refTransform == null ? transformSync.position : transformSync.localPosition);
            rotationSnapshots.Add(refTransform == null ? transformSync.rotation : transformSync.localRotation);
            if (snapshots.Count >= 2) {
                // TODO: consider using least square fit or a smoonthening function over all snapshots, but should balance with performance too.
                velocitySnapshots.Add((snapshots[snapshots.Count - 1] - snapshots[0]) / Time.fixedDeltaTime / (snapshots.Count - 1));
            }
            if (snapshots.Count > MAX_SNAPSHOTS)
            {
                snapshots.RemoveAt(0);
            }
            if (rotationSnapshots.Count > MAX_SNAPSHOTS)
            {
                rotationSnapshots.RemoveAt(0);
            }
            if (velocitySnapshots.Count > MAX_SNAPSHOTS)
            {
                velocitySnapshots.RemoveAt(0);
            }
        }

        void OnRenderObject()
        {
            transformSync.SetPositionAndRotation(transform.position, transform.rotation);
            if (debugVelocityLine != null)
            {
                debugVelocityLine.enabled = renderDebugVelocityLine;
                debugVelocityLine.SetPosition(0, transformSync.position);
                debugVelocityLine.SetPosition(1, transformSync.position + GetVelocity());
            }
        }

        void Destroy()
        {
            Destroy(transformSync.gameObject);
        }

        public Vector3 GetVelocity()
        {
            if (velocitySnapshots.Count == 0)
            {
                return Vector3.zero;
            }
            return refTransform == null ? velocitySnapshots[0] : refTransform.TransformVector(velocitySnapshots[0]);
        }

        public Vector3 GetAverageVelocityInSnapshots()
        {
            if (velocitySnapshots.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 vSum = Vector3.zero;
            foreach (Vector3 v in velocitySnapshots)
            {
                vSum += v;
            }
            Vector3 vAverage = vSum / velocitySnapshots.Count;

            return refTransform == null ? vAverage : refTransform.TransformVector(vAverage);
        }

        private void EnsureTransformSync()
        {
            if (transformSync == null)
            {
                transformSync = new GameObject().transform;
            }
        }

        private void CreateDebugVelocityLine()
        {
            debugVelocityLine = gameObject.AddComponent<LineRenderer>();
            debugVelocityLine.useWorldSpace = true;
            debugVelocityLine.widthMultiplier = 0.006f;
            debugVelocityLine.positionCount = 2;
            debugVelocityLine.material.color = new Color(0.9f, 0.33f, 0.31f);
            debugVelocityLine.sortingOrder = LayerUtils.getWorldspaceUiLayer();
            debugVelocityLine.enabled = false;
        }
    }
}
