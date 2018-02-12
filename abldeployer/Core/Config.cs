#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (Config.cs) is part of csdeployer.
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
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using abldeployer.Compression;
using abldeployer.Lib;
using abldeployer.Resources;

namespace abldeployer.Core {

    public class Config {

        [Serializable]
        [XmlRoot("Config")]
        public class ProConfig {

            public ProConfig() {
                NumberProcessPerCore = 1;
                ArchivesCompressionLevel = CompressionLevel.Max;
            }

            #region Properties

            /// <summary>
            ///     Indicates how the deployment went
            /// </summary>
            public ReturnCode ReturnCode { get; set; }

            public DateTime DeploymentDateTime { get; set; }

            /// <summary>
            ///     Either pack or deploy
            /// </summary>
            public RunMode RunMode { get; set; }

            /// <summary>
            ///     True if we only want to simulate a deployment w/o actually doing it
            /// </summary>
            public bool IsTestMode { get; set; }

            /// <summary>
            ///     Returns the currently selected database's .pf for the current environment
            /// </summary>
            public string PfPath { get; set; }

            public string ExtraPf { get; set; }

            /// <summary>
            ///     Propath (can be null, in that case we automatically add all the folders of the source dir)
            /// </summary>
            public string IniPath { get; set; }

            public string ExtraProPath { get; set; }

            /// <summary>
            ///     Source directory
            /// </summary>
            public string SourceDirectory { get; set; }

            /// <summary>
            ///     Deployment directory
            /// </summary>
            public string TargetDirectory { get; set; }

            /// <summary>
            ///     The initial deployment directory passed to this program
            /// </summary>
            internal string InitialTargetDirectory { get; set; }

            /// <summary>
            ///     Path to prowin32.exe
            /// </summary>
            public string ProwinPath { get; set; }

            /// <summary>
            ///     Path to the deployment rules
            /// </summary>
            public string FileDeploymentRules { get; set; }

            /// <summary>
            ///     Path to the error log file to use
            /// </summary>
            public string ErrorLogFilePath { get; set; }

            /// <summary>
            ///     Path to the report directory, in which the html will be exporer
            /// </summary>
            public string OutPathReportDir { get; set; }

            /// <summary>
            ///     Path to the output xml result that can later be used in PreviousDeploymentFiles
            /// </summary>
            public string OutPathDeploymentResults { get; set; }

            /// <summary>
            ///     List of previous deployment, used to compute differences with the current source state
            /// </summary>
            public List<string> PreviousDeploymentFiles { get; set; }

            /// <summary>
            ///     True if all the files should be recompiled/deployed
            /// </summary>
            public bool ForceFullDeploy { get; set; }

            /// <summary>
            ///     True if the tool should use a MD5 sum for each file to figure out if it has changed
            /// </summary>
            public bool ComputeMd5 { get; set; }

            /// <summary>
            ///     The reference directory that will be copied into the TargetDirectory before a packaging
            /// </summary>
            public string ReferenceDirectory { get; set; }

            /// <summary>
            ///     The folder name of the networking client directory
            /// </summary>
            public string ClientNwkDirectoryName { get; set; }

            /// <summary>
            ///     The folder name of the webclient directory (if left empty, the tool will not generate the webclient dir!)
            /// </summary>
            public string ClientWcpDirectoryName { get; set; }

            // Info on the package to create
            public string WcApplicationName { get; set; }

            public string WcPackageName { get; set; }
            public string WcVendorName { get; set; }
            public string WcStartupParam { get; set; }
            public string WcLocatorUrl { get; set; }
            public string WcClientVersion { get; set; }

            /// <summary>
            ///     Path to the model of the .prowcapp to use (can be left empty and the internal model will be used)
            /// </summary>
            public string WcProwcappModelPath { get; set; }

            /// <summary>
            ///     Prowcapp version, automatically computed by this tool
            /// </summary>
            public int WcProwcappVersion { get; set; }

            // other parameters
            public string CmdLineParameters { get; set; }

