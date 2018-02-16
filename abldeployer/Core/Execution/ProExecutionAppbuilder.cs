using abldeployer.Core.Config;
using abldeployer.Lib;

namespace abldeployer.Core.Execution {
    internal class ProExecutionAppbuilder : ProExecution {

        public override ExecutionType ExecutionType { get { return ExecutionType.Appbuilder; } }

        public string CurrentFile { get; set; }

        protected override bool SetExecutionInfo() {

            SetPreprocessedVar("CurrentFilePath", CurrentFile.PreProcQuoter());

            return true;
        }

        public ProExecutionAppbuilder(IConfigExecution proEnv) : base(proEnv) { }
    }
}