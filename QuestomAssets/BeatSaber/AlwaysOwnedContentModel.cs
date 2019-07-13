﻿using QuestomAssets.AssetsChanger;
using System;
using System.Collections.Generic;
using System.Text;

namespace QuestomAssets.BeatSaber
{
    public sealed class AlwaysOwnedContentModel : MonoBehaviourObject, INeedAssetsMetadata
    {

        public AlwaysOwnedContentModel(AssetsFile assetsFile) : base(assetsFile, assetsFile.Manager.GetScriptObject("AlwaysOwnedContentModelSO"))
        { }

        public AlwaysOwnedContentModel(IObjectInfo<AssetsObject> objectInfo, AssetsReader reader) : base(objectInfo)
        {
            Parse(reader);
        }

        public override void Parse(AssetsReader reader)
        {
            base.ParseBase(reader);
            AlwaysOwnedPacks = reader.ReadArrayOf<ISmartPtr<BeatmapLevelPackObject>>(x => SmartPtr<BeatmapLevelPackObject>.Read(ObjectInfo.ParentFile, this, reader));
            AlwaysOwnedBeatmapLevels = reader.ReadArrayOf<ISmartPtr<BeatmapLevelDataObject>>(x => SmartPtr<BeatmapLevelDataObject>.Read(ObjectInfo.ParentFile, this, reader));
        }

        protected override void WriteObject(AssetsWriter writer)
        {
            base.WriteBase(writer);
            writer.WriteArrayOf(AlwaysOwnedPacks, (x, y) => x.Write(y));
            writer.WriteArrayOf(AlwaysOwnedBeatmapLevels, (x, y) => x.Write(y));
        }

        public List<ISmartPtr<BeatmapLevelPackObject>> AlwaysOwnedPacks { get; set; } = new List<ISmartPtr<BeatmapLevelPackObject>>();

        public List<ISmartPtr<BeatmapLevelDataObject>> AlwaysOwnedBeatmapLevels { get; set; } = new List<ISmartPtr<BeatmapLevelDataObject>>();

        [System.ComponentModel.Browsable(false)]
        [Newtonsoft.Json.JsonIgnore]
        public override byte[] ScriptParametersData
        {
            get
            {
                throw new InvalidOperationException("Cannot access parameters data from this object.");
            }
            set
            {
                throw new InvalidOperationException("Cannot access parameters data from this object.");
            }
        }
    }
}
