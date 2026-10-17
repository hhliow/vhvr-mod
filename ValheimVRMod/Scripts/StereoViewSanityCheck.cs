using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

using static ValheimVRMod.Utilities.LogUtils;

namespace ValheimVRMod.Scripts
{
    // Watches that a stereo camera renders the eye views of where it is. A camera whose view matrices do not
    // follow its transform, e. g. ones frozen by Camera.CopyFrom() before XR had set up proper eye views, shows
    // the world turning the wrong way as the head turns, and each eye's image offset to the wrong side, which reads
    // as double vision. That is logged with what is known about the camera; it is left as it is so as not to hide
    // the bug that caused it.
    // Which cameras render to the headset is also logged once per scene, since a second one drawing over the
    // others would show things twice as well.
    [RequireComponent(typeof(Camera))]
    class StereoViewSanityCheck : MonoBehaviour
    {
        // Eyes of canted displays turn outwards by a few degrees, far less than this.
        private const float MAX_EYE_ANGLE = 30f;
        private const float CHECK_INTERVAL = 1f;
        private const int MAX_LOGGED_MISMATCHES = 5;

        private static string lastLoggedScene;

        private Camera stereoCamera;
        private float nextCheckTime;
        private int mismatchCount;

        void Awake()
        {
            stereoCamera = GetComponent<Camera>();
        }

        void OnEnable()
        {
            Camera.onPostRender += OnCameraPostRender;
        }

        void OnDisable()
        {
            Camera.onPostRender -= OnCameraPostRender;
        }

        private void OnCameraPostRender(Camera camera)
        {
            if (camera != stereoCamera || !camera.stereoEnabled || Time.unscaledTime < nextCheckTime)
            {
                return;
            }
            nextCheckTime = Time.unscaledTime + CHECK_INTERVAL;

            maybeLogStereoCameras();

            Matrix4x4 leftEyeToWorld = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse;
            Matrix4x4 rightEyeToWorld = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse;
            // View space looks down -z.
            float leftAngle = Vector3.Angle(-(Vector3)leftEyeToWorld.GetColumn(2), transform.forward);
            float rightAngle = Vector3.Angle(-(Vector3)rightEyeToWorld.GetColumn(2), transform.forward);
            float eyeOrder = Vector3.Dot(
                (Vector3)rightEyeToWorld.GetColumn(3) - (Vector3)leftEyeToWorld.GetColumn(3), transform.right);
            if (leftAngle <= MAX_EYE_ANGLE && rightAngle <= MAX_EYE_ANGLE && eyeOrder >= 0)
            {
                return;
            }

            // So as not to flood the log once a second.
            if (++mismatchCount > MAX_LOGGED_MISMATCHES)
            {
                return;
            }
            LogWarning(
                "Stereo camera " + camera.name + " renders eye views that do not match its pose" +
                " (scene: " + SceneManager.GetActiveScene().name +
                ", depth: " + camera.depth +
                ", target eye: " + camera.stereoTargetEye +
                ", left eye off by " + leftAngle.ToString("F1") + " degrees" +
                ", right eye off by " + rightAngle.ToString("F1") + " degrees" +
                ", right eye " + eyeOrder.ToString("F3") + " m to the right of the left eye," +
                " mismatch " + mismatchCount + ").");
        }

        private static void maybeLogStereoCameras()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == lastLoggedScene)
            {
                return;
            }
            lastLoggedScene = scene;
            var cameras = new StringBuilder("Cameras rendering to the headset (scene: " + scene + "):");
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera.stereoTargetEye == StereoTargetEyeMask.None || camera.targetTexture != null)
                {
                    continue;
                }
                cameras.Append(
                    " " + camera.name + " (depth " + camera.depth + ", target eye " + camera.stereoTargetEye +
                    ", culling mask " + camera.cullingMask + ");");
            }
            LogInfo(cameras.ToString());
        }
    }
}
