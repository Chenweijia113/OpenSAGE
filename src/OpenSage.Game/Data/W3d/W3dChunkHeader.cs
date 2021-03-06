﻿using System.IO;

namespace OpenSage.Data.W3d
{
    public class W3dChunkHeader
    {
        public const int SizeInBytes = sizeof(W3dChunkType) + sizeof(uint);

        public W3dChunkType ChunkType { get; private set; }

        /// <summary>
        /// Size of the chunk, (not including the chunk header)
        /// </summary>
        public uint ChunkSize { get; private set; }

        public bool HasSubChunks { get; private set; }

        public static W3dChunkHeader Parse(BinaryReader reader)
        {
            var result = new W3dChunkHeader
            {
                ChunkType = (W3dChunkType)reader.ReadUInt32(),
            };

            var chunkSize = reader.ReadUInt32();
            result.ChunkSize = chunkSize & 0x7FFFFFFF;
            result.HasSubChunks = (chunkSize >> 31) == 1;

            return result;
        }
    }
}