            public bool CompileLocally { get; set; }
            public string PreExecutionProgram { get; set; }
            public string PostExecutionProgram { get; set; }
            public bool CompileWithDebugList { get; set; }
            public bool CompileWithXref { get; set; }
            public bool CompileWithListing { get; set; }
            public bool CompileUseXmlXref { get; set; }
            public string FileDeploymentHook { get; set; }
            public bool NeverUseProwinInBatchMode { get; set; }
            public bool ExploreRecursively { get; set; }
            public bool ForceSingleProcess { get; set; }
            public bool OnlyGenerateRcode { get; set; }
            public int NumberProcessPerCore { get; set; }
            public bool CompileForceUseOfTemp { get; set; }

            /// <summary>
            /// Create the package in the temp directory then copy it to the remote location (target dir) at the end
            /// </summary>
            public bool CreatePackageInTempDir { get; set; }

            public string OverloadFolderTemp { get; set; }
            public string OverloadFilesPatternCompilable { get; set; }
            public CompressionLevel ArchivesCompressionLevel { get; set; }
            public bool CompileUnmatchedProgressFiles { get; set; }

            /// <summary>
            ///     List of the compilation errors found
            /// </summary>
            public List<FileError> CompilationErrors { get; set; }

            /// <summary>
            ///     List all the files that were deployed from the source directory
            /// </summary>
            public List<FileDeployed> DeployedFiles { get; set; }

            [XmlIgnore]
            public string ExportXmlFile { get; set; }

            /// <summary>
            ///     The deployer for this environment (can either be a new one, or a copy of this proenv is, itself, a copy)
            /// </summary>
            internal Deployer Deployer {
                get { return _deployer ?? (_deployer = new Deployer(DeploymentRules.GetRules(FileDeploymentRules, out _ruleErrors), this)); }
            }

            /// <summary>
            ///     Returns the path to prolib.exe considering the path to prowin.exe
            /// </summary>
            internal string ProlibPath {
                get { return string.IsNullOrEmpty(ProwinPath) ? "" : Path.Combine(Path.GetDirectoryName(ProwinPath) ?? "", @"prolib.exe"); }
            }

            /// <summary>
            ///     We can use the nosplash parameter since progress 11.6
            /// </summary>
            internal bool CanProwinUseNoSplash {
                get { return (ProwinPath ?? "").Contains("116"); }
            }

            public string FolderTemp {
                get { return _folderTemp ?? (_folderTemp = CreateDirectory(Path.Combine(!string.IsNullOrEmpty(OverloadFolderTemp) ? OverloadFolderTemp : Path.Combine(Path.GetTempPath(), "AblDeployer"), Path.GetRandomFileName()))); }
            }

            internal string FilesPatternCompilable {
                get { return !string.IsNullOrEmpty(OverloadFilesPatternCompilable) ? OverloadFilesPatternCompilable : "*.p,*.w,*.t,*.cls"; }
            }

            /// <summary>
            ///     If any, will contain the errors found while reading the rules in html format
            /// </summary>
            [XmlIgnore]
            public List<Tuple<string, List<Tuple<int, string>>>> RuleErrors {
                get {
                    return new List<Tuple<string, List<Tuple<int, string>>>> {
                        new Tuple<string, List<Tuple<int, string>>>(FileDeploymentRules, _ruleErrors)
                    };
                }
            }

            public string RaisedException { get; set; }

            internal byte[] ProgramDumpTableCrc {
                get { return AblResource.DumpTableCrc; }
            }

            internal byte[] ProgramProgressRun {
                get { return AblResource.ProgressRun; }
            }

            internal byte[] ProgramDeploymentHook {
                get { return AblResource.DeploymentHook; }
            }

            internal byte[] FileContentProwcapp {
                get { return AblResource.prowcapp; }
            }

            #endregion

            #region private fields

            /// <summary>
            ///     Finding files in directories is actually a task that can take a long time,
            ///     if we get a match, we save it here so the next time we look for the file,
            ///     we already know its full path
            /// </summary>
            private Dictionary<string, string> _savedFoundFiles = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

            private List<string> _currentProPathDirList;

            private Deployer _deployer;

            private List<Tuple<int, string>> _ruleErrors;
            private string _folderTemp;

            #endregion

            #region DB Connection string

