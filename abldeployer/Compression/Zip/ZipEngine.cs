#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (ZipEngine.cs) is part of csdeployer.
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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;

namespace abldeployer.Compression.Zip {
    /// <summary>
    ///     Engine capable of packing and unpacking archives in the zip format.
    /// </summary>
    public partial class ZipEngine : CompressionEngine {
        private static Dictionary<ZipCompressionMethod, Converter<Stream, Stream>>
            compressionStreamCreators;

        private static Dictionary<ZipCompressionMethod, Converter<Stream, Stream>>
            decompressionStreamCreators;

        private long currentArchiveBytesProcessed;
        private string currentArchiveName;
        private short currentArchiveNumber;
        private long currentArchiveTotalBytes;
        private long currentFileBytesProcessed;

        // Progress data
        private string currentFileName;

        private int currentFileNumber;
        private long currentFileTotalBytes;
        private long fileBytesProcessed;
        private string mainArchiveName;
        private short totalArchives;
        private long totalFileBytes;
        private int totalFiles;

        /// <summary>
        ///     Gets the comment from the last-examined archive,
        ///     or sets the comment to be added to any created archives.
        /// </summary>
        public string ArchiveComment { get; set; }

        /// <summary>
        ///     Creates a new instance of the zip engine.
        /// </summary>
        public ZipEngine() {
            InitCompressionStreamCreators();
        }

        private static void InitCompressionStreamCreators() {
            if (compressionStreamCreators == null) {
                compressionStreamCreators = new
                    Dictionary<ZipCompressionMethod, Converter<Stream, Stream>>();
                decompressionStreamCreators = new
                    Dictionary<ZipCompressionMethod, Converter<Stream, Stream>>();

                RegisterCompressionStreamCreator(
                    ZipCompressionMethod.Store,
                    CompressionMode.Compress,
                    delegate(Stream stream) { return stream; });
                RegisterCompressionStreamCreator(
                    ZipCompressionMethod.Deflate,
                    CompressionMode.Compress,
                    delegate(Stream stream) { return new DeflateStream(stream, CompressionMode.Compress, true); });
                RegisterCompressionStreamCreator(
                    ZipCompressionMethod.Store,
                    CompressionMode.Decompress,
                    delegate(Stream stream) { return stream; });
                RegisterCompressionStreamCreator(
                    ZipCompressionMethod.Deflate,
                    CompressionMode.Decompress,
                    delegate(Stream stream) { return new DeflateStream(stream, CompressionMode.Decompress, true); });
            }
        }

        /// <summary>
        ///     Registers a delegate that can create a warpper stream for
        ///     compressing or uncompressing the data of a source stream.
        /// </summary>
        /// <param name="compressionMethod">Compression method being registered.</param>
        /// <param name="compressionMode">
        ///     Indicates registration for ether
        ///     compress or decompress mode.
        /// </param>
        /// <param name="creator">Delegate being registered.</param>
        /// <remarks>
        ///     For compression, the delegate accepts a stream that writes to the archive
        ///     and returns a wrapper stream that compresses bytes as they are written.
        ///     For decompression, the delegate accepts a stream that reads from the archive
        ///     and returns a wrapper stream that decompresses bytes as they are read.
        ///     This wrapper stream model follows the design used by
        ///     System.IO.Compression.DeflateStream, and indeed that class is used
        ///     to implement the Deflate compression method by default.
        ///     <para>
        ///         To unregister a delegate, call this method again and pass
        ///         null for the delegate parameter.
        ///     </para>
        /// </remarks>
        /// <example>
        ///     When the ZipEngine class is initialized, the Deflate compression method
        ///     is automatically registered like this:
        ///     <code>
        ///        ZipEngine.RegisterCompressionStreamCreator(
        ///            ZipCompressionMethod.Deflate,
        ///            CompressionMode.Compress,
        ///            delegate(Stream stream) {
        ///                return new DeflateStream(stream, CompressionMode.Compress, true);
        ///            });
        ///        ZipEngine.RegisterCompressionStreamCreator(
        ///            ZipCompressionMethod.Deflate,
        ///            CompressionMode.Decompress,
        ///            delegate(Stream stream) {
        ///                return new DeflateStream(stream, CompressionMode.Decompress, true);
        ///            });
        /// </code>
        /// </example>
        public static void RegisterCompressionStreamCreator(
            ZipCompressionMethod compressionMethod,
            CompressionMode compressionMode,
            Converter<Stream, Stream> creator) {
            InitCompressionStreamCreators();
            if (compressionMode == CompressionMode.Compress) compressionStreamCreators[compressionMethod] = creator;
            else decompressionStreamCreators[compressionMethod] = creator;
        }

