using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BgmPlaylist", menuName = "Shizuku City/Audio/BGM Playlist")]
public sealed class BgmPlaylist : ScriptableObject
{
    [SerializeField] private string playlistId = "default";
    [SerializeField] private string displayName = "Playlist";
    [SerializeField] private List<BgmKey> tracks = new();
    [SerializeField] private BgmKey initialTrack = BgmKey.None;

    public string PlaylistId => playlistId;
    public string DisplayName => displayName;
    public IReadOnlyList<BgmKey> Tracks => tracks;
    public BgmKey InitialTrack => initialTrack;

    private void OnValidate()
    {
        playlistId = playlistId?.Trim();
        tracks.RemoveAll(key => key == BgmKey.None);
    }
}
