﻿using Emulamer.Utils;
using System;
using System.IO;
using QuestomAssets.AssetsChanger;
using QuestomAssets.BeatSaber;
using System.Collections.Generic;
using System.Linq;

namespace QuestomAssets
{
    public class QuestomAssetsEngine : IDisposable
    {
        private string _apkFilename;
        private Apkifier _apk;
        private bool _readOnly;

        //TODO: fix cross-asset file loading of stuff before turning this to false, some of the OST Vol 1 songs are in another file
        public bool HideOriginalPlaylists { get; private set; } = true;

        /// <summary>
        /// Create a new instance of the class and open the apk file
        /// </summary>
        /// <param name="apkFilename">The path to the Beat Saber APK file</param>
        /// <param name="readOnly">True to open the APK read only</param>
        /// <param name="pemCertificateData">The contents of the PEM certificate that will be used to sign the APK.  If omitted, a new self signed cert will be generated.</param>
        public QuestomAssetsEngine(string apkFilename, bool readOnly = false, string pemCertificateData = BSConst.DebugCertificatePEM)
        {
            _readOnly = readOnly;
            _apkFilename = apkFilename;
            _apk = new Apkifier(apkFilename, !readOnly, readOnly?null:pemCertificateData, readOnly);
        }

        private Dictionary<string, AssetsFile> _openAssetsFiles = new Dictionary<string, AssetsFile>();

        private AssetsFile OpenAssets(string assetsFilename)
        {
            if (_openAssetsFiles.ContainsKey(assetsFilename))
                return _openAssetsFiles[assetsFilename];
            AssetsFile assetsFile = new AssetsFile(_apk.ReadCombinedAssets(BSConst.KnownFiles.AssetsRootPath+assetsFilename), BSConst.GetAssetTypeMap());
            _openAssetsFiles.Add(assetsFilename, assetsFile);
            return assetsFile;
        }

        private void WriteAllOpenAssets()
        {
            foreach (var assetsFileName in _openAssetsFiles.Keys.ToList())
            {
                var assetsFile = _openAssetsFiles[assetsFileName];
                try
                {
                    _apk.WriteCombinedAssets(assetsFile, BSConst.KnownFiles.AssetsRootPath+assetsFileName);
                }
                catch (Exception ex)
                {
                    Log.LogErr($"Exception writing assets file {assetsFileName}", ex);
                    throw;
                }
                _openAssetsFiles.Remove(assetsFileName);
            }
        }

