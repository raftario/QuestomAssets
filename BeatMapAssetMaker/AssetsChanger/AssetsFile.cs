﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace BeatmapAssetMaker.AssetsChanger
{
    public class AssetsFile
    {

        public AssetsHeader Header { get; set; }

        public AssetsMetadata Metadata { get; set; }

        public List<AssetsObject> Objects { get; set; }
        public AssetsFile(string fileName, Dictionary<Guid, Type> scriptHashToTypes)
        {
            Objects = new List<AssetsObject>();

            using (MemoryStream fullFileStream = new MemoryStream())
            {

                List<string> assetFiles = new List<string>();
                if (fileName.ToLower().EndsWith("split0"))
                {
                    assetFiles.AddRange(Directory.EnumerateFiles(Path.GetDirectoryName(fileName), Path.GetFileNameWithoutExtension(fileName) + ".split*").OrderBy(x => Convert.ToInt32(x.Split(new string[] { ".split" }, StringSplitOptions.None).Last())));
                }
                else
                {
                    assetFiles.Add(fileName);
                }
                foreach (string assetFile in assetFiles)
                {
                    using (Stream fileStream = File.OpenRead(assetFile))
                    {
                        fileStream.CopyTo(fullFileStream);
                    }
                }
                fullFileStream.Seek(0, SeekOrigin.Begin);


                using (AssetsReader reader = new AssetsReader(fullFileStream))
                {
                    Header = new AssetsHeader(reader);
       

                    Metadata = new AssetsMetadata(reader);

                    //I think align to 16?  this is a guess based on how they're written
                    reader.AlignTo(16);
                    if (reader.BaseStream.Position != Header.ObjectDataOffset)
                        throw new Exception("Object data isn't where the header says it should be!");
           
                    foreach (var objectInfo in Metadata.ObjectInfos)
                    {
                        AssetsObject assetsObject;
                        var objectType = Metadata.Types[objectInfo.TypeIndex];
                        switch (objectType.ClassID)
                        {
                            case AssetsConstants.MonoBehaviourScriptType:
                                if (scriptHashToTypes.ContainsKey(objectType.ScriptHash))
                                {
                                    Type assetObjectType = scriptHashToTypes[objectType.ScriptHash];
                                    if (!assetObjectType.IsSubclassOf(typeof(AssetsMonoBehaviourObject)))
                                    {
                                        throw new ArgumentException("Types provided in scriptHashToTypes must be a subclass of AssetsMonoBehaviourObject.");
                                    }
                                    assetsObject = (AssetsObject)Activator.CreateInstance(assetObjectType, objectInfo, reader);
                                }
                                else
                                {
                                    assetsObject = new AssetsMonoBehaviourObject(objectInfo, reader);
                                }
                                break;
                            case AssetsConstants.AudioClipType:
                                assetsObject = new AssetsAudioClip(objectInfo, reader);
                                break;
                            default:
                                assetsObject = new AssetsObject(objectInfo, reader);
                                break;
                        }
                        reader.AlignToObjectData(8);
                        Objects.Add(assetsObject);
                    }
                }
            }
        }

        public void Write(string fileName)
        {
            MemoryStream objectsMS = new MemoryStream();
            MemoryStream metaMS = new MemoryStream();
            using (AssetsWriter writer = new AssetsWriter(objectsMS))
            {
                foreach (var obj in Objects)
                {
                    
                    obj.ObjectInfo.DataOffset = (int)objectsMS.Position;
                    obj.Write(writer);
                    writer.Flush();
                    obj.ObjectInfo.DataSize = (int)(objectsMS.Position - obj.ObjectInfo.DataOffset);
                    writer.AlignTo(8);
                }
            }
            using (AssetsWriter writer = new AssetsWriter(metaMS))
            {
                Metadata.Write(writer);
                
                writer.AlignTo(4);
                //I don't know why
                writer.Write((int)0);
            }
            Header.FileSize = Header.HeaderSize + (int)objectsMS.Length + (int)metaMS.Length;
            Header.ObjectDataOffset = Header.HeaderSize + (int)metaMS.Length;
            Header.MetadataSize = (int)metaMS.Length;
            objectsMS.Seek(0, SeekOrigin.Begin);
            metaMS.Seek(0, SeekOrigin.Begin);
            using (FileStream fs = File.Open(fileName, FileMode.Create))
            {
                using (AssetsWriter writer = new AssetsWriter(fs))
                {
                    Header.Write(writer);
                }
                metaMS.CopyTo(fs);

                //not sure if this should align to 4, 8, 16, 32, etc.
                //int alignto = 24;
                //int count = alignto - ((int)fs.Position % alignto);
                //if (count > 0 && count < alignto)
                //    fs.Write(new byte[count], 0, count);

                objectsMS.CopyTo(fs);
            }

        }

        public long GetNextObjectID()
        {
            return Metadata.ObjectInfos.Max(x => x.ObjectID) + 1;
        }

        public void AddObject(AssetsObject assetsObject, bool assignNextObjectID = false)
        {
            if (assetsObject.ObjectInfo == null)
                throw new ArgumentException("ObjectInfo must be set!");

            if (assignNextObjectID)
            {
                assetsObject.ObjectInfo.ObjectID = GetNextObjectID();
            }
            if (assetsObject.ObjectInfo.ObjectID < 1)
                throw new ArgumentException("ObjectInfo.ObjectID must be > 0.");
            if (Metadata.ObjectInfos.Exists(x => x.ObjectID == assetsObject.ObjectInfo.ObjectID))
                throw new ArgumentException("ObjectInfo.ObjectID already exists in this file.");

            Metadata.ObjectInfos.Add(assetsObject.ObjectInfo);
            Objects.Add(assetsObject);
        }
    }
}
