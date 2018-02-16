using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using abldeployer.Resources;

namespace abldeployer.Core.Config {

    public class ConfigDeploymentDifferential : ConfigDeployment, IConfigExecutionDatabase {

        public ConfigDeploymentDifferential() {
            DatabaseExtractCandoTblType = "T";
            DatabaseExtractCandoTblName = "*";
        }

        /// <summary>
        ///     List of previous deployment, used to compute differences with the current source state
        /// </summary>
        public List<string> PreviousDeploymentFiles { get; set; }

        /// <summary>
        ///     True if the tool should use a MD5 sum for each file to figure out if it has changed
        /// </summary>
        public bool ComputeMd5 { get; set; }

        public string DatabaseExtractCandoTblType { get; set; }
        public string DatabaseExtractCandoTblName { get; set; }

        public byte[] ProgramDumpTableCrc {
            get { return AblResource.DumpTableCrc; }
        }
    }
}
