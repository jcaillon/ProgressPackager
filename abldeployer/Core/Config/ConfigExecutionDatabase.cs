using System.Collections.Generic;
using abldeployer.Resources;

namespace abldeployer.Core.Config {
    public class ConfigExecutionDatabase : ConfigExecution, IConfigExecutionDatabase {

        public ConfigExecutionDatabase() {
            DatabaseExtractCandoTblType = "T";
            DatabaseExtractCandoTblName = "*";
        }

        public string DatabaseExtractCandoTblType { get; set; }
        public string DatabaseExtractCandoTblName { get; set; }

        public byte[] ProgramDumpTableCrc {
            get { return AblResource.DumpTableCrc; }
        }
    }
}