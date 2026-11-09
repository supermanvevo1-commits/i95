using System;
using System.Collections;
using System.IO;
using System.Text;
using SurviveI95.Sim;
using UnityEngine;

namespace SurviveI95.Game
{
    /// <summary>Visible robot test: run the built game with -autotest -testOut DIR. Each step is captioned on screen.</summary>
    public class AutoPlayTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autotest") >= 0)
                new GameObject("AutoPlayTest").AddComponent<AutoPlayTest>();
        }

        class Step { public string id, caption; public float secs, mph = 200f, yaw, pitch; public int lane; public bool cockpit; }

        static readonly Step[] Steps =
        {
            new Step { id="01_accelerate", caption="Accelerating from a stop (chase camera)", secs=9 },
            new Step { id="02_lane_left", caption="Lane change to the left at speed", secs=6, lane=1 },
            new Step { id="03_lane_right", caption="Lane change back to the right", secs=6, lane=0 },
            new Step { id="04_brake", caption="Braking down to 45 mph", secs=6, mph=45 },
            new Step { id="05_cockpit_ahead", caption="Inside the car: can you see the road and dash?", secs=6, mph=70, cockpit=true },
            new Step { id="06_cockpit_left", caption="Inside the car: looking at the driver's side window", secs=5, mph=70, cockpit=true, yaw=-82, pitch=-4 },
            new Step { id="07_cockpit_right", caption="Inside the car: looking across to the passenger side", secs=5, mph=70, cockpit=true, yaw=70, pitch=-8 },
            new Step { id="08_cockpit_dash", caption="Inside the car: looking down at the dash", secs=4, mph=70, cockpit=true, pitch=-24 },
            new Step { id="09_cockpit_steer", caption="Inside the car: lane change, watch the steering wheel", secs=7, mph=70, cockpit=true, lane=1 },
        };

        string caption = "Robot test starting...";
        string detail = "";
        PlayerCar car;

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            string dir = args[Array.IndexOf(args, "-testOut") + 1];
            var bot = new LaneBot();
            PlayerCar.AutoDriver = (p, c) => bot.Think(p, c);
            car = FindAnyObjectByType<PlayerCar>();
            yield return new WaitForSeconds(3f);

            var log = new StringBuilder();
            float worst = 0; int frames = 0; float total = 0;
            foreach (var step in Steps)
            {
                caption = step.caption;
                bot.SpeedMph = step.mph; bot.TargetLaneFromRight = step.lane;
                car.SetCockpit(step.cockpit);
                car.ScriptedLook = new Vector2(step.yaw, step.pitch);
                float t = 0, stepWorst = 0; bool shot = false; int skip = 0;
                while (t < step.secs)
                {
                    yield return null;
                    t += Time.unscaledDeltaTime; total += Time.unscaledDeltaTime; frames++;
                    if (skip > 0) skip--; // frames spent writing a screenshot are not game hitches
                    else { stepWorst = Mathf.Max(stepWorst, Time.unscaledDeltaTime); worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
                    detail = $"{Mathf.RoundToInt(car.Sim.Speed * DriveSim.MetersPerSecondToMph)} mph   steering {car.Sim.Steer:+0.0;-0.0;0.0}   {(car.Sim.OffRoad ? "OFF ROAD" : "on road")}";
                    bool ready = step.id == "09_cockpit_steer" ? (Mathf.Abs(car.Sim.Steer) > 0.45f && t > 0.5f) || t > step.secs * 0.9f : t > step.secs * 0.7f;
                    if (!shot && ready) { ScreenCapture.CaptureScreenshot(Path.Combine(dir, step.id + ".png")); shot = true; skip = 3; }
                }
                float lane = car.Path.HalfWidth(car.Sim.S) - RoutePath_LaneCenter(step.lane) - car.Sim.Lateral;
                log.AppendLine($"{step.id}|{step.caption}|{car.Sim.Speed * DriveSim.MetersPerSecondToMph:F0} mph, {Mathf.Abs(lane):F2} m from lane center, {(car.Sim.OffRoad ? "OFF ROAD" : "on road")}, slowest frame {stepWorst * 1000f:F0} ms");
            }
            log.AppendLine($"performance||{frames / total:F0} FPS average, worst frame {worst * 1000f:F0} ms");
            File.WriteAllText(Path.Combine(dir, "steps.txt"), log.ToString());
            caption = "Test finished"; detail = "";
            yield return new WaitForSeconds(2f);
            Application.Quit();
        }

        static float RoutePath_LaneCenter(int lane) => World.RoutePath.LaneWidth * (lane + 0.5f);

        void OnGUI()
        {
            GUI.color = new Color(0, 0, 0, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, 96), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var small = new GUIStyle(big) { fontSize = 20, fontStyle = FontStyle.Normal };
            GUI.Label(new Rect(0, 6, Screen.width, 54), "ROBOT TEST: " + caption, big);
            GUI.Label(new Rect(0, 58, Screen.width, 32), detail, small);
        }
    }
}
