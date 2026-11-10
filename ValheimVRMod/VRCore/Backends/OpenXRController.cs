using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using static ValheimVRMod.Utilities.LogUtils;
using InputDevice = UnityEngine.InputSystem.InputDevice;

namespace ValheimVRMod.VRCore.Backends
{
    enum XRHand { Left = 0, Right = 1 }

    // One hand's controller: the Input System device that the OpenXR plugin creates for the interaction profile in
    // use (OpenXRLoaderSetup enables them), e. g. the OculusTouchController layout. Its controls are read by their
    // names in that layout ("thumbstick", "primaryButton"...), through the profile's bindings (OpenXRProfile); a
    // control the controller lacks reads as released or zero. Nothing here names the plugin's device classes, which
    // would load Unity.XR.OpenXR.dll under SteamVR too: the device is found by its layout name.
    sealed class OpenXRController
    {
        private readonly Dictionary<string, InputControl> controls = new Dictionary<string, InputControl>();
        private InputDevice device;

        public bool IsConnected => device != null && device.added;
        public string Name => IsConnected ? device.layout + " (\"" + device.description.product + "\")" : null;
        // The profile whose bindings and grip offset apply; null for a controller of no profile the mod knows of.
        public OpenXRProfile Profile { get; private set; }

        // Where the SteamVR raw pose of the controller (what the hands of the rig follow, and what the mod's held
        // item offsets and laser pointer are made for) is from the OpenXR grip pose.
        private Vector3 rawPositionInGrip;
        private Quaternion rawRotationInGrip = Quaternion.identity;
        // The OpenXR grip pose in the raw pose.
        private Vector3 gripPosition;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetDevice(InputDevice newDevice, XRHand hand)
        {
            if (newDevice == device)
            {
                return;
            }
            device = newDevice;
            controls.Clear();
            Profile = device == null ? null : OpenXRProfiles.Find(device.layout, device.description.product);
            setGrip(Profile ?? OpenXRProfiles.Touch, hand);
        }

        private InputControl Control(string path)
        {
            if (!IsConnected)
            {
                return null;
            }
            if (!controls.TryGetValue(path, out InputControl control))
            {
                controls[path] = control = device.TryGetChildControl(path);
            }
            return control;
        }

        public bool Has(string path) => Control(path) != null;

        // A mod action as pressed on this hand so far this frame (OpenXRInputBackend), for the chords.
        internal System.Func<string, bool> ActionState;
        public bool ActionPressed(string action) => ActionState != null && ActionState(action);

        // A button, or an axis pressed at least half way.
        public bool Button(string path)
        {
            InputControl control = Control(path);
            if (control is ButtonControl button) return button.isPressed;
            return control is AxisControl axis && axis.ReadValue() >= 0.5f;
        }

        // An axis, or a button as 0 or 1 (ButtonControl is an AxisControl).
        public float Axis(string path) => Control(path) is AxisControl axis ? axis.ReadValue() : 0;

        public Vector2 Vector2(string path) =>
            Control(path) is InputControl<Vector2> control ? control.ReadValue() : UnityEngine.Vector2.zero;

        public Vector3 Vector3(string path) =>
            Control(path) is InputControl<Vector3> control ? control.ReadValue() : UnityEngine.Vector3.zero;

        public Quaternion Rotation(string path) =>
            Control(path) is InputControl<Quaternion> control ? control.ReadValue() : Quaternion.identity;

        // The grip pose ("devicePose" in every profile), in tracking space; false when it is not tracked.
        public bool TryGetGripPose(out Vector3 position, out Quaternion rotation)
        {
            position = UnityEngine.Vector3.zero;
            rotation = Quaternion.identity;
            if (!IsConnected || !Button("devicePose/isTracked"))
            {
                return false;
            }
            position = Vector3("devicePose/position");
            rotation = Rotation("devicePose/rotation");
            return true;
        }

        // The velocities the runtime gives for the grip pose, zero where it gives none.
        public void GetGripVelocity(out Vector3 velocity, out Vector3 angularVelocity)
        {
            velocity = Vector3("devicePose/velocity");
            angularVelocity = Vector3("devicePose/angularVelocity");
        }

