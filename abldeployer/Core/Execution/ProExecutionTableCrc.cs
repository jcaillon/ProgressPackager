using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using abldeployer.Core.Config;
using abldeployer.Lib;

namespace abldeployer.Core.Execution {
    /// <summary>
    ///     Allows to output a file containing the structure of the database
    /// </summary>
    public class ProExecutionTableCrc : ProExecution {

        /// <summary>
        ///     Copy of the pro env to use
        /// </summary>
        public new IConfigExecutionDatabase ProEnv { get; private set; }

        public ProExecutionTableCrc(IConfigExecutionDatabase proEnv) : base(proEnv) {
            ProEnv = proEnv;
        }

        #region Methods

        /// <summary>
        ///     Get a list with all the tables + CRC
        /// </summary>
        /// <returns></returns>
        public List<TableCrc> GetTableCrc() {
            var output = new List<TableCrc>();
            Utils.ForEachLine(OutputPath, new byte[0], (i, line) => {
                var split = line.Split('\t');
                if (split.Length == 2)
                    output.Add(new TableCrc {
                        QualifiedTableName = split[0],
                        Crc = split[1]
                    });
            }, Encoding.Default);
            return output;
        }

        #endregion

        #region Properties

        public override ExecutionType ExecutionType {
            get { return ExecutionType.TableCrc; }
        }

        /// <summary>
        /// File to the output path that contains the CRC of each table
        /// </summary>
        public string OutputPath { get; set; }

        #endregion

        #region Override

        protected override void SetExecutionInfo() {
            OutputPath = Path.Combine(_localTempDir, "db.extract");
            SetPreprocessedVar("OutputPath", OutputPath.PreProcQuoter());

            var fileToExecute = "db_" + DateTime.Now.ToString("yyMMdd_HHmmssfff") + ".p";
            File.WriteAllBytes(Path.Combine(_localTempDir, fileToExecute), ProEnv.ProgramDumpTableCrc);
            SetPreprocessedVar("CurrentFilePath", fileToExecute.PreProcQuoter());
            SetPreprocessedVar("DatabaseExtractCandoTblType", ProEnv.DatabaseExtractCandoTblType.Trim().PreProcQuoter());
            SetPreprocessedVar("DatabaseExtractCandoTblName", ProEnv.DatabaseExtractCandoTblName.Trim().PreProcQuoter());
        }

        protected override bool CanUseBatchMode() {
            return true;
        }

        #endregion
    }
}