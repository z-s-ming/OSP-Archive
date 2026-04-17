using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class PreDefinedEpisode : Episode
{
    private string filePath;
    private TextReader reader;
    private List<Vector2> targetPositionList;

    public PreDefinedEpisode() : base() { }

    public PreDefinedEpisode(string fileName) : base() {
        targetPositionList = new List<Vector2>();
        filePath = "Assets/Resources/" + fileName +".txt";
        reader = File.OpenText(filePath);

        string line = null;
        while ((line = reader.ReadLine()) != null) {
            string[] num = line.Split(',');
            float x = float.Parse(num[0]);
            float y = float.Parse(num[1]);
            targetPositionList.Add(new Vector2(x, y));
        }
        reader.Close();

        if (this.episodeLength != targetPositionList.Count)
            this.episodeLength = targetPositionList.Count;
    }

    protected override void GenerateEpisode(Transform2D virtualUserTransform, Space2D virtualSpace, Object2D virtualUser)
    {
        Vector2 userPosition = virtualUserTransform.localPosition;
        Vector2 candidate = targetPositionList[currentEpisodeIndex];

        currentTargetPosition = IsValidTargetCandidate(virtualSpace, userPosition, candidate, 0.5f)
            ? candidate
            : userPosition;
    }
}
