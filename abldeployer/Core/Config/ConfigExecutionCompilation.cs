using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using abldeployer.Compression;
using abldeployer.Lib;

namespace abldeployer.Core.Config {
    
    public class ConfigExecutionCompilation : ConfigExecution {

        public ConfigExecutionCompilation() {
            ArchivesCompressionLevel = CompressionLevel.Max;
        }

        /// <summary>
        /// Deployment directory
        /// </summary>
        public string TargetDirectory { get; set; }

        /// <summary>
        /// Source directory
        /// </summary>
        public string SourceDirectory { get; set; }

        /// <summary>
        ///     The deployer for this environment (can either be a new one, or a copy of this proenv is, itself, a copy)
        /// </summary>
        internal Deployer Deployer {
            get { return _deployer ?? (_deployer = new Deployer(DeploymentRules.GetRules(FileDeploymentRules, out _ruleErrors), this)); }
        }

        private Deployer _deployer;

        private List<Tuple<int, string>> _ruleErrors;

        /// <summary>
        ///     Path to the deployment rules
        /// </summary>
        public string FileDeploymentRules { get; set; }

        public bool CompileLocally { get; set; }
        public bool CompileWithDebugList { get; set; }
        public bool CompileWithXref { get; set; }
        public bool CompileWithListing { get; set; }
        public bool CompileUseXmlXref { get; set; }

        public bool ExploreRecursively { get; set; }

        
        public CompressionLevel ArchivesCompressionLevel { get; set; }
        public bool CompileUnmatchedProgressFiles { get; set; }

        /// <summary>
        ///     Returns the path to prolib.exe considering the path to prowin.exe
        /// </summary>
        public string ProlibPath { get; set; }

        /// <summary>
        ///     Finding files in directories is actually a task that can take a long time,
        ///     if we get a match, we save it here so the next time we look for the file,
        ///     we already know its full path
        /// </summary>
        private Dictionary<string, string> _savedFoundFiles = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>
        ///     tries to find the specified file in the current propath
        ///     returns an empty string if nothing is found, otherwise returns the fullpath of the file
        /// </summary>
        public string FindFirstFileInPropath(string fileName) {
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


    }
}
