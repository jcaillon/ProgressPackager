#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (ConcatStream.cs) is part of csdeployer.
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

namespace abldeployer.Compression.Zip {
    /// <summary>
    ///     Used to trick a DeflateStream into reading from or writing to
    ///     a series of (chunked) streams instead of a single steream.
    /// </summary>
    internal class ConcatStream : Stream {
        private long length;
        private Action<ConcatStream> nextStreamHandler;
        private long position;

        public Stream Source { get; set; }

        public override bool CanRead {
            get { return true; }
        }

        public override bool CanWrite {
            get { return true; }
        }

        public override bool CanSeek {
            get { return false; }
        }

        public override long Length {
            get { return length; }
        }

        public override long Position {
            get { return position; }
            set { throw new NotSupportedException(); }
        }

        public ConcatStream(Action<ConcatStream> nextStreamHandler) {
            if (nextStreamHandler == null) throw new ArgumentNullException("nextStreamHandler");

            this.nextStreamHandler = nextStreamHandler;
            length = long.MaxValue;
        }

        public override int Read(byte[] buffer, int offset, int count) {
            if (Source == null) nextStreamHandler(this);

            count = (int) Math.Min(count, length - position);

            var bytesRemaining = count;
            while (bytesRemaining > 0) {
                if (Source == null) throw new InvalidOperationException();

                var partialCount = (int) Math.Min(bytesRemaining,
                    Source.Length - Source.Position);

                if (partialCount == 0) {
                    nextStreamHandler(this);
                    continue;
                }

                partialCount = Source.Read(
                    buffer, offset + count - bytesRemaining, partialCount);
                bytesRemaining -= partialCount;
                position += partialCount;
            }

            return count;
        }

        public override void Write(byte[] buffer, int offset, int count) {
            if (Source == null) nextStreamHandler(this);

            var bytesRemaining = count;
            while (bytesRemaining > 0) {
                if (Source == null) throw new InvalidOperationException();

                var partialCount = (int) Math.Min(bytesRemaining,
                    Math.Max(0, length - Source.Position));

                if (partialCount == 0) {
                    nextStreamHandler(this);
                    continue;
                }

                Source.Write(
                    buffer, offset + count - bytesRemaining, partialCount);
                bytesRemaining -= partialCount;
                position += partialCount;
            }
        }

        public override void Flush() {
            if (Source != null) Source.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        public override void SetLength(long value) {
            length = value;
        }

        public override void Close() {
            if (Source != null) Source.Close();
        }
    }
}