        public BeatSaberQuestomConfig GetCurrentConfig(bool suppressImages = false)
        {
            BeatSaberQuestomConfig config = new BeatSaberQuestomConfig();
            var file19 = OpenAssets(BSConst.KnownFiles.MainCollectionAssetsFilename);
            var file17 = OpenAssets(BSConst.KnownFiles.SongsAssetsFilename);
            var mainPack = GetMainLevelPack();
            foreach (var packPtr in mainPack.BeatmapLevelPacks)
            {
                if (file19.GetFilenameForFileID(packPtr.FileID) != BSConst.KnownFiles.SongsAssetsFilename)
                    throw new NotImplementedException("Songs and packs are only supported in one file currently.");
                var pack = file17.GetAssetByID<BeatmapLevelPackObject>(packPtr.PathID);
                if (pack == null)
                {
                    Log.LogErr($"Level pack with path ID {packPtr} was not found in {BSConst.KnownFiles.SongsAssetsFilename}!");
                    continue;
                }
                if (HideOriginalPlaylists && BSConst.KnownLevelPackIDs.Contains(pack.PackID))
                    continue;

                //TODO: cover art, ETC pack and all that
                var packModel = new BeatSaberPlaylist() { PlaylistName = pack.PackName, PlaylistID = pack.PackID, LevelPackObject = pack };
                //TODO: check file ref?  right now they're all in 17
                var collection = file17.GetAssetByID<BeatmapLevelCollectionObject>(pack.BeatmapLevelCollection.PathID);
                if (collection == null)
                {
                    Log.LogErr($"Failed to find level pack collection object for playlist {pack.PackName}");
                    continue;
                }
                packModel.LevelCollection = collection;

                foreach (var songPtr in collection.BeatmapLevels)
                {
                    var songObj = file17.GetAssetByID<BeatmapLevelDataObject>(songPtr.PathID);
                    if (songObj == null)
                    {
                        Log.LogErr($"Failed to find beatmap level data for playlist {pack.PackName} with path id {songPtr.PathID}!");
                        continue;
                    }
                    var songModel = new BeatSaberSong()
                    {
                        LevelAuthorName = songObj.LevelAuthorName,
                        SongID = songObj.LevelID,
                        SongAuthorName = songObj.SongAuthorName,
                        SongName = songObj.SongName,
                        SongSubName = songObj.SongSubName,
                        LevelData = songObj
                    };
                    try
                    {
                        var songCover = file17.GetAssetByID<Texture2DObject>(songObj.CoverImageTexture2D.PathID);
                        if (songCover == null)
                        {
                            Log.LogErr($"The cover image for song id '{songObj.LevelID}' could not be found!");
                        }
                        else
                        {
                            if (!suppressImages)
                                songModel.CoverArtBase64PNG = songCover.ToBase64PNG();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr($"Exception loading/converting the cover image for song id '{songObj.LevelID}'", ex);
                    }
                    packModel.SongList.Add(songModel);
                }
                config.Playlists.Add(packModel);
            }
            return config;
        }

        private void UpdatePlaylistConfig(BeatSaberPlaylist playlist)
        {
            Log.LogMsg("Starting update of config...");
            var songsAssetFile = OpenAssets(BSConst.KnownFiles.SongsAssetsFilename);
            BeatmapLevelPackObject levelPack = null;
            BeatmapLevelCollectionObject levelCollection = null;
            levelPack = songsAssetFile.FindAssets<BeatmapLevelPackObject>(x=>x.PackID == playlist.PlaylistID).FirstOrDefault();
            //create a new level pack if one waasn't found
            if (levelPack == null)
            {
                Log.LogMsg($"Level pack for playlist '{playlist.PlaylistID}' was not found and will be created");
                //don't try to find the cover name, just let it create a dupe, we'll try to clean up linked things we did later

                var packCover = CustomLevelLoader.LoadPackCover(playlist.PlaylistID, songsAssetFile, playlist.CoverArtFile);
                levelPack = new BeatmapLevelPackObject(songsAssetFile.Metadata)
                {
                    CoverImage = packCover ?? KnownObjects.File17.ExtrasCoverArt,
                    Enabled = 1,
                    GameObjectPtr = new PPtr(),
                    IsPackAlwaysOwned = true,
                    PackID = playlist.PlaylistID,
                    Name = playlist.PlaylistID + BSConst.NameSuffixes.LevelPack,
                    PackName = playlist.PlaylistName
                };
                songsAssetFile.AddObject(levelPack, true);
            }
            else
            {
                Log.LogMsg($"Level pack for playlist '{playlist.PlaylistID}' was found and will be updated");
                levelCollection = songsAssetFile.GetAssetByID<BeatmapLevelCollectionObject>(levelPack.BeatmapLevelCollection.PathID);
                if (levelCollection == null)
                {
                    Log.LogErr($"{nameof(BeatmapLevelCollectionObject)} was not found for playlist id {playlist.PlaylistID}!  It will be created, but something is wrong with the assets!");
                }
            }
            if (levelCollection == null)
            {
                levelCollection = new BeatmapLevelCollectionObject(songsAssetFile.Metadata)
                { Name = playlist.PlaylistID + BSConst.NameSuffixes.LevelCollection };
                songsAssetFile.AddObject(levelCollection, true);
                levelPack.BeatmapLevelCollection = levelCollection.ObjectInfo.LocalPtrTo;
            }

            playlist.LevelCollection = levelCollection;
            playlist.LevelPackObject = levelPack;

            levelPack.PackName = playlist.PlaylistName;
            if (!string.IsNullOrEmpty(playlist.CoverArtFile))
            {
                Log.LogErr($"Playlist '{playlist.PlaylistID}' specified a cover art file, but it isn't supported yet!");
                //////todo:  delete, old cover art asset, add new one
                //var packCover = songsAssetFile.GetAssetByID<SpriteObject>
                //packCover = CustomLevelLoader.LoadPackCover(playlist.PlaylistID+BSConst.NameSuffixes.PackCover, songsAssetFile, playlist.CoverArtFile);
            }

            //clear out any levels, we'll add them back
            levelCollection.BeatmapLevels.Clear();

            foreach (var song in playlist.SongList.ToList())
            {
                if (UpdateSongConfig(song))
                {
                    if (playlist.LevelCollection.BeatmapLevels.Any(x => x.PathID == song.LevelData.ObjectInfo.ObjectID))
                    {
                        Log.LogErr($"Playlist ID '{playlist.PlaylistID}' already contains song ID '{song.SongID}' once, removing the second link");
                    }

                    playlist.LevelCollection.BeatmapLevels.Add(song.LevelData.ObjectInfo.LocalPtrTo);
                    continue;
                }
                
                playlist.SongList.Remove(song);
            }
        }

        private bool UpdateSongConfig(BeatSaberSong song)
        {
            var songsAssetFile = OpenAssets(BSConst.KnownFiles.SongsAssetsFilename);
            BeatmapLevelDataObject level = null;
            if (!string.IsNullOrWhiteSpace(song.SongID))
            {
                var levels = songsAssetFile.FindAssets<BeatmapLevelDataObject>(x => x.LevelID == song.SongID);
                if (levels.Count > 0)
                {
                    if (levels.Count > 1)
                        Log.LogErr($"Song ID {song.SongID} already has more than one entry in the assets, this may cause problems!");
                    else
                        Log.LogMsg($"Song ID {song.SongID} exists already and won't be loaded");
                    level = levels.First();
                    song.LevelData = level;
                }
                else
                {
                    Log.LogMsg($"Song ID '{song.SongID}' does not exist and will be created");
                }
            }
            if (level != null && !string.IsNullOrWhiteSpace(song.CustomSongFolder))
            {
                Log.LogErr("SongID and CustomSongsFolder are both set and the level already exists.  The existing one will be used and CustomSongsFolder won'tbe imported again.");
                return false;
            }

            //load new song
            if (!string.IsNullOrWhiteSpace(song.CustomSongFolder))
            {
                try
                {
                    string oggPath;
                    var deser = CustomLevelLoader.DeserializeFromJson(songsAssetFile, song.CustomSongFolder, song.SongID);
                    var found = songsAssetFile.FindAssets<BeatmapLevelDataObject>(x => x.LevelID == deser.LevelID).FirstOrDefault();
                    if (found != null)
                    {
                        Log.LogErr($"No song id was specified, but the level {found.LevelID} is already in the assets, skipping it.");
                        song.LevelData = found;
                        return true;
                    }
                    level = CustomLevelLoader.LoadSongToAsset(deser, song.CustomSongFolder, songsAssetFile, out oggPath, true);
                    song.SourceOgg = oggPath;
                }
                catch (Exception ex)
                {
                    Log.LogErr($"Exception loading custom song folder '{song.CustomSongFolder}', skipping it", ex);
                    return false;
                }

                if (level == null)
                {
                    Log.LogErr($"Song at folder '{song.CustomSongFolder}' failed to load, skipping it");
                    return false;
                }

                song.LevelData = level;
                return true;
            }
            //level == null && string.IsNullOrWhiteSpace(song.CustomSongFolder)
            
            Log.LogErr($"Song ID '{song.SongID}' either was not specified or could not be found and no CustomSongFolder was specified, skipping it.");
            return false;
            
        }

        private void RemoveLevelAssets(BeatmapLevelDataObject level, List<string> audioFilesToDelete)
        {
            Log.LogMsg($"Removing assets for song id '{level.LevelID}'");
            var file17 = OpenAssets(BSConst.KnownFiles.SongsAssetsFilename);
            var cover = file17.GetAssetByID<Texture2DObject>(level.CoverImageTexture2D.PathID);
            if (cover == null)
            {
                Log.LogErr($"Could not find cover for song id '{level.LevelID}' to remove it");
            }
            else
            {
                file17.DeleteObject(cover);
            }
            foreach (var diff in level.DifficultyBeatmapSets)
            {
                foreach (var diffbm in diff.DifficultyBeatmaps)
                {                    
                    file17.DeleteObject(file17.GetAssetByID<AssetsObject>(diffbm.BeatmapDataPtr.PathID));
                }
            }
            var audioClip = file17.GetAssetByID<AudioClipObject>(level.AudioClip.PathID);
            if (audioClip == null)
            {
                Log.LogErr($"Could not find audio clip asset for song id '{level.LevelID}' to remove it");
            }
            else
            {
                audioFilesToDelete.Add(audioClip.Resource.Source);
                file17.DeleteObject(audioClip);
            }
            file17.DeleteObject(level);
        }

        private void RemoveLevelPackAssets(BeatmapLevelPackObject levelPack)
        {
            Log.LogMsg($"Removing assets for playlist ID '{ levelPack.PackID}'");
            var file17 = OpenAssets(BSConst.KnownFiles.SongsAssetsFilename);

            //TODO: remove cover images once implemented

            var collection = file17.GetAssetByID<BeatmapLevelCollectionObject>(levelPack.BeatmapLevelCollection.PathID);
            file17.DeleteObject(collection);
            file17.DeleteObject(levelPack);                       
        }



        public void UpdateConfig(BeatSaberQuestomConfig config)
        {
            //todo: basic validation of the config
            if (_readOnly)
                throw new InvalidOperationException("Cannot update in read only mode.");
            UpdateKnownObjects();

            //get the old config before we start on this
            var originalConfig = GetCurrentConfig();

            //get existing playlists and their songs
            //compare with new ones
            //generate a diff
            //etc.
            var songsAssetFile = OpenAssets(BSConst.KnownFiles.SongsAssetsFilename);

            foreach (var playlist in config.Playlists)
            {
                UpdatePlaylistConfig(playlist);            
            }


            //open the assets with the main levels collection, find the file index of sharedassets17.assets, and add the playlists to it
            var mainLevelsFile = OpenAssets(BSConst.KnownFiles.MainCollectionAssetsFilename);
            var file17Index = mainLevelsFile.GetFileIDForFilename(BSConst.KnownFiles.SongsAssetsFilename);
            var mainLevelPack = mainLevelsFile.FindAsset<MainLevelPackCollectionObject>();



            List<BeatmapLevelPackObject> packsToRemove = new List<BeatmapLevelPackObject>();
            List<PPtr> levelPackPointersToUnlink = new List<PPtr>();
            //List<BeatmapLevelCollectionObject> collectionsToRemove = new List<BeatmapLevelCollectionObject>();
            foreach (var packPtr in mainLevelPack.BeatmapLevelPacks)
            {
                if (packPtr.FileID != file17Index)
                {
                    Log.LogMsg("One of the beatmap level packs is in another file, not removing it");
                    continue;
                }
                var pack = songsAssetFile.GetAssetByID<BeatmapLevelPackObject>(packPtr.PathID);
                //not sure if I should remove it or leave it here... if one ends up in another asset file it'll break
                if (pack == null)
                {
                    Log.LogErr("Unable to locate one of the beatmap level packs referenced in the main collection, removing the link");
                    levelPackPointersToUnlink.Add(packPtr);
                    continue;
                }
                
                if (pack.BeatmapLevelCollection.FileID != 0)
                {
                    Log.LogMsg("One of the beatmap level pack collections is in another file, not removing it");
                    continue;
                }
                var packCollection = songsAssetFile.GetAssetByID<BeatmapLevelCollectionObject>(pack.BeatmapLevelCollection.PathID);
                if (packCollection == null)
                {
                    Log.LogErr($"Unable to locate the level collection for '{pack.PackID}', removing the link");
                    levelPackPointersToUnlink.Add(packPtr);
                    continue;
                }
                if (config.Playlists.Any(x => x.LevelPackObject.ObjectInfo.ObjectID == pack.ObjectInfo.ObjectID))
                {
                    //unlink it so we can relink it in order
                    levelPackPointersToUnlink.Add(packPtr);
                    continue;
                }
                if (BSConst.KnownLevelPackIDs.Contains(pack.PackID))
                {
                    if (!HideOriginalPlaylists)
                        levelPackPointersToUnlink.Add(packPtr);
                    continue;
                }

                levelPackPointersToUnlink.Add(packPtr);
                packsToRemove.Add(pack);
            }
  
            var oldSongs = originalConfig.Playlists.SelectMany(x => x.SongList).Select(x => x.LevelData).Distinct();
            var newSongs = config.Playlists.SelectMany(x => x.SongList).Select(x => x.LevelData).Distinct();
                        
            //don't allow removal of the actual tracks or level packs that are built in, although you can unlink them from the main list
            var removeSongs = oldSongs.Where(x => !newSongs.Contains(x) && !BSConst.KnownLevelIDs.Contains(x.LevelID)).Distinct().ToList();

            List<string> audioFilesToDelete = new List<string>();
            removeSongs.ForEach(x => RemoveLevelAssets(x, audioFilesToDelete));
            packsToRemove.ForEach(x => RemoveLevelPackAssets(x));

            levelPackPointersToUnlink.ForEach(x => mainLevelPack.BeatmapLevelPacks.Remove(x));

            //relink all the level packs in order
            mainLevelPack.BeatmapLevelPacks.AddRange(config.Playlists.Select(x => new PPtr(file17Index, x.LevelPackObject.ObjectInfo.ObjectID)));

            //do a first loop to guess at the file size
            Int64 sizeGuess = new FileInfo(_apkFilename).Length;
            foreach (var pl in config.Playlists)
            {
                foreach (var sng in pl.SongList)
                {
                    if (sng.SourceOgg != null)
                    {
                        var clip = songsAssetFile.GetAssetByID<AudioClipObject>(sng.LevelData.AudioClip.PathID);
                        sizeGuess += new FileInfo(sng.SourceOgg).Length;
                    }
                }
            }
            foreach (var toDelete in audioFilesToDelete)
            {
                sizeGuess -= _apk.GetFileSize(BSConst.KnownFiles.AssetsRootPath + toDelete);
            }

            if (sizeGuess > Int32.MaxValue)
            {
                Log.LogErr("***************ERROR*****************");
                Log.LogErr($"Guesstimating a file size around {sizeGuess / (Int64)1000000}MB , this will crash immediately upon launch.");
                Log.LogErr($"The file size MUST be less than {Int32.MaxValue / (int)1000000}MB");
                Log.LogErr("***************ERROR*****************");
                Log.LogErr($"Proceeding anyways, but you've been warned");
            }

            ////////START WRITING DATA
            foreach (var pl in config.Playlists)
            {
                foreach (var sng in pl.SongList)
                {
                    if (sng.SourceOgg != null)
                    {
                        var clip = songsAssetFile.GetAssetByID<AudioClipObject>(sng.LevelData.AudioClip.PathID);
                        _apk.Write(sng.SourceOgg, BSConst.KnownFiles.AssetsRootPath+clip.Resource.Source, true, false);
                        //saftey check to make sure we aren't removing a file we just put here
                        if (audioFilesToDelete.Contains(clip.Resource.Source))
                        {
                            Log.LogErr($"Level id '{sng.LevelData.LevelID}' wrote file '{clip.Resource.Source}' that was on the delete list...");
                            audioFilesToDelete.Remove(clip.Resource.Source);
                        }
                    }
                }
            }

            foreach (var toDelete in audioFilesToDelete)
            {
                Log.LogMsg($"Deleting audio file {toDelete}");
                _apk.Delete(BSConst.KnownFiles.AssetsRootPath + toDelete);
            }

            WriteAllOpenAssets();
        }



        private MainLevelPackCollectionObject GetMainLevelPack()
        {
            var file19 = OpenAssets(BSConst.KnownFiles.MainCollectionAssetsFilename);
            var mainLevelPack = file19.FindAsset<MainLevelPackCollectionObject>();
            if (mainLevelPack == null)
                throw new Exception("Unable to find the main level pack collection object!");
            return mainLevelPack;
        }

        public bool ApplyBeatmapSignaturePatch()
        {
            return Utils.Patcher.PatchBeatmapSigCheck(_apk);
        }

        //this is crap, I need to load all files and resolve file pointers properly
        private void UpdateKnownObjects()
        {
            var songsFile = OpenAssets(BeatSaber.BSConst.KnownFiles.SongsAssetsFilename);
            if (!songsFile.Metadata.ExternalFiles.Any(x => x.FileName == BSConst.KnownFiles.File19))
            {
                songsFile.Metadata.ExternalFiles.Add(new ExternalFile()
                {
                    FileName = BSConst.KnownFiles.File19,
                    AssetName = "",
                    ID = Guid.Empty,
                    Type = 0
                });
            }
            songsFile = OpenAssets(BeatSaber.BSConst.KnownFiles.SongsAssetsFilename);
            if (!songsFile.Metadata.ExternalFiles.Any(x => x.FileName == BSConst.KnownFiles.File14))
            {
                songsFile.Metadata.ExternalFiles.Add(new ExternalFile()
                {
                    FileName = BSConst.KnownFiles.File19,
                    AssetName = "",
                    ID = Guid.Empty,
                    Type = 0
                });
            }
            int file19 = songsFile.GetFileIDForFilename(BSConst.KnownFiles.File19);
            int file14 = songsFile.GetFileIDForFilename(BSConst.KnownFiles.File14);
            KnownObjects.File17.MonstercatEnvironment = new PPtr(file19, KnownObjects.File17.MonstercatEnvironment.PathID);
            KnownObjects.File17.NiceEnvironment = new PPtr(file14, KnownObjects.File17.NiceEnvironment.PathID);
        }

        #region Helper Functions

        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                    if (_apk != null)
                        _apk.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion

    }
}