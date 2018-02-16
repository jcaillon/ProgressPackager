﻿#region header

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
using abldeployer.Core;
using abldeployer.Core.Config;
using abldeployer.Lib;

namespace csdeployer.Core {
    
    [Serializable]
    [XmlRoot("Config")]
    public class CsDeployerConfigXml {

        public CsDeployerConfigXml() {
            NumberProcessPerCore = 1;
            ArchivesCompressionLevel = CompressionLevel.Max;
        }
        
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
        /// List of the compilation errors found
        /// </summary>
        public List<FileError> CompilationErrors { get; set; }

        /// <summary>
        /// List all the files that were deployed from the source directory
        /// </summary>
        public List<FileDeployed> DeployedFiles { get; set; }
        

    }
}