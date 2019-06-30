﻿using QuestomAssets.AssetsChanger;
using QuestomAssets.BeatSaber;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static QuestomAssets.MusicConfigCache;

namespace QuestomAssets.AssetOps
{
    public class AutoCreatePlaylistsOp : AssetOp
    {
        public override bool IsWriteOp => true;

        public AutoCreatePlaylistsOp(PlaylistSortMode sortMode, int maxPerNamePlaylist)
        {
            SortMode = sortMode;
            MaxPerNamePlaylist = maxPerNamePlaylist;
        }

        public PlaylistSortMode SortMode { get; private set; }
        public int MaxPerNamePlaylist { get; set; }
        internal override void PerformOp(OpContext context)
        {
            Comparer<SongAndPlaylist> comparer;
            Func<SongAndPlaylist, string> nameGetter;
            switch (SortMode)
            {
                case PlaylistSortMode.Name:
                    nameGetter = (s) =>
                    {
                        if (s.Song.SongName?.Length < 1)
                            return "";
                        return s.Song.SongName.Substring(0, 1).ToUpper();
                    };
                    comparer = Comparer<SongAndPlaylist>.Create((s1, s2) =>
                    {
                        try
                        {
                            return s1.Song.SongName.ToUpper().CompareTo(s2.Song.Name.ToUpper());
                        }
                        catch
                        {
                            return -1;
                        }
                    });
                    break;
                case PlaylistSortMode.MaxDifficulty:
                    nameGetter = (s) =>
                    {
                        var max = s.Song?.DifficultyBeatmapSets?.SelectMany(x => x?.DifficultyBeatmaps)?.Max(x => x?.Difficulty);
                        if (max == null)
                            return Difficulty.Easy.ToString();

                        return max.ToString();
                    };
                    comparer = Comparer<SongAndPlaylist>.Create((s1, s2) =>
                    {
                        try
                        {
                            return s1.Song.DifficultyBeatmapSets.SelectMany(x => x.DifficultyBeatmaps).Max(x => x.Difficulty)
                                .CompareTo(s2.Song.DifficultyBeatmapSets.SelectMany(x => x.DifficultyBeatmaps).Max(x => x.Difficulty));
                        }
                        catch
                        {
                            return -1;
                        }
                    });
                    break;
                case PlaylistSortMode.LevelAuthor:
                    nameGetter = (s) =>
                    {
                        return s.Song?.LevelAuthorName?.ToUpper()??"";
                    };
                    comparer = Comparer<SongAndPlaylist>.Create((s1, s2) =>
                    {
                        try
                        {
                            return s1.Song.LevelAuthorName.ToUpper().CompareTo(s2.Song.LevelAuthorName.ToUpper());
                        }
                        catch
                        {
                            return -1;
                        }
                    });
                    break;
                default:
                    throw new NotImplementedException("Unhandled playlist sort mode.");
            }
            var songList = context.Cache.SongCache.Values.ToArray();
            Array.Sort(songList, comparer);
            var songsAssetFile = context.Engine.GetSongsAssetsFile();
            PlaylistAndSongs currentPlaylist = null;
            int plCtr = 0;
            for (int i = 0; i < songList.Count(); i++)
            {
                var song = songList[i];
                var curSongName = nameGetter(song);
                var packID = $"Auto_{curSongName}";
                if (currentPlaylist == null || currentPlaylist.Playlist.PackID != packID)
                {
                    bool newPlaylist = true;
                    if (currentPlaylist != null && SortMode == PlaylistSortMode.Name)
                    {
                        if (plCtr < MaxPerNamePlaylist)
                        {
                            newPlaylist = false;
                        } else
                        {
                            currentPlaylist.Playlist.PackName = " - " + curSongName;
                        }
                    }
                    if (newPlaylist)
                    {
                        if (context.Cache.PlaylistCache.ContainsKey(packID))
                        {
                            currentPlaylist = context.Cache.PlaylistCache[packID];
                        }
                        else
                        {
                            currentPlaylist = new PlaylistAndSongs()
                            {
                                Playlist = OpCommon.CreatePlaylist(context, new Models.BeatSaberPlaylist()
                                {
                                    PlaylistID = packID,
                                    PlaylistName = curSongName
                                }, songsAssetFile)
                            };
                        }
                        plCtr = 0;
                    }                    
                }

                //update assets
                var oldPl = context.Cache.PlaylistCache[song.Playlist.PackID];
                var oldPtr = oldPl.Playlist.BeatmapLevelCollection.Object.BeatmapLevels.Where(x => x.Object.LevelID == song.Song.LevelID).First();
                oldPtr.Dispose();
                oldPl.Playlist.BeatmapLevelCollection.Object.BeatmapLevels.Remove(oldPtr);
                currentPlaylist.Playlist.BeatmapLevelCollection.Object.BeatmapLevels.Add(song.Song.PtrFrom(currentPlaylist.Playlist.BeatmapLevelCollection.Object));
                
                //update cache
                oldPl.Songs.Remove(song.Song.LevelID);
                song.Playlist = currentPlaylist.Playlist;
                currentPlaylist.Songs.Add(song.Song.LevelID, new OrderedSong() { Song = song.Song, Order = plCtr });

                plCtr++;
            }
        }
    }
}
