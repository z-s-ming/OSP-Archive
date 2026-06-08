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
        LoadHostConnection(ref hostAddress, ref hostPosePort);
    }

    public static void Save(int userId, string hostAddress, int hostPosePort, int expectedUserCount)
    {
        SaveHostConnection(hostAddress, hostPosePort);
    }

    public static void Save(int userId, string hostAddress, int hostPosePort, int expectedUserCount, bool proactiveResetEnabled)
    {
        SaveHostConnection(hostAddress, hostPosePort);
    }

    public static void LoadHostConnection(ref string hostAddress, ref int hostPosePort)
    {
        ClearRuntimeStateKeys();
        hostAddress = PlayerPrefs.GetString(HostKey, hostAddress);
        hostPosePort = PlayerPrefs.GetInt(PortKey, hostPosePort);
    }

    public static void SaveHostConnection(string hostAddress, int hostPosePort)
    {
        ClearRuntimeStateKeys();
        PlayerPrefs.SetString(HostKey, string.IsNullOrEmpty(hostAddress) ? string.Empty : hostAddress);
        PlayerPrefs.SetInt(PortKey, Mathf.Max(1, hostPosePort));
        PlayerPrefs.Save();
    }

    public static bool LoadProactiveResetEnabled(bool fallback)
    {
        ClearRuntimeStateKeys();
        return fallback;
    }

    public static void ClearRuntimeStateKeys()
    {
        bool changed = false;
        changed |= DeleteKeyIfPresent(UserIdKey);
        changed |= DeleteKeyIfPresent(UsersKey);
        changed |= DeleteKeyIfPresent(ProactiveResetKey);
        if (changed)
            PlayerPrefs.Save();
    }

    private static bool DeleteKeyIfPresent(string key)
    {
        if (!PlayerPrefs.HasKey(key))
            return false;

        PlayerPrefs.DeleteKey(key);
        return true;
    }
}
