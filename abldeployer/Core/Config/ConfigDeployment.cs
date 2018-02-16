using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using abldeployer.Compression;
using abldeployer.Resources;

namespace abldeployer.Core.Config {

    public class ConfigDeployment : ConfigExecutionMultiCompilation {

        /// <summary>
        ///     List all the files that were deployed from the source directory
        /// </summary>
        public List<FileDeployed> DeployedFiles { get; set; }

        /// <summary>
        ///     List of the compilation errors found
        /// </summary>
        public List<FileError> CompilationErrors { get; set; }

        public string FilesPatternCompilable { get; set; }

        /// <summary>
        /// True if all the files should be recompiled/deployed
        /// </summary>
        public bool ForceFullDeploy { get; set; }

        /// <summary>
        ///     True if we only want to simulate a deployment w/o actually doing it
        /// </summary>
        public bool IsTestMode { get; set; }

        /// <summary>
        ///     Indicates how the deployment went
        /// </summary>
        public ReturnCode ReturnCode { get; set; }

        public DateTime DeploymentDateTime { get; set; }


        public string FileDeploymentHook { get; set; }

        
        public bool IsDatabaseSingleUser { get; set; }

        public byte[] ProgramDeploymentHook {
            get { return AblResource.DeploymentHook; }
        }

    }
}
