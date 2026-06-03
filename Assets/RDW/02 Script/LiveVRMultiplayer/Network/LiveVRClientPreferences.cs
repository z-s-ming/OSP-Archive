using UnityEngine;

public static class LiveVRClientPreferences
{
    private const string UserIdKey = "LiveVR.Client.UserId";
    private const string HostKey = "LiveVR.Client.Host";
    private const string PortKey = "LiveVR.Client.Port";
    private const string UsersKey = "LiveVR.Client.ExpectedUsers";
    private const string ProactiveResetKey = "LiveVR.Client.ProactiveResetEnabled";

    public static void Load(ref int userId, ref string hostAddress, ref int hostPosePort, ref int expectedUserCount)
    {
        userId = PlayerPrefs.GetInt(UserIdKey, userId);
        hostAddress = PlayerPrefs.GetString(HostKey, hostAddress);
        hostPosePort = PlayerPrefs.GetInt(PortKey, hostPosePort);
        expectedUserCount = PlayerPrefs.GetInt(UsersKey, expectedUserCount);
    }

    public static void Save(int userId, string hostAddress, int hostPosePort, int expectedUserCount)
    {
        Save(userId, hostAddress, hostPosePort, expectedUserCount, LoadProactiveResetEnabled(true));
    }

    public static void Save(int userId, string hostAddress, int hostPosePort, int expectedUserCount, bool proactiveResetEnabled)
    {
        PlayerPrefs.SetInt(UserIdKey, Mathf.Max(0, userId));
        PlayerPrefs.SetString(HostKey, string.IsNullOrEmpty(hostAddress) ? string.Empty : hostAddress);
        PlayerPrefs.SetInt(PortKey, Mathf.Max(1, hostPosePort));
        PlayerPrefs.SetInt(UsersKey, Mathf.Max(1, expectedUserCount));
        PlayerPrefs.SetInt(ProactiveResetKey, proactiveResetEnabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void LoadHostConnection(ref string hostAddress, ref int hostPosePort)
    {
        hostAddress = PlayerPrefs.GetString(HostKey, hostAddress);
        hostPosePort = PlayerPrefs.GetInt(PortKey, hostPosePort);
    }

    public static void SaveHostConnection(string hostAddress, int hostPosePort)
    {
        PlayerPrefs.SetString(HostKey, string.IsNullOrEmpty(hostAddress) ? string.Empty : hostAddress);
        PlayerPrefs.SetInt(PortKey, Mathf.Max(1, hostPosePort));
        PlayerPrefs.Save();
    }

    public static bool LoadProactiveResetEnabled(bool fallback)
    {
        return PlayerPrefs.GetInt(ProactiveResetKey, fallback ? 1 : 0) != 0;
    }
}
