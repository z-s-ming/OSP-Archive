public class LongRandomEpisode : RandomEpisode
{
    public LongRandomEpisode() : base() { }

    public LongRandomEpisode(int episodeLength) : base(episodeLength) { }

    protected override float MinSegmentDistance { get { return 8.0f; } }
    protected override float MaxSegmentDistance { get { return 12.0f; } }
    protected override string EpisodeName { get { return "LongRandomEpisode"; } }
}
