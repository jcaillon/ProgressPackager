#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (ProlibExtractor.cs) is part of csdeployer.
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
using abldeployer.Lib;

namespace abldeployer.Compression.Prolib {
    /// <summary>
    ///     Allows to delete files in a prolib file
    /// </summary>
    internal class ProlibExtractor {
        #region Life and death

        public ProlibExtractor(string archivePath, string prolibPath, string plExtractionFolder) {
            _archivePath = archivePath;
            _plExtractionFolder = plExtractionFolder;
            _prolibExe = new ProcessIo(prolibPath);
        }

        #endregion

        #region Methods

        /// <summary>
        ///     Extract the files given RelativePathInPack
        /// </summary>
        /// <param name="files"></param>
        public void ExtractFiles(List<string> files) {
            _prolibExe.StartInfo.WorkingDirectory = _plExtractionFolder;

            // create the subfolders needed to extract each file
            foreach (var folder in files.Select(Path.GetDirectoryName).Distinct(StringComparer.CurrentCultureIgnoreCase)) Directory.CreateDirectory(Path.Combine(_plExtractionFolder, folder));

            // for files containing a space, we don't have a choice, call delete for each...
            foreach (var file in files.Where(deploy => deploy.ContainsFast(" "))) {
                _prolibExe.Arguments = _archivePath.Quoter() + " -extract " + file.Quoter();
                if (!_prolibExe.TryDoWait(true)) 
                    throw new Exception("Error while extracting a file from a .pl", new Exception(_prolibExe.ErrorOutput.ToString()));
            }

            var remainingFiles = files.Where(deploy => !deploy.ContainsFast(" ")).ToList();
            if (remainingFiles.Count > 0) {
                // for the other files, we can use the -pf parameter
                var pfContent = new StringBuilder();
                pfContent.AppendLine("-extract");
                foreach (var file in remainingFiles) pfContent.AppendLine(file);

                var pfPath = _plExtractionFolder + Path.GetFileName(_archivePath) + "~" + Path.GetRandomFileName() + ".pf";

                File.WriteAllText(pfPath, pfContent.ToString(), Encoding.Default);

                _prolibExe.Arguments = _archivePath.Quoter() + " -pf " + pfPath.Quoter();
                if (!_prolibExe.TryDoWait(true)) 
                    throw new Exception("Error while extracting a file from a .pl", new Exception(_prolibExe.ErrorOutput.ToString()));

                if (File.Exists(pfPath))
                    File.Delete(pfPath);
            }
        }

        #endregion

        #region Private

        private ProcessIo _prolibExe;
        private string _archivePath;
        private string _plExtractionFolder;

        #endregion
    }
}