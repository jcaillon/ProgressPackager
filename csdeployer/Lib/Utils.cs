#region header
// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (Utils.cs) is part of csdeployer.
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
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using csdeployer.Core;
using csdeployer._Resource;

namespace csdeployer.Lib {
    /// <summary>
    /// Class that exposes utility methods
    /// </summary>
    internal static class Utils {
        #region File manipulation wrappers

        /// <summary>
        /// File write all bytes
        /// </summary>
        public static bool FileWriteAllBytes(string path, byte[] bytes) {
            try {
                File.WriteAllBytes(path, bytes);
                return true;
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Impossible d'écrire dans le fichier " + path.Quoter());
            }
            return false;
        }

        /// <summary>
        /// File write all text
        /// </summary>
        public static bool FileWriteAllText(string path, string text, Encoding encoding = null) {
            try {
                if (encoding == null)
                    encoding = Encoding.Default;
                File.WriteAllText(path, text, encoding);
                return true;
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Impossible d'écrire dans le fichier " + path.Quoter());
            }
            return false;
        }

        /// <summary>
        /// File write all text
        /// </summary>
        public static bool FileAppendAllText(string path, string text, Encoding encoding = null) {
            try {
                if (encoding == null)
                    encoding = Encoding.Default;
                File.AppendAllText(path, text, encoding);
                return true;
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Impossible d'écrire dans le fichier " + path.Quoter());
            }
            return false;
        }



        /// <summary>
        /// Delete a dir, recursively
        /// </summary>
        public static bool DeleteDirectory(string path, bool recursive) {
            try {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    return true;
                Directory.Delete(path, recursive);
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Impossible de supprimer le dossier " + path.Quoter());
                return false;
            }
            return true;
        }

        /// <summary>
        /// Delete a file
        /// </summary>
        public static bool DeleteFile(string path) {
            try {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return true;
                File.Delete(path);
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Impossible de supprimer le fichier " + path.Quoter());
                return false;
            }
            return true;
        }

        /// <summary>
        /// Creates the directory, can apply attributes
        /// </summary>
        public static bool CreateDirectory(string path, FileAttributes attributes = FileAttributes.Directory) {
            try {
                if (Directory.Exists(path))
                    return true;
                var dirInfo = Directory.CreateDirectory(path);
                dirInfo.Attributes |= attributes;
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Impossible de créer le dossier " + path.Quoter());
                return false;
            }
            return true;
        }

        /// <summary>
        /// Same as Directory.EnumerateDirectories but doesn't list hidden folders
        /// </summary>
        public static IEnumerable<string> EnumerateFolders(string folderPath, string pattern, SearchOption options) {
            var hiddenDirList = new List<string>();
            foreach (var dir in Directory.EnumerateDirectories(folderPath, pattern, options)) {
                if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.Hidden)) {
                    hiddenDirList.Add(dir);
                }
                bool hidden = false;
                foreach (var hiddenDir in hiddenDirList) {
                    if (dir.StartsWith(hiddenDir)) {
                        hidden = true;
                    }
                }
                if (!hidden)
                    yield return dir;
            }
        }

        /// <summary>
        /// Same as Directory.EnumerateFiles but doesn't list files in hidden folders
        /// </summary>
        public static IEnumerable<string> EnumerateFiles(string folderPath, string pattern, SearchOption options) {
            foreach (var file in Directory.EnumerateFiles(folderPath, pattern, SearchOption.TopDirectoryOnly)) {
                yield return file;
            }
            if (options == SearchOption.AllDirectories) {
                foreach (var folder in EnumerateFolders(folderPath, "*", SearchOption.AllDirectories)) {
                    foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)) {
                        yield return file;
                    }
                }
            }
        }

        #endregion

        #region Read a configuration file

        /// <summary>
        /// Reads all the line of either the filePath (if the file exists) or from byte array dataResources,
        /// Apply the action toApplyOnEachLine to each line
        /// Uses encoding as the Encoding to read the file or convert the byte array to a string
        /// Uses the char # as a comment in the file
        /// </summary>
        public static bool ForEachLine(string filePath, byte[] dataResources, Action<int, string> toApplyOnEachLine, Encoding encoding, Action<Exception> onException) {
            bool wentOk = true;
            try {
                SubForEachLine(filePath, dataResources, toApplyOnEachLine, encoding);
            } catch (Exception e) {
                wentOk = false;
                onException(e);

                // read default file, if it fails then we can't do much but to throw an exception anyway...
                if (dataResources != null)
                    SubForEachLine(null, dataResources, toApplyOnEachLine, encoding);
            }
            return wentOk;
        }

        private static void SubForEachLine(string filePath, byte[] dataResources, Action<int, string> toApplyOnEachLine, Encoding encoding) {
            string line;
            // to apply on each line
            Action<TextReader> action = reader => {
                int i = 0;
                while ((line = reader.ReadLine()) != null) {
                    if (line.Length > 0 && line[0] != '#')
                        toApplyOnEachLine(i, line);
                    i++;
                }
            };

            // either read from the file or from the byte array
            if (!String.IsNullOrEmpty(filePath) && File.Exists(filePath)) {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    using (var reader = new StreamReader(fileStream, encoding)) {
                        action(reader);
                    }
                }
            } else {
                // we use the default encoding for the resoures since we can control the encoding on this...
                using (StringReader reader = new StringReader(Encoding.Default.GetString(dataResources))) {
                    action(reader);
                }
            }
        }

        #endregion

        #region Misc

        /// <summary>
        /// Allows to know how many files of each file type there is
        /// </summary>
        public static Dictionary<FileExt, int> GetNbFilesPerType(List<string> files) {
            Dictionary<FileExt, int> output = new Dictionary<FileExt, int>();

            foreach (var file in files) {
                FileExt fileExt;
                if (!Enum.TryParse((Path.GetExtension(file) ?? "").Replace(".", ""), true, out fileExt))
                    fileExt = FileExt.Unknow;
                if (output.ContainsKey(fileExt))
                    output[fileExt]++;
                else
                    output.Add(fileExt, 1);
            }

            return output;
        }

        /// <summary>
        /// Get the time elapsed in a human readable format
        /// </summary>
        public static string ConvertToHumanTime(TimeSpan t) {
            if (t.Hours > 0)
                return string.Format("{0:D2}h:{1:D2}m:{2:D2}s", t.Hours, t.Minutes, t.Seconds);
            if (t.Minutes > 0)
                return string.Format("{0:D2}m:{1:D2}s", t.Minutes, t.Seconds);
            if (t.Seconds > 0)
                return string.Format("{0:D2}s", t.Seconds);
            return string.Format("{0:D3}ms", t.Milliseconds);
        }

        /// <summary>
        /// Computes the MD5 hash of the given string
        /// </summary>
        public static string CalculateMd5Hash(string input) {
            MD5 md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++) {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns the name of the image to use for a particular extension
        /// </summary>
        public static string GetExtensionImage(string ext, bool exist = false) {
            if (exist)
                return "Ext" + ext;
            FileExt fileExt;
            if (!Enum.TryParse(ext, true, out fileExt))
                fileExt = FileExt.Unknow;
            return "Ext" + fileExt;
        }

        /// <summary>
        /// Returns the image from the resources
        /// </summary>
        public static Image GetImageFromStr(string typeStr) {
            Image tryImg = (Image) ImageResources.ResourceManager.GetObject(typeStr);
            return tryImg ?? ImageResources.ExtError;
        }

        public static string GetImageNameOf(string name) {
            return ImageResources.ResourceManager.GetObject(name) != null ? name : "Error";
        }

        #endregion
    }
}