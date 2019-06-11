﻿using Newtonsoft.Json;
using QuestomAssets.AssetsChanger;
using System;
using System.Collections.Generic;
using System.Text;

namespace QuestomAssets.BeatSaber
{
    public sealed class SimpleColorSO : MonoBehaviourObject
    {
        [JsonProperty("_color")]
        public Color color;

        public SimpleColorSO(IObjectInfo<AssetsObject> objectInfo, AssetsReader reader) : base(objectInfo, reader)
        {
            Parse(reader);
        }

        public SimpleColorSO(IObjectInfo<AssetsObject> objectInfo) : base(objectInfo)
        { }

        public SimpleColorSO(AssetsFile assetsFile) : base(assetsFile, assetsFile.Manager.GetScriptObject("SimpleColorSO"))
        { }

        protected override void Parse(AssetsReader reader)
        {
            base.Parse(reader);
            color = new Color(reader);
        }

        public override void Write(AssetsWriter writer)
        {
            WriteBase(writer);
            color.Write(writer);
        }
    }
}