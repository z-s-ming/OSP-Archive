using System;
using System.IO;
using System.Text;
using UnityEngine;

public class HMDPoseLogger : MonoBehaviour
{
    [Header("Target")]
    public Transform hmdCamera;

    [Header("Logging")]
    public bool logToConsole = true;
    public bool writeCsv = true;
    public float sampleInterval = 0.1f;   // 10 Hz，先不要每帧写文件

    private float timer = 0f;
    private string csvPath;
    private StreamWriter writer;

    void Start()
    {
        if (hmdCamera == null)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                hmdCamera = cam.transform;
            }
        }

        if (hmdCamera == null)
        {
            Debug.LogError("[HMDPoseLogger] No HMD camera found. Please assign Main Camera.");
            enabled = false;
            return;
        }

        if (writeCsv)
        {
            string fileName = "hmd_pose_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
            csvPath = Path.Combine(Application.persistentDataPath, fileName);

            writer = new StreamWriter(csvPath, false, Encoding.UTF8);
            writer.WriteLine("time,frame,pos_x,pos_y,pos_z,yaw_deg,forward_x,forward_z");
            writer.Flush();

            Debug.Log("[HMDPoseLogger] CSV path: " + csvPath);
        }
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < sampleInterval) return;
        timer = 0f;

        Vector3 pos = hmdCamera.position;
        float yaw = GetYawDeg(hmdCamera);

        Vector3 fwd = hmdCamera.forward;
        Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z).normalized;

        string line = string.Format(
            "{0:F3},{1},{2:F4},{3:F4},{4:F4},{5:F2},{6:F4},{7:F4}",
            Time.time,
            Time.frameCount,
            pos.x,
            pos.y,
            pos.z,
            yaw,
            flatFwd.x,
            flatFwd.z
        );

        if (logToConsole)
        {
            Debug.Log("[HMDPose] " + line);
        }

        if (writeCsv && writer != null)
        {
            writer.WriteLine(line);
        }
    }

    private float GetYawDeg(Transform t)
    {
        Vector3 fwd = t.forward;
        fwd.y = 0f;

        if (fwd.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        fwd.Normalize();

        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        return NormalizeAngle180(yaw);
    }

    private float NormalizeAngle180(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    void OnApplicationQuit()
    {
        CloseWriter();
    }

    void OnDisable()
    {
        CloseWriter();
    }

    private void CloseWriter()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
            Debug.Log("[HMDPoseLogger] CSV saved: " + csvPath);
        }
    }
}