using System;
using UnityEngine;

[Serializable]
public class LiveVRProtocolVersionContext
{
    [SerializeField] private string hostRunId = string.Empty;
    [SerializeField] private string runId = string.Empty;
    [SerializeField] private int trialId;
    [SerializeField] private int configVersion;
    [SerializeField] private int restartEpoch;
    [SerializeField] private int calibrationVersion;
    [SerializeField] private int assignmentVersion;

    public string HostRunId { get { return hostRunId; } set { hostRunId = value ?? string.Empty; } }
    public string RunId { get { return runId; } set { runId = value ?? string.Empty; } }
    public int TrialId { get { return trialId; } set { trialId = Mathf.Max(0, value); } }
    public int ConfigVersion { get { return configVersion; } set { configVersion = Mathf.Max(0, value); } }
    public int RestartEpoch { get { return restartEpoch; } set { restartEpoch = Mathf.Max(0, value); } }
    public int CalibrationVersion { get { return calibrationVersion; } set { calibrationVersion = Mathf.Max(0, value); } }
    public int AssignmentVersion { get { return assignmentVersion; } set { assignmentVersion = Mathf.Max(0, value); } }

    public void EnsureHostRunId()
    {
        if (string.IsNullOrEmpty(hostRunId))
            hostRunId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        if (string.IsNullOrEmpty(runId))
            runId = hostRunId;
    }

    public int IncrementRestartEpoch()
    {
        restartEpoch++;
        return restartEpoch;
    }

    public int IncrementCalibrationVersion()
    {
        calibrationVersion++;
        return calibrationVersion;
    }

    public int IncrementAssignmentVersion()
    {
        assignmentVersion++;
        return assignmentVersion;
    }

    public bool Accepts(LiveVRControlEnvelope envelope)
    {
        bool isNewerRestartEpoch = envelope.RestartEpoch > restartEpoch;
        if (!string.IsNullOrEmpty(hostRunId) &&
            !string.IsNullOrEmpty(envelope.HostRunId) &&
            !string.Equals(hostRunId, envelope.HostRunId, StringComparison.Ordinal) &&
            !isNewerRestartEpoch)
        {
            return false;
        }

        if (envelope.RestartEpoch < restartEpoch)
            return false;

        if (envelope.CalibrationVersion > 0 &&
            calibrationVersion > 0 &&
            envelope.CalibrationVersion != calibrationVersion)
        {
            return false;
        }

        return true;
    }

    public LiveVRProtocolVersionContext Clone()
    {
        return new LiveVRProtocolVersionContext
        {
            HostRunId = hostRunId,
            RunId = runId,
            TrialId = trialId,
            ConfigVersion = configVersion,
            RestartEpoch = restartEpoch,
            CalibrationVersion = calibrationVersion,
            AssignmentVersion = assignmentVersion
        };
    }
}
