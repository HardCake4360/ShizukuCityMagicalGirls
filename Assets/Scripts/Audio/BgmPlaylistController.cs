using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BgmPlaylistController : MonoBehaviour
{
    private const string DefaultPlaylistResourcePath = "Audio/BgmPlaylist";

    [SerializeField] private BgmPlaylist playlist;

    private readonly List<BgmKey> orderedTracks = new();
    private BgmManager manager;
    private int currentIndex = -1;

    public IReadOnlyList<BgmKey> OrderedTracks => orderedTracks;
    public int CurrentIndex => currentIndex;

    public event Action PlaylistChanged;
    public event Action<int, BgmKey> CurrentTrackChanged;

    private void Awake()
    {
        if (playlist == null)
        {
            playlist = Resources.Load<BgmPlaylist>(DefaultPlaylistResourcePath);
        }

        manager = BgmManager.Instance;
        ResetFromDefinition();
        manager.TrackChanged += OnManagerTrackChanged;
    }

    private void Start()
    {
        PlayInitialTrack();
    }

    private void OnDestroy()
    {
        if (manager != null)
        {
            manager.TrackChanged -= OnManagerTrackChanged;
        }
    }

    public void ResetFromDefinition()
    {
        orderedTracks.Clear();
        if (playlist != null)
        {
            for (int i = 0; i < playlist.Tracks.Count; i++)
            {
                BgmKey key = playlist.Tracks[i];
                if (key != BgmKey.None && !orderedTracks.Contains(key))
                {
                    orderedTracks.Add(key);
                }
            }
        }

        currentIndex = orderedTracks.IndexOf(manager.CurrentKey);
        PlaylistChanged?.Invoke();
    }

    public bool PlayInitialTrack()
    {
        if (playlist == null || playlist.InitialTrack == BgmKey.None)
        {
            return false;
        }

        int initialIndex = orderedTracks.IndexOf(playlist.InitialTrack);
        if (initialIndex >= 0)
        {
            return PlayAt(initialIndex);
        }

        if (!manager.Play(playlist.InitialTrack))
        {
            return false;
        }

        currentIndex = -1;
        CurrentTrackChanged?.Invoke(currentIndex, playlist.InitialTrack);
        return true;
    }

    public bool PlayAt(int index)
    {
        if (index < 0 || index >= orderedTracks.Count)
        {
            return false;
        }

        if (!manager.Play(orderedTracks[index]))
        {
            return false;
        }

        currentIndex = index;
        CurrentTrackChanged?.Invoke(currentIndex, orderedTracks[currentIndex]);
        return true;
    }

    public bool PlayNext()
    {
        if (orderedTracks.Count == 0)
        {
            return false;
        }

        int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % orderedTracks.Count;
        return PlayAt(nextIndex);
    }

    public bool PlayPrevious()
    {
        if (orderedTracks.Count == 0)
        {
            return false;
        }

        int previousIndex = currentIndex < 0
            ? orderedTracks.Count - 1
            : (currentIndex - 1 + orderedTracks.Count) % orderedTracks.Count;
        return PlayAt(previousIndex);
    }

    public void TogglePlayPause()
    {
        if (manager.IsPaused)
        {
            manager.Resume();
        }
        else if (manager.IsPlaying)
        {
            manager.Pause();
        }
        else if (currentIndex >= 0)
        {
            PlayAt(currentIndex);
        }
        else
        {
            PlayNext();
        }
    }

    public bool MoveTrack(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= orderedTracks.Count
            || toIndex < 0 || toIndex >= orderedTracks.Count
            || fromIndex == toIndex)
        {
            return false;
        }

        BgmKey currentKey = currentIndex >= 0 ? orderedTracks[currentIndex] : BgmKey.None;
        BgmKey movedTrack = orderedTracks[fromIndex];
        orderedTracks.RemoveAt(fromIndex);
        orderedTracks.Insert(toIndex, movedTrack);
        currentIndex = currentKey == BgmKey.None ? -1 : orderedTracks.IndexOf(currentKey);
        PlaylistChanged?.Invoke();
        return true;
    }

    private void OnManagerTrackChanged(BgmKey key)
    {
        currentIndex = orderedTracks.IndexOf(key);
        CurrentTrackChanged?.Invoke(currentIndex, key);
    }
}
