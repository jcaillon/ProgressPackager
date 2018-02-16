using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using abldeployer.Resources;

namespace abldeployer.Core.Config {

    public class ConfigDeploymentPackaging : ConfigDeploymentDifferential {
        
        /// <summary>
        ///     The initial deployment directory passed to this program
        /// </summary>
        internal string InitialTargetDirectory { get; set; }

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

        /// <summary>
        /// Create the package in the temp directory then copy it to the remote location (target dir) at the end
        /// </summary>
        public bool CreatePackageInTempDir { get; set; }

        internal byte[] FileContentProwcapp {
            get { return AblResource.prowcapp; }
        }
    }
}
