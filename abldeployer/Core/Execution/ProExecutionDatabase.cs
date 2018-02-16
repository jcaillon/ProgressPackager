using System;
using System.IO;
using abldeployer.Lib;

namespace abldeployer.Core.Execution {
    /// <summary>
    /// Allows to output a file containing the structure of the database
    /// </summary>
    internal class ProExecutionDatabase : ProExecution {

        #region Properties

        public override ExecutionType ExecutionType { get { return ExecutionType.Database; } }

        /// <summary>
        /// File to the output path that contains the structure of the database
        /// </summary>
        public string OutputPath { get; set; }

        #endregion

        #region Override

        protected override bool SetExecutionInfo() {

            OutputPath = Path.Combine(_localTempDir, "db.extract");
            SetPreprocessedVar("OutputPath", OutputPath.PreProcQuoter());

            var fileToExecute = "db_" + DateTime.Now.ToString("yyMMdd_HHmmssfff") + ".p";
            if (!Utils.FileWriteAllBytes(Path.Combine(_localTempDir, fileToExecute), DataResources.DumpDatabase))
                return false;
            SetPreprocessedVar("CurrentFilePath", fileToExecute.PreProcQuoter());

            return true;
        }
        
        protected override bool CanUseBatchMode() {
            return true;
        }

        #endregion
    }
}