            /// <summary>
            ///     Returns the database connection string (complete with .pf + extra)
            /// </summary>
            internal string ConnectionString {
                get {
                    var connectionString = new StringBuilder();
                    if (File.Exists(PfPath)) {
                        Utils.ForEachLine(PfPath, new byte[0], (nb, line) => {
                            var commentPos = line.IndexOf("#", StringComparison.CurrentCultureIgnoreCase);
                            if (commentPos == 0)
                                return;
                            if (commentPos > 0)
                                line = line.Substring(0, commentPos);
                            line = line.Trim();
                            if (!string.IsNullOrEmpty(line)) {
                                connectionString.Append(" ");
                                connectionString.Append(line);
                            }
                        });
                        connectionString.Append(" ");
                    }
                    connectionString.Append(ExtraPf.Trim());
                    return connectionString.ToString().Replace("\n", " ").Replace("\r", "").Trim();
                }
            }

            /// <summary>
            ///     Use this method to know if the CONNECT define for the current environment connects the database in
            ///     single user mode (returns false if not or if no database connection is set)
            /// </summary>
            /// <returns></returns>
            public bool IsDatabaseSingleUser {
                get { return ConnectionString.RegexMatch(@"\s-1", RegexOptions.Singleline); }
            }

            #endregion

            #region Get ProPath

            /// <summary>
            ///     List the existing directories as they are listed in the .ini file + in the custom ProPath field,
            ///     this returns an exhaustive list of EXISTING folders and .pl files and ensure each item is present only once
            ///     It also take into account the relative path, using the BaseLocalPath (or currentFileFolder)
            /// </summary>
            internal List<string> GetProPathDirList {
                get {
                    if (_currentProPathDirList == null) {
                        var ini = new IniReader(IniPath);
                        var completeProPath = ini.GetValue("PROPATH", "");
                        completeProPath = (completeProPath + "," + ExtraProPath).Trim(',');

                        var uniqueDirList = new HashSet<string>();
                        foreach (var path in completeProPath
                            .Split(',', '\n', ';')
                            .Select(path => path.Trim())
                            .Where(path => !string.IsNullOrEmpty(path))) {
                            var thisPath = path;
                            // need to take into account relative paths
                            if (!Path.IsPathRooted(thisPath))
                                try {
                                    if (thisPath.Contains("%"))
                                        thisPath = Environment.ExpandEnvironmentVariables(thisPath);
                                    thisPath = Path.GetFullPath(Path.Combine(SourceDirectory, thisPath));
                                } catch (Exception) {
                                    //
                                }
                            if (Directory.Exists(thisPath) || File.Exists(thisPath))
                                if (!uniqueDirList.Contains(thisPath))
                                    uniqueDirList.Add(thisPath);
                        }

                        // if the user didn't set a propath, add every folder of the source directory in the propath (don't add hidden folders though)
                        if (uniqueDirList.Count == 0)
                            try {
                                foreach (var folder in Utils.EnumerateFolders(SourceDirectory, "*", SearchOption.AllDirectories))
                                    if (!uniqueDirList.Contains(folder))
                                        uniqueDirList.Add(folder);
                            } catch (Exception) {
                                //
                            }

                        // add the source directory
                        if (!uniqueDirList.Contains(SourceDirectory))
                            uniqueDirList.Add(SourceDirectory);

                        _currentProPathDirList = uniqueDirList.ToList();
                    }
                    return _currentProPathDirList;
                }
            }

            #endregion

            #region Find file

            /// <summary>
            ///     tries to find the specified file in the current propath
            ///     returns an empty string if nothing is found, otherwise returns the fullpath of the file
            /// </summary>
            internal string FindFirstFileInPropath(string fileName) {
                if (_savedFoundFiles.ContainsKey(fileName))
                    return _savedFoundFiles[fileName];

                try {
                    foreach (var item in GetProPathDirList) {
                        var curPath = Path.Combine(item, fileName);
                        if (File.Exists(curPath)) {
                            _savedFoundFiles.Add(fileName, curPath);
                            return curPath;
                        }
                    }
                } catch (Exception) {
                    // The path in invalid, well we don't really care
                }
                return "";
            }

            #endregion

            #region Private methods

            private string CreateDirectory(string dir) {
                Utils.CreateDirectory(dir);
                return dir;
            }

            #endregion
        }

        public enum ReturnCode {
            NoSet,
            Error,
            Ok,
            Canceled
        }

        public enum RunMode {
            Deployment,
            Packaging
        }
        
    }
}