        /// <summary>
        ///     Checks whether a Stream begins with a header that indicates
        ///     it is a valid archive file.
        /// </summary>
        /// <param name="stream">Stream for reading the archive file.</param>
        /// <returns>
        ///     True if the stream is a valid zip archive
        ///     (with no offset); false otherwise.
        /// </returns>
        public override bool IsArchive(Stream stream) {
            if (stream == null) throw new ArgumentNullException("stream");

            if (stream.Length - stream.Position < 4) return false;

            var reader = new BinaryReader(stream);
            var sig = reader.ReadUInt32();
            switch (sig) {
                case ZipFileHeader.LFHSIG:
                case ZipEndOfCentralDirectory.EOCDSIG:
                case ZipEndOfCentralDirectory.EOCD64SIG:
                case ZipFileHeader.SPANSIG:
                case ZipFileHeader.SPANSIG2:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        ///     Gets the offset of an archive that is positioned 0 or more bytes
        ///     from the start of the Stream.
        /// </summary>
        /// <param name="stream">A stream for reading the archive.</param>
        /// <returns>
        ///     The offset in bytes of the archive,
        ///     or -1 if no archive is found in the Stream.
        /// </returns>
        /// <remarks>The archive must begin on a 4-byte boundary.</remarks>
        public override long FindArchiveOffset(Stream stream) {
            var offset = base.FindArchiveOffset(stream);
            if (offset > 0) {
                // Some self-extract packages include the exe stub in file offset calculations.
                // Check the first header directory offset to decide whether the entire
                // archive needs to be offset or not.

                var eocd = GetEOCD(null, stream);
                if (eocd != null && eocd.totalEntries > 0) {
                    stream.Seek(eocd.dirOffset, SeekOrigin.Begin);

                    var header = new ZipFileHeader();
                    if (header.Read(stream, true) && header.localHeaderOffset < stream.Length) {
                        stream.Seek(header.localHeaderOffset, SeekOrigin.Begin);
                        if (header.Read(stream, false)) return 0;
                    }
                }
            }

            return offset;
        }

        /// <summary>
        ///     Gets information about files in a zip archive or archive chain.
        /// </summary>
        /// <param name="streamContext">
        ///     A context interface to handle opening
        ///     and closing of archive and file streams.
        /// </param>
        /// <param name="fileFilter">
        ///     A predicate that can determine
        ///     which files to process, optional.
        /// </param>
        /// <returns>Information about files in the archive stream.</returns>
        /// <exception cref="ArchiveException">
        ///     The archive provided
        ///     by the stream context is not valid.
        /// </exception>
        /// <remarks>
        ///     The <paramref name="fileFilter" /> predicate takes an internal file
        ///     path and returns true to include the file or false to exclude it.
        /// </remarks>
        public override IList<ArchiveFileInfo> GetFileInfo(
            IUnpackStreamContext streamContext,
            Predicate<string> fileFilter) {
            if (streamContext == null) throw new ArgumentNullException("streamContext");

            lock (this) {
                var headers = GetCentralDirectory(streamContext);
                if (headers == null) throw new ZipException("Zip central directory not found.");

                var files = new List<ArchiveFileInfo>(headers.Count);
                foreach (var header in headers)
                    if (!header.IsDirectory &&
                        (fileFilter == null || fileFilter(header.fileName))) files.Add(header.ToZipFileInfo());

                return files.AsReadOnly();
            }
        }

        /// <summary>
        ///     Reads all the file headers from the central directory in the main archive.
        /// </summary>
        private IList<ZipFileHeader> GetCentralDirectory(IUnpackStreamContext streamContext) {
            Stream archiveStream = null;
            currentArchiveNumber = 0;
            try {
                var headers = new List<ZipFileHeader>();
                archiveStream = OpenArchive(streamContext, 0);

                var eocd = GetEOCD(streamContext, archiveStream);
                if (eocd == null) return null;
                else if (eocd.totalEntries == 0) return headers;

                headers.Capacity = (int) eocd.totalEntries;

                if (eocd.dirOffset > archiveStream.Length - ZipFileHeader.CFH_FIXEDSIZE) {
                    streamContext.CloseArchiveReadStream(
                        currentArchiveNumber, string.Empty, archiveStream);
                    archiveStream = null;
                } else {
                    archiveStream.Seek(eocd.dirOffset, SeekOrigin.Begin);
                    var sig = new BinaryReader(archiveStream).ReadUInt32();
                    if (sig != ZipFileHeader.CFHSIG) {
                        streamContext.CloseArchiveReadStream(
                            currentArchiveNumber, string.Empty, archiveStream);
                        archiveStream = null;
                    }
                }

                if (archiveStream == null) {
                    currentArchiveNumber = (short) (eocd.dirStartDiskNumber + 1);
                    archiveStream = streamContext.OpenArchiveReadStream(
                        currentArchiveNumber, string.Empty, this);

                    if (archiveStream == null) return null;
                }

                archiveStream.Seek(eocd.dirOffset, SeekOrigin.Begin);

                while (headers.Count < eocd.totalEntries) {
                    var header = new ZipFileHeader();
                    if (!header.Read(archiveStream, true))
                        throw new ZipException(
                            "Missing or invalid central directory file header");

                    headers.Add(header);

                    if (headers.Count < eocd.totalEntries &&
                        archiveStream.Position == archiveStream.Length) {
                        streamContext.CloseArchiveReadStream(
                            currentArchiveNumber, string.Empty, archiveStream);
                        currentArchiveNumber++;
                        archiveStream = streamContext.OpenArchiveReadStream(
                            currentArchiveNumber, string.Empty, this);
                        if (archiveStream == null) {
                            currentArchiveNumber = 0;
                            archiveStream = streamContext.OpenArchiveReadStream(
                                currentArchiveNumber, string.Empty, this);
                        }
                    }
                }

                return headers;
            } finally {
                if (archiveStream != null)
                    streamContext.CloseArchiveReadStream(
                        currentArchiveNumber, string.Empty, archiveStream);
            }
        }

        /// <summary>
        ///     Locates and reads the end of central directory record near the
        ///     end of the archive.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "streamContext")]
        private ZipEndOfCentralDirectory GetEOCD(
            IUnpackStreamContext streamContext, Stream archiveStream) {
            var reader = new BinaryReader(archiveStream);
            var offset = archiveStream.Length
                         - ZipEndOfCentralDirectory.EOCD_RECORD_FIXEDSIZE;
            while (offset >= 0) {
                archiveStream.Seek(offset, SeekOrigin.Begin);

                var sig = reader.ReadUInt32();
                if (sig == ZipEndOfCentralDirectory.EOCDSIG) break;

                offset--;
            }

            if (offset < 0) return null;

            var eocd = new ZipEndOfCentralDirectory();
            archiveStream.Seek(offset, SeekOrigin.Begin);
            if (!eocd.Read(archiveStream)) throw new ZipException("Invalid end of central directory record");

            if (eocd.dirOffset == uint.MaxValue) {
                var saveComment = eocd.comment;

                archiveStream.Seek(
                    offset - Zip64EndOfCentralDirectoryLocator.EOCDL64_SIZE,
                    SeekOrigin.Begin);

                var eocdl =
                    new Zip64EndOfCentralDirectoryLocator();
                if (!eocdl.Read(archiveStream))
                    throw new ZipException("Missing or invalid end of " +
                                           "central directory record locator");

                if (eocdl.dirStartDiskNumber == eocdl.totalDisks - 1) {
                    // ZIP64 eocd is entirely in current stream.
                    archiveStream.Seek(eocdl.dirOffset, SeekOrigin.Begin);
                    if (!eocd.Read(archiveStream))
                        throw new ZipException("Missing or invalid ZIP64 end of " +
                                               "central directory record");
                } else if (streamContext == null) {
                    return null;
                } else {
                    // TODO: handle EOCD64 spanning archives!
                    throw new NotImplementedException("Zip implementation does not " +
                                                      "handle end of central directory record that spans archives.");
                }

                eocd.comment = saveComment;
            }

            return eocd;
        }

        private void ResetProgressData() {
            currentFileName = null;
            currentFileNumber = 0;
            totalFiles = 0;
            currentFileBytesProcessed = 0;
            currentFileTotalBytes = 0;
            currentArchiveName = null;
            currentArchiveNumber = 0;
            totalArchives = 0;
            currentArchiveBytesProcessed = 0;
            currentArchiveTotalBytes = 0;
            fileBytesProcessed = 0;
            totalFileBytes = 0;
        }

        private void OnProgress(ArchiveProgressType progressType) {
            var e = new ArchiveProgressEventArgs(
                progressType,
                currentFileName,
                currentFileNumber >= 0 ? currentFileNumber : 0,
                totalFiles,
                currentFileBytesProcessed,
                currentFileTotalBytes,
                currentArchiveName,
                currentArchiveNumber,
                totalArchives,
                currentArchiveBytesProcessed,
                currentArchiveTotalBytes,
                fileBytesProcessed,
                totalFileBytes);
            OnProgress(e);
        }
    }
}