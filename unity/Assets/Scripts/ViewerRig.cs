using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace ScanViewer
{
    // No scripted camera path or gravity. Head pose always comes from the headset.
    public class ViewerRig : MonoBehaviour
    {
        public Camera eye;
        public Transform leftHand, rightHand;
        public Vector3 startPosition, startForward = Vector3.forward;
        public Vector4 walkBounds = new Vector4(-5,-5,5,5);
        public float speed = 1.2f;
        readonly List<XRInputSubsystem> inputs = new();
        bool placed, turnHeld, floorReady;

        void OnEnable() => Application.onBeforeRender += Pose;
        void OnDisable() => Application.onBeforeRender -= Pose;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            while (true)
            {
                var manager = XRGeneralSettings.Instance?.Manager;
                if (manager != null && manager.activeLoader == null)
                {
                    yield return manager.InitializeLoader();
                    if (manager.activeLoader != null) manager.StartSubsystems();
                }
                yield return new WaitForSecondsRealtime(10);
            }
        }

        bool HeadTracked()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked) && tracked;
        }

        void Pose()
        {
            UpdatePose(XRNode.Head,eye.transform);
            UpdatePose(XRNode.LeftHand,leftHand);
            UpdatePose(XRNode.RightHand,rightHand);
        }

        static void UpdatePose(XRNode node,Transform target)
        {
            if (!target) return;
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.TryGetFeatureValue(CommonUsages.devicePosition,out Vector3 p)) target.localPosition=p;
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation,out Quaternion q)) target.localRotation=q;
        }

        void Update()
        {
            bool tracked=HeadTracked(); Application.targetFrameRate=tracked ? -1 : 30;
            if (!tracked) return;
            if (!floorReady)
            {
                SubsystemManager.GetSubsystems(inputs);
                foreach (var input in inputs)
                    if (input.running && input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor)) floorReady=true;
                return; // Let floor origin settle before placing the rig.
            }
            Pose();
            if (!placed)
            {
                float delta=Vector3.SignedAngle(Vector3.ProjectOnPlane(eye.transform.forward,Vector3.up), startForward,Vector3.up);
                transform.RotateAround(eye.transform.position,Vector3.up,delta);
                Vector3 shift=startPosition-eye.transform.position;shift.y=0;transform.position+=shift;
                placed=true;Debug.Log("[ScanViewer] Tracked floor origin placed; manual movement only.");
            }
            var left=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (left.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 move))
            {
                Vector3 forward=Vector3.ProjectOnPlane(eye.transform.forward,Vector3.up).normalized;
                Vector3 across=Vector3.Cross(Vector3.up,forward);
                Vector3 step=(forward*move.y+across*move.x)*speed*Time.unscaledDeltaTime;
                if (move.magnitude>.15f)
                {
                    Vector3 desired=eye.transform.position+step;
                    desired.x=Mathf.Clamp(desired.x,walkBounds.x,walkBounds.z);
                    desired.z=Mathf.Clamp(desired.z,walkBounds.y,walkBounds.w);
                    transform.position+=new Vector3(desired.x-eye.transform.position.x,0,desired.z-eye.transform.position.z);
                }
            }
            if (right.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 turn))
            {
                if (Mathf.Abs(turn.x)>.7f && !turnHeld)
                {transform.RotateAround(eye.transform.position,Vector3.up,Mathf.Sign(turn.x)*30);turnHeld=true;}
                else if (Mathf.Abs(turn.x)<.3f) turnHeld=false;
            }
        }
    }
}
