using System.Collections.Generic;
using abldeployer.Resources;

namespace abldeployer.Core.Config {
    public class ConfigExecution : IConfigExecution {

        public string ConnectionString { get; set; }

        /// <summary>
        /// Format : ALIAS,DATABASE;ALIAS2,DATABASE;...
        /// </summary>
        public string DatabaseAliasList { get; set; }

        /// <summary>
        /// Propath (can be null, in that case we automatically add all the folders of the source dir)
        /// </summary>
        public string IniPath { get; set; }

        public List<string> GetProPathDirList { get; set; }

        /// <summary>
        /// Path to prowin32.exe
        /// </summary>
        public string ProwinPath { get; set; }

        // other parameters
        public string CmdLineParameters { get; set; }
        
        public string PreExecutionProgram { get; set; }
        public string PostExecutionProgram { get; set; }

        public bool NeverUseProwinInBatchMode { get; set; }
        
        public bool CanProwinUseNoSplash { get; set; }

        public string FolderTemp { get; set; }
        
        public byte[] ProgramProgressRun {
            get { return AblResource.ProgressRun; }
        }
        
    }
}