using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace csdeployer.Lib {

    /// <summary>
    /// PInvoke wrapper for CopyEx
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa363852.aspx
    /// </summary>
    internal class XCopy {

        private int _isCancelled;
        private int _filePercentCompleted;

        public XCopy() {
            _isCancelled = 0;
        }

        /// <summary>
        /// Copies the file asynchronously
        /// </summary>
        /// <param name="source">the source path</param>
        /// <param name="destination">the destination path</param>
        /// <param name="nobuffering">Buffering status</param>
        public void CopyAsync(string source, string destination, bool nobuffering) {
            try {
                // since we needed an async copy ..
                Action action = () => CopyInternal(source, destination, nobuffering);
                Task task = new Task(action);
                task.Start();
            } catch (AggregateException ex) {
                // handle the inner exception since exception thrown from task are wrapped in aggregate exception
                OnCompleted(ProgressCopy.CopyCompletedType.Exception, ex.InnerException);
            } catch (Exception ex) {
                OnCompleted(ProgressCopy.CopyCompletedType.Exception, ex);
            }
        }

        /// <summary>
        /// Event which will notify the subscribers if the copy gets completed
        /// There are three scenarios in which completed event will be thrown when
        /// 1.Copy succeeded
        /// 2.Copy aborted.
        /// 3.Any exception occurred.
        /// These information can be obtained from the Event args.
        /// </summary>
        public event EventHandler<ProgressCopy.EndEventArgs> Completed;

        /// <summary>
        /// Event which will notify the subscribers if there is any progress change while copying.
        /// This will indicate the progress percentage in its event args.
        /// </summary>
        public event EventHandler<ProgressChangedEventArgs> ProgressChanged;

        /// <summary>
        /// Aborts the copy asynchronously and throws Completed event when done.
        /// User may not want to wait for completed event in case of Abort since 
        /// the event will tell that copy has been aborted.
        /// </summary>
        public void AbortCopyAsync() {

            //setting this will cancel an operation since we pass the
            //reference to copyfileex and it will periodically check for this.
            //otherwise also We can check for iscancelled on onprogresschanged and return 
            //Progress_cancelled .
            _isCancelled = 1;

            Action completedEvent = () => {
                // wait for some time because we ll not know when IsCancelled is set , at what time windows stops copying.
                // so after sometime this may become valid .
                Thread.Sleep(500);
                //do we need to wait for some time and send completed event.
                OnCompleted(ProgressCopy.CopyCompletedType.Aborted);
                //reset the value , otherwise if we try to copy again since value is 1 , 
                //it thinks that its aborted and wont allow to copy.
                _isCancelled = 0;
            };

            Task completedTask = new Task(completedEvent);
            completedTask.Start();
        }
        
        /// <summary>
        /// Copies the file using asynchronous task
        /// </summary>
        /// <param name="source">the source path</param>
        /// <param name="destination">the destination path</param>
        /// <param name="nobuffering">Buffering status</param>
        private void CopyInternal(string source, string destination, bool nobuffering) {
            CopyFileFlags copyFileFlags = CopyFileFlags.COPY_FILE_RESTARTABLE;

            if (nobuffering) {
                copyFileFlags |= CopyFileFlags.COPY_FILE_NO_BUFFERING;
            }

            try {
                //call win32 api.
                bool result = CopyFileEx(source, destination, CopyProgressHandler, IntPtr.Zero, ref _isCancelled, copyFileFlags);
                if (!result) {
                    //when ever we get the result as false it means some error occurred so get the last win 32 error.
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            } catch (Exception ex) {
                //the message will contain the requested operation was aborted when the file copy
                //was canceled. so we explicitly check for that and do a graceful exit
                if (!ex.Message.Contains("aborted")) {
                    OnCompleted(ProgressCopy.CopyCompletedType.Exception, ex.InnerException);
                }
            }
        }

        private void OnProgressChanged(double percent) {
            // only raise an event when progress has changed
            if ((int)percent > _filePercentCompleted) {
                _filePercentCompleted = (int)percent;
                var handler = ProgressChanged;
                if (handler != null) {
                    handler(this, new ProgressChangedEventArgs(_filePercentCompleted, null));
                }
            }
        }

        private void OnCompleted(ProgressCopy.CopyCompletedType type, Exception exception = null) {
            var handler = Completed;
            if (handler != null) {
                handler(this, new ProgressCopy.EndEventArgs(type, exception));
            }
        }

        #region PInvoke

        /// <summary>
        /// Delegate which will be called by Win32 API for progress change
        /// </summary>
        /// <param name="total">the total size</param>
        /// <param name="transferred">the transferred size</param>
        /// <param name="streamSize">size of the stream</param>
        /// <param name="streamByteTrans"></param>
        /// <param name="dwStreamNumber">stream number</param>
        /// <param name="reason">reason for callback</param>
        /// <param name="hSourceFile">the source file handle</param>
        /// <param name="hDestinationFile">the destination file handle</param>
        /// <param name="lpData">data passed by users</param>
        /// <returns>indicating whether to continue or do something else.</returns>
        private CopyProgressResult CopyProgressHandler(long total, long transferred, long streamSize, long streamByteTrans, uint dwStreamNumber,
            CopyProgressCallbackReason reason, IntPtr hSourceFile, IntPtr hDestinationFile, IntPtr lpData) {
            //when a chunk is finished call the progress changed.
            if (reason == CopyProgressCallbackReason.CALLBACK_CHUNK_FINISHED) {
                OnProgressChanged((transferred / (double)total) * 100.0);
            }

            //transfer completed
            if (transferred >= total) {
                OnCompleted(ProgressCopy.CopyCompletedType.Succeeded);
            }

            return CopyProgressResult.PROGRESS_CONTINUE;
        }
        
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CopyFileEx(string lpExistingFileName, string lpNewFileName, CopyProgressRoutine lpProgressRoutine, IntPtr lpData, ref Int32 pbCancel, CopyFileFlags dwCopyFlags);

        private delegate CopyProgressResult CopyProgressRoutine(long totalFileSize, long totalBytesTransferred, long streamSize, long streamBytesTransferred, uint dwStreamNumber, CopyProgressCallbackReason dwCallbackReason,
            IntPtr hSourceFile, IntPtr hDestinationFile, IntPtr lpData);

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum CopyProgressResult : uint {
            PROGRESS_CONTINUE = 0,
            PROGRESS_CANCEL = 1,
            PROGRESS_STOP = 2,
            PROGRESS_QUIET = 3
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum CopyProgressCallbackReason : uint {
            CALLBACK_CHUNK_FINISHED = 0x00000000,
            CALLBACK_STREAM_SWITCH = 0x00000001
        }

        [Flags]
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum CopyFileFlags : uint {
            COPY_FILE_FAIL_IF_EXISTS = 0x00000001,
            COPY_FILE_NO_BUFFERING = 0x00001000,
            COPY_FILE_RESTARTABLE = 0x00000002,
            COPY_FILE_OPEN_SOURCE_FOR_WRITE = 0x00000004,
            COPY_FILE_ALLOW_DECRYPTED_DESTINATION = 0x00000008
        }

        #endregion

    }

}
