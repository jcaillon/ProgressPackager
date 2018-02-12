﻿#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (ProlibDelete.cs) is part of csdeployer.
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
using System.IO;
using System.Linq;
using System.Text;
using abldeployer.Core;
using abldeployer.Lib;

namespace abldeployer.Compression.Prolib {
    /// <summary>
    ///     Allows to delete files in a prolib file
    /// </summary>
    internal class ProlibDelete : IPackager {
        #region Life and death

        public ProlibDelete(string archivePath, string prolibPath) {
            _archivePath = archivePath;
            _prolibExe = new ProcessIo(prolibPath);
        }

        #endregion

        #region Methods

        public void PackFileSet(IDictionary<string, FileToDeployInPack> files, CompressionLevel compLevel, EventHandler<ArchiveProgressEventArgs> progressHandler) {
            var archiveFolder = Path.GetDirectoryName(_archivePath);
            if (!string.IsNullOrEmpty(archiveFolder))
                _prolibExe.StartInfo.WorkingDirectory = archiveFolder;

            // for files containing a space, we don't have a choice, call delete for each...
            foreach (var file in files.Values.Where(deploy => deploy.RelativePathInPack.ContainsFast(" "))) {
                _prolibExe.Arguments = _archivePath.Quoter() + " -delete " + file.RelativePathInPack.Quoter();
                var isOk = _prolibExe.TryDoWait(true);
                if (progressHandler != null)
                    progressHandler(this, new ArchiveProgressEventArgs(ArchiveProgressType.FinishFile, file.RelativePathInPack, isOk ? null : new Exception(_prolibExe.ErrorOutput.ToString())));
            }

            var remainingFiles = files.Values.Where(deploy => !deploy.RelativePathInPack.ContainsFast(" ")).ToList();
            if (remainingFiles.Count > 0) {
                // for the other files, we can use the -pf parameter
                var pfContent = new StringBuilder();
                pfContent.AppendLine("-delete");
                foreach (var file in remainingFiles)
                    pfContent.AppendLine(file.RelativePathInPack);

                Exception ex = null;
                var pfPath = _archivePath + "~" + Path.GetRandomFileName() + ".pf";

                try {
                    File.WriteAllText(pfPath, pfContent.ToString(), Encoding.Default);
                } catch (Exception e) {
                    ex = e;
                }

                _prolibExe.Arguments = _archivePath.Quoter() + " -pf " + pfPath.Quoter();
                var isOk = _prolibExe.TryDoWait(true);

                try {
                    if (ex == null)
                        File.Delete(pfPath);
                } catch (Exception e) {
                    ex = e;
                }

                if (progressHandler != null)
                    foreach (var file in files.Values.Where(deploy => !deploy.RelativePathInPack.ContainsFast(" ")))
                        progressHandler(this, new ArchiveProgressEventArgs(ArchiveProgressType.FinishFile, file.RelativePathInPack, ex ?? (isOk ? null : new Exception(_prolibExe.ErrorOutput.ToString()))));
            }
        }

        #endregion

        #region Private

        private ProcessIo _prolibExe;
        private string _archivePath;

        #endregion
    }
}