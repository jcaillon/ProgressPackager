#region header
// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (ZipFileInfo.cs) is part of csdeployer.
// 
// csdeployer is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// csdeployer is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with csdeployer. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace csdeployer.Lib.Compression.Zip {
    /// <summary>
    /// Object representing a compressed file within a zip package; provides operations for getting
    /// the file properties and extracting the file.
    /// </summary>
    [Serializable]
    public class ZipFileInfo : ArchiveFileInfo {
        private long compressedLength;
        private ZipCompressionMethod compressionMethod;

        /// <summary>
        /// Creates a new ZipFileInfo object representing a file within a zip in a specified path.
        /// </summary>
        /// <param name="zipInfo">An object representing the zip archive containing the file.</param>
        /// <param name="filePath">The path to the file within the zip archive. Usually, this is a simple file
        /// name, but if the zip archive contains a directory structure this may include the directory.</param>
        public ZipFileInfo(ZipInfo zipInfo, string filePath)
            : base(zipInfo, filePath) {
            if (zipInfo == null) {
                throw new ArgumentNullException("zipInfo");
            }
        }

        /// <summary>
        /// Creates a new ZipFileInfo object with all parameters specified,
        /// used internally when reading the metadata out of a zip archive.
        /// </summary>
        /// <param name="filePath">The internal path and name of the file in the zip archive.</param>
        /// <param name="zipNumber">The zip archive number where the file starts.</param>
        /// <param name="attributes">The stored attributes of the file.</param>
        /// <param name="lastWriteTime">The stored last write time of the file.</param>
        /// <param name="length">The uncompressed size of the file.</param>
        /// <param name="compressedLength">The compressed size of the file.</param>
        /// <param name="compressionMethod">Compression algorithm used for this file.</param>
        internal ZipFileInfo(
            string filePath,
            int zipNumber,
            FileAttributes attributes,
            DateTime lastWriteTime,
            long length,
            long compressedLength,
            ZipCompressionMethod compressionMethod)
            : base(filePath, zipNumber, attributes, lastWriteTime, length) {
            this.compressedLength = compressedLength;
            this.compressionMethod = compressionMethod;
        }

        /// <summary>
        /// Initializes a new instance of the ZipFileInfo class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected ZipFileInfo(SerializationInfo info, StreamingContext context)
            : base(info, context) {
            compressedLength = info.GetInt64("compressedLength");
        }

        /// <summary>
        /// Gets the compressed size of the file in bytes.
        /// </summary>
        public long CompressedLength {
            get { return compressedLength; }
        }

        /// <summary>
        /// Gets the method used to compress this file.
        /// </summary>
        public ZipCompressionMethod CompressionMethod {
            get { return compressionMethod; }
        }

        /// <summary>
        /// Sets the SerializationInfo with information about the archive.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data.</param>
        /// <param name="context">The StreamingContext that contains contextual information
        /// about the source or destination.</param>
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context) {
            base.GetObjectData(info, context);
            info.AddValue("compressedLength", compressedLength);
        }
    }
}