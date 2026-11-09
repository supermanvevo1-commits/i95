using SurviveI95.Sim;
using SurviveI95.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SurviveI95.Game
{
    public class PlayerCar : MonoBehaviour
    {
        public string routeId = "florida_jupiter";
        public float startS = 300f;
        public Camera chaseCamera;
        public float cameraBack = 7.5f, cameraUp = 2.6f;

        RoutePath path;
        readonly DriveSim sim = new DriveSim();
        float prevS, prevLat;
        Vector3 camVelocity;

        void Awake() { path = RoutePath.Load(routeId); Place(startS); }

        public void Place(float s)
        {
            path ??= RoutePath.Load(routeId);
            sim.S = prevS = s;
            sim.Lateral = prevLat = path.HalfWidth(s) - RoutePath.LaneWidth / 2f;
            Pose(s, sim.Lateral);
            SnapCamera();
        }

        void FixedUpdate()
        {
            var k = Keyboard.current;
            var input = new DriveInput();
            if (k != null)
            {
                input.Throttle = k.wKey.isPressed || k.upArrowKey.isPressed;
                input.Brake = k.sKey.isPressed || k.downArrowKey.isPressed;
                input.Steer = ((k.dKey.isPressed || k.rightArrowKey.isPressed) ? 1 : 0) - ((k.aKey.isPressed || k.leftArrowKey.isPressed) ? 1 : 0);
            }
            prevS = sim.S; prevLat = sim.Lateral;
            sim.Step(path, input, Time.fixedDeltaTime);
            if (sim.S < prevS) { prevS = sim.S; prevLat = sim.Lateral; }
        }

        void Update()
        {
            float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            Pose(Mathf.Lerp(prevS, sim.S, t), Mathf.Lerp(prevLat, sim.Lateral, t));
        }

        void Pose(float s, float lateral)
        {
            transform.position = path.Point(s, lateral);
            var tan = path.Tangent(s);
            float yaw = sim.Speed > 1f ? Mathf.Atan2(sim.LateralVelocity, sim.Speed) * Mathf.Rad2Deg : 0;
            transform.rotation = Quaternion.LookRotation(tan, Vector3.up) * Quaternion.Euler(0, yaw, 0);
        }

        void LateUpdate()
        {
            if (chaseCamera == null) return;
            var goal = CameraGoal();
            chaseCamera.transform.position = Vector3.SmoothDamp(chaseCamera.transform.position, goal, ref camVelocity, 0.05f);
            chaseCamera.transform.rotation = Quaternion.LookRotation(transform.position + transform.forward * 14f + Vector3.up * 1.1f - chaseCamera.transform.position);
            chaseCamera.fieldOfView = Mathf.Lerp(chaseCamera.fieldOfView, 62f + 16f * Mathf.Clamp01(sim.Speed / sim.TopSpeed), Time.deltaTime * 4f);
        }

        Vector3 CameraGoal() => transform.position - path.Tangent(sim.S) * cameraBack + Vector3.up * cameraUp;

        public void SnapCamera()
        {
            if (chaseCamera == null) return;
            chaseCamera.transform.position = CameraGoal();
            chaseCamera.transform.rotation = Quaternion.LookRotation(transform.position + transform.forward * 14f + Vector3.up * 1.1f - chaseCamera.transform.position);
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            GUI.Label(new Rect(Screen.width - 330, Screen.height - 70, 300, 56), Mathf.RoundToInt(sim.Speed * DriveSim.MetersPerSecondToMph) + " MPH", style);
        }
    }
}
