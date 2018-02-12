#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (CabWorker.cs) is part of csdeployer.
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
using System.Runtime.InteropServices;

namespace abldeployer.Compression.Cab {
    internal abstract class CabWorker : IDisposable {
        internal const string CabStreamName = "%%CAB%%";

        private byte[] buf;

        protected long currentArchiveBytesProcessed;
        protected string currentArchiveName;
        protected short currentArchiveNumber;
        protected long currentArchiveTotalBytes;
        protected long currentFileBytesProcessed;

        // Progress data
        protected string currentFileName;

        protected int currentFileNumber;
        protected long currentFileTotalBytes;
        protected short currentFolderNumber;
        protected long currentFolderTotalBytes;

        private GCHandle erfHandle;
        protected long fileBytesProcessed;

        protected short totalArchives;
        protected long totalFileBytes;
        protected int totalFiles;

        public CabEngine CabEngine { get; private set; }

        internal NativeMethods.ERF Erf { get; private set; }

        internal GCHandle ErfHandle {
            get { return erfHandle; }
        }

        internal HandleManager<Stream> StreamHandles { get; private set; }

        internal bool SuppressProgressEvents { get; set; }

        internal IDictionary<string, short> CabNumbers { get; private set; }

        internal string NextCabinetName { get; set; }

        internal Stream CabStream { get; set; }

        internal Stream FileStream { get; set; }

        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
        protected CabWorker(CabEngine cabEngine) {
            CabEngine = cabEngine;
            StreamHandles = new HandleManager<Stream>();
            Erf = new NativeMethods.ERF();
            erfHandle = GCHandle.Alloc(Erf, GCHandleType.Pinned);
            CabNumbers = new Dictionary<string, short>(1);

            // 32K seems to be the size of the largest chunks processed by cabinet.dll.
            // But just in case, this buffer will auto-enlarge.
            buf = new byte[32768];
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~CabWorker() {
            Dispose(false);
        }

        protected void ResetProgressData() {
            currentFileName = null;
            currentFileNumber = 0;
            totalFiles = 0;
            currentFileBytesProcessed = 0;
            currentFileTotalBytes = 0;
            currentFolderNumber = 0;
            currentFolderTotalBytes = 0;
            currentArchiveName = null;
            currentArchiveNumber = 0;
            totalArchives = 0;
            currentArchiveBytesProcessed = 0;
            currentArchiveTotalBytes = 0;
            fileBytesProcessed = 0;
            totalFileBytes = 0;
        }

        protected void OnProgress(ArchiveProgressType progressType) {
            if (!SuppressProgressEvents) {
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
                CabEngine.ReportProgress(e);
            }
        }

        internal IntPtr CabAllocMem(int byteCount) {
            var memPointer = Marshal.AllocHGlobal((IntPtr) byteCount);
            return memPointer;
        }

        internal void CabFreeMem(IntPtr memPointer) {
            Marshal.FreeHGlobal(memPointer);
        }

        internal int CabOpenStream(string path, int openFlags, int shareMode) {
            int err;
            return CabOpenStreamEx(path, openFlags, shareMode, out err, IntPtr.Zero);
        }

        internal virtual int CabOpenStreamEx(string path, int openFlags, int shareMode, out int err, IntPtr pv) {
            path = path.Trim();
            var stream = CabStream;
            CabStream = new DuplicateStream(stream);
            var streamHandle = StreamHandles.AllocHandle(stream);
            err = 0;
            return streamHandle;
        }

        internal int CabReadStream(int streamHandle, IntPtr memory, int cb) {
            int err;
            return CabReadStreamEx(streamHandle, memory, cb, out err, IntPtr.Zero);
        }

        internal virtual int CabReadStreamEx(int streamHandle, IntPtr memory, int cb, out int err, IntPtr pv) {
            var stream = StreamHandles[streamHandle];
            var count = cb;
            if (count > buf.Length) buf = new byte[count];
            count = stream.Read(buf, 0, count);
            Marshal.Copy(buf, 0, memory, count);
            err = 0;
            return count;
        }

        internal int CabWriteStream(int streamHandle, IntPtr memory, int cb) {
            int err;
            return CabWriteStreamEx(streamHandle, memory, cb, out err, IntPtr.Zero);
        }

        internal virtual int CabWriteStreamEx(int streamHandle, IntPtr memory, int cb, out int err, IntPtr pv) {
            var stream = StreamHandles[streamHandle];
            var count = cb;
            if (count > buf.Length) buf = new byte[count];
            Marshal.Copy(memory, buf, 0, count);
            stream.Write(buf, 0, count);
            err = 0;
            return cb;
        }

        internal int CabCloseStream(int streamHandle) {
            int err;
            return CabCloseStreamEx(streamHandle, out err, IntPtr.Zero);
        }

        internal virtual int CabCloseStreamEx(int streamHandle, out int err, IntPtr pv) {
            StreamHandles.FreeHandle(streamHandle);
            err = 0;
            return 0;
        }

        internal int CabSeekStream(int streamHandle, int offset, int seekOrigin) {
            int err;
            return CabSeekStreamEx(streamHandle, offset, seekOrigin, out err, IntPtr.Zero);
        }

        internal virtual int CabSeekStreamEx(int streamHandle, int offset, int seekOrigin, out int err, IntPtr pv) {
            var stream = StreamHandles[streamHandle];
            offset = (int) stream.Seek(offset, (SeekOrigin) seekOrigin);
            err = 0;
            return offset;
        }

        /// <summary>
        ///     Disposes of resources allocated by the cabinet engine.
        /// </summary>
        /// <param name="disposing">
        ///     If true, the method has been called directly or indirectly by a user's code,
        ///     so managed and unmanaged resources will be disposed. If false, the method has been called by the
        ///     runtime from inside the finalizer, and only unmanaged resources will be disposed.
        /// </param>
        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
        protected virtual void Dispose(bool disposing) {
            if (disposing) {
                if (CabStream != null) {
                    CabStream.Close();
                    CabStream = null;
                }

                if (FileStream != null) {
                    FileStream.Close();
                    FileStream = null;
                }
            }

            if (erfHandle.IsAllocated) erfHandle.Free();
        }

        protected void CheckError(bool extracting) {
            if (Erf.Error)
                throw new CabException(
                    Erf.Oper,
                    Erf.Type,
                    CabException.GetErrorMessage(Erf.Oper, Erf.Type, extracting));
        }
    }
}