        // The device's controls, for the log.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Controls()
        {
            if (!IsConnected) return "none";
            var names = new List<string>();
            foreach (InputControl control in device.allControls)
            {
                if (control.parent == device) names.Add(control.name);
            }
            return string.Join(", ", names.ToArray());
        }

        public void GripToRaw(Vector3 gripPos, Quaternion gripRot, out Vector3 rawPos, out Quaternion rawRot)
        {
            rawRot = gripRot * rawRotationInGrip;
            rawPos = gripPos + gripRot * rawPositionInGrip;
        }

        // Where the grip pose is from the raw pose, in tracking space, for the velocity of the raw pose.
        public Vector3 GripOffset(Quaternion rawRot) => rawRot * gripPosition;

        private void setGrip(OpenXRProfile profile, XRHand hand)
        {
            Vector3 position = profile.GripPosition;
            if (hand == XRHand.Left)
            {
                position.x = -position.x;
            }
            Vector3 euler = profile.GripEuler;
            // Into Unity's coordinates: z negated, and with it the sense of rotations about x and y.
            position.z = -position.z;
            Quaternion rotation = Quaternion.Euler(-euler.x, -euler.y, euler.z);
            gripPosition = position;
            // grip = raw * (position, rotation), so raw = grip * inverse(position, rotation).
            rawRotationInGrip = Quaternion.Inverse(rotation);
            rawPositionInGrip = -(Quaternion.Inverse(rotation) * position);
        }

        // The hand's controller among the Input System's devices: an XR controller used as that hand, one of a
        // profile the mod knows of first. Null when there is none.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InputDevice FindDevice(XRHand hand)
        {
            InternedString usage = hand == XRHand.Left ? CommonUsages.LeftHand : CommonUsages.RightHand;
            InputDevice fallback = null;
            foreach (InputDevice candidate in InputSystem.devices)
            {
                if (!(candidate is XRController) || !candidate.added || !candidate.usages.Contains(usage))
                {
                    continue;
                }
                if (OpenXRProfiles.Find(candidate.layout, candidate.description.product) != null)
                {
                    return candidate;
                }
                fallback = fallback ?? candidate;
            }
            return fallback;
        }

        // Makes sure the Input System keeps the plugin's controllers. A game may limit it to the devices it supports
        // (InputSettings.supportedDevices, e. g. keyboard, mouse and gamepads), and the Input System then drops every
        // other device: with the controllers dropped, the hands would neither move nor press anything. Adding
        // XRController, the base layout of every profile's device, brings back the devices it dropped.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void AllowControllers()
        {
            InputSettings settings = InputSystem.settings;
            var supported = new List<string>();
            foreach (string layout in settings.supportedDevices)
            {
                supported.Add(layout);
            }
            LogInfo("Input System: update mode " + settings.updateMode + ", supported devices: " +
                    (supported.Count == 0 ? "all" : string.Join(", ", supported.ToArray())) + ".");
            if (supported.Count == 0 || supported.Contains("XRController"))
            {
                return;
            }
            supported.Add("XRController");
            settings.supportedDevices = new ReadOnlyArray<string>(supported.ToArray());
            LogInfo("Input System: added XRController to the supported devices for the OpenXR controllers.");
        }

        // Every XR device of the Input System, for when a hand is not found or not the expected controller.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string DescribeDevices()
        {
            var lines = new List<string>();
            foreach (InputDevice candidate in InputSystem.devices)
            {
                if (candidate is XRController || candidate is XRHMD || candidate.description.interfaceName == "XRInput")
                {
                    lines.Add(candidate.layout + " \"" + candidate.description.product + "\" [" +
                              string.Join(",", toStrings(candidate.usages)) + "]");
                }
            }
            return lines.Count == 0 ? "none" : string.Join("; ", lines.ToArray());
        }

        private static string[] toStrings(ReadOnlyArray<InternedString> values)
        {
            var strings = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                strings[i] = values[i].ToString();
            }
            return strings;
        }
    }
}
